using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.ResponsibilityTerms;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EquipmentManagement.IntegrationTests.ResponsibilityTerms;

/// <summary>
/// A failed send must leave one operational line in the application log (it
/// used to be visible only in the database and the UI), carrying identifiers
/// and a failure category but no recipient, name, body or credential. The
/// existing database/UI delivery status keeps working unchanged.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class EmailFailureLoggingTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task FailedTermSend_LogsDeliveryIdCategoryAndCorrelationId_AndNothingSensitive()
    {
        var logs = new CapturingLoggerProvider();
        await using var derivedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(logs)));

        var (adminEmail, adminPassword) = await ApiTestUsers.CreateAsync(factory, "Administration");
        using var client = derivedFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await client.LoginAsync(adminEmail, adminPassword);

        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var consultant = await client.GetFromJsonAsync<EquipmentManagement.Api.Contracts.Consultants.ConsultantDetailResponse>(
            $"/api/consultants/{consultantId}", HttpClientTestExtensions.JsonOptions);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var termResponse = await client.PostWithCsrfAsync<object?>($"/api/equipment-handovers/{handover.Id}/responsibility-terms", null, csrfToken);
        termResponse.EnsureSuccessStatusCode();
        var term = (await termResponse.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;

        logs.Clear();
        derivedFactory.Services.GetRequiredService<RecordingEmailSender>().FailNextSendWith = EmailSendFailureReason.Temporary;
        var failedResponse = await Phase7TestData.SendAsync(client, term.Id);

        // The database/UI delivery status is unchanged by the new log line.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedResponse.StatusCode);
        var deliveries = await client.GetFromJsonAsync<List<ResponsibilityTermDeliveryResponse>>(
            $"/api/responsibility-terms/{term.Id}/deliveries", HttpClientTestExtensions.JsonOptions);
        var failedAttempt = Assert.Single(deliveries!);
        Assert.Equal(DeliveryStatus.Failed, failedAttempt.Status);
        Assert.Equal(DeliveryFailureCode.TemporaryFailure, failedAttempt.FailureCode);

        var line = Assert.Single(logs.Messages, m => m.Category == typeof(EmailFailureLogger).FullName);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains("MessageType=ResponsibilityTerm", line.Text, StringComparison.Ordinal);
        Assert.Contains($"DeliveryId={failedAttempt.Id}", line.Text, StringComparison.Ordinal);
        Assert.Contains("FailureCategory=Temporary", line.Text, StringComparison.Ordinal);
        var correlationId = Assert.Single(failedResponse.Headers.GetValues("X-Correlation-ID"));
        Assert.Contains($"CorrelationId={correlationId}", line.Text, StringComparison.Ordinal);

        // Nothing about the people or the message itself reaches the log.
        var everythingLogged = string.Join('\n', logs.Messages.Select(m => m.Text));
        Assert.DoesNotContain(consultant!.Email, everythingLogged, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(consultant.FullName, everythingLogged, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(adminEmail, everythingLogged, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(adminPassword, everythingLogged, StringComparison.Ordinal);
        Assert.DoesNotContain(term.DocumentNumber, line.Text, StringComparison.Ordinal);

        // A successful retry logs no failure line.
        logs.Clear();
        (await Phase7TestData.SendAsync(client, term.Id)).EnsureSuccessStatusCode();
        Assert.DoesNotContain(logs.Messages, m => m.Category == typeof(EmailFailureLogger).FullName);
    }

    [Fact]
    public async Task FailedRetirementNotification_IsLoggedAgainstTheRequest_WithoutTheAdministratorsAddress()
    {
        var logs = new CapturingLoggerProvider();
        await using var derivedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(logs)));

        var (adminEmail, _) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR Email Log, Lisboa");
        using var hrClient = derivedFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await hrClient.LoginAsync(hrEmail, hrPassword);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, type.Id);

        logs.Clear();
        derivedFactory.Services.GetRequiredService<RecordingEmailSender>().FailNextSendWith = EmailSendFailureReason.Rejected;
        var csrf = await hrClient.GetAntiforgeryTokenAsync();
        var response = await hrClient.PostWithCsrfAsync(
            "/api/equipment-retirement-requests", new { EquipmentId = equipment.Id, Reason = "Motivo qualquer." }, csrf);

        // The request itself still succeeds: email is communication, not part of the transaction.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<EquipmentManagement.Api.Contracts.Equipments.EquipmentRetirementRequestResponse>(
            HttpClientTestExtensions.JsonOptions))!;

        var line = Assert.Single(logs.Messages, m => m.Category == typeof(EmailFailureLogger).FullName);
        Assert.Contains("MessageType=RetirementRequestNotification", line.Text, StringComparison.Ordinal);
        Assert.Contains($"DeliveryId={created.Id}", line.Text, StringComparison.Ordinal);
        Assert.Contains("FailureCategory=Rejected", line.Text, StringComparison.Ordinal);

        var everythingLogged = string.Join('\n', logs.Messages.Select(m => m.Text));
        Assert.DoesNotContain(adminEmail, everythingLogged, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(hrEmail, everythingLogged, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Motivo qualquer", everythingLogged, StringComparison.Ordinal);
    }

    private sealed record CapturedLog(string Category, LogLevel Level, string Text);

    /// <summary>Captures every log line the host writes, exception text included, so a test can assert on what is absent as well as present.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<CapturedLog> _messages = new();

        public IReadOnlyList<CapturedLog> Messages => [.. _messages];

        public void Clear() => _messages.Clear();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception) + (exception is null ? string.Empty : "\n" + exception)));
            }
        }
    }
}
