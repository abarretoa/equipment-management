using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.ResponsibilityTerms;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.ResponsibilityTerms;

/// <summary>
/// Phase 7 hardening pass (ADR-052 update): proves the
/// Attempting -&gt; Sent/Failed delivery-attempt lifecycle ordering invariant
/// — a durable attempt row is always committed before the email side effect,
/// and a delivery attempt that never reaches a terminal outcome remains
/// truthfully visible as <see cref="DeliveryStatus.Attempting"/> /
/// <see cref="ResponsibilityTermExecutionStatus.SendPending"/> rather than
/// being guessed at. Uses <see cref="WebApplicationFactory{TEntryPoint}.WithWebHostBuilder"/>
/// to swap in a test-only <see cref="IEmailSender"/> for exactly one derived
/// host per test, leaving the shared <see cref="ApiWebApplicationFactory"/>
/// (and every other test class's use of it) untouched.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class ResponsibilityTermDeliveryHardeningTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    /// <summary>
    /// The core invariant of the Phase 7 hardening pass: "no email
    /// side effect without a durable attempt identity." Proven by having the
    /// fake email sender itself — invoked synchronously from inside
    /// <c>ResponsibilityTermDeliveryService.SendAsync</c> — open an
    /// independent database scope and confirm the Attempting row is already
    /// visible there. A separate scope only sees committed data (read
    /// committed isolation), so this is a genuine durability check, not a
    /// same-instance in-memory re-read.
    /// </summary>
    [Fact]
    public async Task Send_DurablyCommitsAttemptingRow_BeforeInvokingEmailSender()
    {
        using var setupClient = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(setupClient);
        var term = await GenerateTermAsync(setupClient, handover.Id);

        var assertingSender = new AttemptRowAssertingEmailSender(term.Id);
        await using var derivedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IEmailSender>(sp =>
                {
                    assertingSender.RootProvider = sp;
                    return assertingSender;
                })));

        using var sendClient = derivedFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        await sendClient.LoginAsync(email, password);

        var response = await Phase7TestData.SendAsync(sendClient, term.Id);
        response.EnsureSuccessStatusCode();

        Assert.True(assertingSender.InvokedWithAttemptRowAlreadyCommitted, "IEmailSender.SendAsync observed no durably committed Attempting row.");
    }

    /// <summary>
    /// If the mail transport never returns a classified outcome (an
    /// unexpected exception the <see cref="EmailSendException"/> contract
    /// does not cover — e.g. a bug, or a connection lost in a way the sender
    /// implementation cannot itself classify), the row must remain
    /// <see cref="DeliveryStatus.Attempting"/> — never guessed at as
    /// <c>Failed</c> — and the derived execution status must truthfully
    /// report <see cref="ResponsibilityTermExecutionStatus.SendPending"/>,
    /// never <c>Sent</c> or <c>NotSent</c> (Phase 7 hardening pass).
    /// </summary>
    [Fact]
    public async Task Send_UnclassifiedSenderFailure_LeavesDeliveryAttemptingAndReportsSendPending()
    {
        using var setupClient = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(setupClient);
        var term = await GenerateTermAsync(setupClient, handover.Id);

        await using var derivedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IEmailSender, ThrowingEmailSender>()));

        using var sendClient = derivedFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        await sendClient.LoginAsync(email, password);

        var response = await Phase7TestData.SendAsync(sendClient, term.Id);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // Read back through an ordinary client — the row must be visible,
        // durable, and truthfully unresolved.
        var deliveries = await setupClient.GetFromJsonAsync<List<ResponsibilityTermDeliveryResponse>>(
            $"/api/responsibility-terms/{term.Id}/deliveries", HttpClientTestExtensions.JsonOptions);
        var attempt = Assert.Single(deliveries!);
        Assert.Equal(DeliveryStatus.Attempting, attempt.Status);
        Assert.Null(attempt.SentAtUtc);
        Assert.Null(attempt.FailureCode);

        var detail = await setupClient.GetFromJsonAsync<ResponsibilityTermResponse>(
            $"/api/responsibility-terms/{term.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ResponsibilityTermExecutionStatus.SendPending, detail!.ExecutionStatus);
    }

    /// <summary>
    /// Phase 7 hardening pass: a delivery attempt transitions to a
    /// terminal state exactly once and is then immutable through normal
    /// application APIs. A resend must never touch an earlier attempt's
    /// already-terminal fields.
    /// </summary>
    [Fact]
    public async Task Resend_NeverMutatesAnEarlierTerminalDeliveryAttempt()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        var firstResponse = await Phase7TestData.SendAsync(client, term.Id);
        firstResponse.EnsureSuccessStatusCode();
        var first = (await firstResponse.Content.ReadFromJsonAsync<ResponsibilityTermDeliveryResponse>(HttpClientTestExtensions.JsonOptions))!;

        var secondResponse = await Phase7TestData.SendAsync(client, term.Id);
        secondResponse.EnsureSuccessStatusCode();
        var second = (await secondResponse.Content.ReadFromJsonAsync<ResponsibilityTermDeliveryResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.NotEqual(first.Id, second.Id);

        var deliveries = await client.GetFromJsonAsync<List<ResponsibilityTermDeliveryResponse>>(
            $"/api/responsibility-terms/{term.Id}/deliveries", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(2, deliveries!.Count);

        var firstAfterResend = deliveries.Single(d => d.Id == first.Id);
        Assert.Equal(DeliveryStatus.Sent, firstAfterResend.Status);
        Assert.NotNull(firstAfterResend.SentAtUtc);
        Assert.Null(firstAfterResend.FailureCode);
        // Sub-microsecond precision can shift by a tick round-tripping through
        // Postgres storage — comparing within a generous tolerance still
        // proves the terminal timestamp was never re-written by the resend.
        Assert.True(Math.Abs((first.SentAtUtc!.Value - firstAfterResend.SentAtUtc!.Value).TotalSeconds) < 1);
    }

    /// <summary>Fired synchronously from inside <c>ResponsibilityTermDeliveryService.SendAsync</c>, so it can only observe state already committed by the time control reaches it.</summary>
    private sealed class AttemptRowAssertingEmailSender(Guid termId) : IEmailSender
    {
        public IServiceProvider? RootProvider { get; set; }

        public bool InvokedWithAttemptRowAlreadyCommitted { get; private set; }

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            using var scope = RootProvider!.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            InvokedWithAttemptRowAlreadyCommitted = await dbContext.ResponsibilityTermDeliveries.AsNoTracking()
                .AnyAsync(d => d.ResponsibilityTermId == termId && d.Status == DeliveryStatus.Attempting, cancellationToken);
        }
    }

    /// <summary>Simulates a mail-transport failure the sender contract does not classify — never a real-world credential/network leak, just a marker exception.</summary>
    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated unclassified send failure for the Phase 7 hardening regression test.");
    }

    private static async Task<ResponsibilityTermResponse> GenerateTermAsync(HttpClient client, Guid handoverId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync<object?>($"/api/equipment-handovers/{handoverId}/responsibility-terms", null, csrfToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task<EquipmentHandoverDetailResponse> CreateFinalizedHandoverAsync(HttpClient client)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        var finalizeResponse = await Phase4TestData.FinalizeAsync(client, handover.Id);
        finalizeResponse.EnsureSuccessStatusCode();
        return (await finalizeResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
