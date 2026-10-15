using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.ResponsibilityTerms;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.ResponsibilityTerms;

/// <summary>
/// The Phase 7 email-delivery workflow against a real PostgreSQL database and
/// the in-memory <see cref="RecordingEmailSender"/> fake (no automated test
/// ever sends a real email — Phase 7 scope).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class ResponsibilityTermDeliveryTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Send_UsesHistoricalConsultantSnapshotEmail_AttachesExactOriginalPdfBytes()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, consultant) = await CreateFinalizedHandoverWithConsultantAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        var originalPdf = await (await client.GetAsync($"/api/responsibility-terms/{term.Id}/content")).Content.ReadAsByteArrayAsync();

        GetEmailSender().Reset();
        var sendResponse = await Phase7TestData.SendAsync(client, term.Id);
        sendResponse.EnsureSuccessStatusCode();
        var delivery = (await sendResponse.Content.ReadFromJsonAsync<ResponsibilityTermDeliveryResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(DeliveryStatus.Sent, delivery.Status);
        Assert.Equal(consultant.Email, delivery.RecipientEmailSnapshot);
        Assert.False(delivery.RecipientOverridden);
        Assert.NotNull(delivery.SentAtUtc);

        var sent = Assert.Single(GetEmailSender().SentMessages);
        Assert.Equal(consultant.Email, sent.ToAddress);
        Assert.Contains(term.DocumentNumber, sent.Subject);
        Assert.NotNull(sent.Attachment);
        Assert.True(sent.IncludeSignature);
        Assert.Equal("application/pdf", sent.Attachment!.ContentType);
        Assert.Equal(term.FileName, sent.Attachment.FileName);
        Assert.Equal(originalPdf, sent.Attachment.Content);
    }

    [Fact]
    public async Task Send_WithRecipientOverride_UsesOverrideAndDoesNotMutateConsultantOrHandover()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, consultant) = await CreateFinalizedHandoverWithConsultantAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        var overrideEmail = $"override.{Guid.NewGuid():N}@example.invalid";
        var sendResponse = await Phase7TestData.SendAsync(client, term.Id, overrideEmail);
        sendResponse.EnsureSuccessStatusCode();
        var delivery = (await sendResponse.Content.ReadFromJsonAsync<ResponsibilityTermDeliveryResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(overrideEmail, delivery.RecipientEmailSnapshot);
        Assert.True(delivery.RecipientOverridden);

        var consultantAfter = await client.GetFromJsonAsync<ConsultantDetailResponse>($"/api/consultants/{consultant.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(consultant.Email, consultantAfter!.Email);

        var handoverAfter = await client.GetFromJsonAsync<EquipmentHandoverDetailResponse>($"/api/equipment-handovers/{handover.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(consultant.Email, handoverAfter!.ConsultantSnapshot!.Email);
    }

    [Fact]
    public async Task Send_InvalidRecipientOverride_ReturnsBadRequest()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        var response = await Phase7TestData.SendAsync(client, term.Id, "not-an-email");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Send_OnSupersededTerm_ReturnsConflict()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var v1 = await GenerateTermAsync(client, handover.Id);
        await GenerateTermAsync(client, handover.Id);

        var response = await Phase7TestData.SendAsync(client, v1.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Send_Resend_PreservesFullHistory_AndTermDetailReflectsSummary()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        (await Phase7TestData.SendAsync(client, term.Id)).EnsureSuccessStatusCode();
        (await Phase7TestData.SendAsync(client, term.Id)).EnsureSuccessStatusCode();

        var deliveries = await client.GetFromJsonAsync<List<ResponsibilityTermDeliveryResponse>>(
            $"/api/responsibility-terms/{term.Id}/deliveries", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(2, deliveries!.Count);
        Assert.All(deliveries, d => Assert.Equal(DeliveryStatus.Sent, d.Status));

        var detail = await client.GetFromJsonAsync<ResponsibilityTermResponse>($"/api/responsibility-terms/{term.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(2, detail!.SendAttemptCount);
        Assert.Equal(ResponsibilityTermExecutionStatus.Sent, detail.ExecutionStatus);
        Assert.NotNull(detail.LastSentAtUtc);
    }

    [Fact]
    public async Task Send_ProviderFailure_RecordsFailedAttemptAndTermRemainsNotSent_RetryThenSucceeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        GetEmailSender().FailNextSendWith = EmailSendFailureReason.Temporary;
        var failedResponse = await Phase7TestData.SendAsync(client, term.Id);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedResponse.StatusCode);

        var deliveriesAfterFailure = await client.GetFromJsonAsync<List<ResponsibilityTermDeliveryResponse>>(
            $"/api/responsibility-terms/{term.Id}/deliveries", HttpClientTestExtensions.JsonOptions);
        var failedAttempt = Assert.Single(deliveriesAfterFailure!);
        Assert.Equal(DeliveryStatus.Failed, failedAttempt.Status);
        Assert.Equal(DeliveryFailureCode.TemporaryFailure, failedAttempt.FailureCode);
        Assert.Null(failedAttempt.SentAtUtc);

        var detailAfterFailure = await client.GetFromJsonAsync<ResponsibilityTermResponse>($"/api/responsibility-terms/{term.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ResponsibilityTermExecutionStatus.NotSent, detailAfterFailure!.ExecutionStatus);

        var retryResponse = await Phase7TestData.SendAsync(client, term.Id);
        retryResponse.EnsureSuccessStatusCode();

        var deliveriesAfterRetry = await client.GetFromJsonAsync<List<ResponsibilityTermDeliveryResponse>>(
            $"/api/responsibility-terms/{term.Id}/deliveries", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(2, deliveriesAfterRetry!.Count);
        Assert.Contains(deliveriesAfterRetry, d => d.Status == DeliveryStatus.Sent);
    }

    [Fact]
    public async Task Send_RequiresAuthentication()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/responsibility-terms/00000000-0000-0000-0000-000000000000/send", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Send_RequiresCsrfToken()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        var response = await client.PostAsJsonAsync($"/api/responsibility-terms/{term.Id}/send", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Hr_CanSend_SameAsAdministration()
    {
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR Delivery, Lisboa");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);

        var handover = await CreateFinalizedHandoverAsync(hrClient);
        var term = await GenerateTermAsync(hrClient, handover.Id);

        var response = await Phase7TestData.SendAsync(hrClient, term.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Send_RecordsAuditEvent_WithoutRecipientEmail_ButFlagsOverride()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        var overrideEmail = $"secret.{Guid.NewGuid():N}@example.invalid";
        (await Phase7TestData.SendAsync(client, term.Id, overrideEmail)).EnsureSuccessStatusCode();

        var audit = await GetAuditPageAsync(client, term.Id, "ResponsibilityTerm.Sent");
        var entry = Assert.Single(audit.Items);
        var combined = $"{entry.OldValuesJson}{entry.NewValuesJson}{entry.MetadataJson}";
        Assert.DoesNotContain(overrideEmail, combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recipientOverridden", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Send_Failure_RecordsAuditEvent_WithSafeFailureCodeOnly()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        GetEmailSender().FailNextSendWith = EmailSendFailureReason.Rejected;
        var failedResponse = await Phase7TestData.SendAsync(client, term.Id);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedResponse.StatusCode);

        var audit = await GetAuditPageAsync(client, term.Id, "ResponsibilityTerm.SendFailed");
        var entry = Assert.Single(audit.Items);
        var combined = $"{entry.OldValuesJson}{entry.NewValuesJson}{entry.MetadataJson}";
        Assert.Contains("ProviderRejected", combined, StringComparison.OrdinalIgnoreCase);
    }

    private RecordingEmailSender GetEmailSender() => factory.Services.GetRequiredService<RecordingEmailSender>();

    private static async Task<ResponsibilityTermResponse> GenerateTermAsync(HttpClient client, Guid handoverId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync<object?>($"/api/equipment-handovers/{handoverId}/responsibility-terms", null, csrfToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task<EquipmentHandoverDetailResponse> CreateFinalizedHandoverAsync(HttpClient client)
    {
        var (handover, _) = await CreateFinalizedHandoverWithConsultantAsync(client);
        return handover;
    }

    private static async Task<(EquipmentHandoverDetailResponse Handover, ConsultantDetailResponse Consultant)> CreateFinalizedHandoverWithConsultantAsync(
        HttpClient client)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var consultant = await client.GetFromJsonAsync<ConsultantDetailResponse>($"/api/consultants/{consultantId}", HttpClientTestExtensions.JsonOptions);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        var finalizeResponse = await Phase4TestData.FinalizeAsync(client, handover.Id);
        finalizeResponse.EnsureSuccessStatusCode();
        var finalized = (await finalizeResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;

        return (finalized, consultant!);
    }

    private static async Task<PagedResponse<AuditLogResponse>> GetAuditPageAsync(HttpClient client, Guid entityId, string action)
    {
        var response = await client.GetAsync($"/api/audit?entityId={entityId}&action={action}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions))!;
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
