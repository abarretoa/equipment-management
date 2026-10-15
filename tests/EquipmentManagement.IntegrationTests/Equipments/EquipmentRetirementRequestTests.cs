using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Infrastructure.Documents;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Equipments;

/// <summary>
/// The Equipment Retirement Request workflow: HR requests, Administration
/// reviews/decides. Approval must go through the existing, single
/// <c>EquipmentService.RetireAsync</c> — never a second retirement
/// implementation (see <c>EquipmentRetirementRequestService</c>'s remarks).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class EquipmentRetirementRequestTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Hr_CanRequestRetirementForAvailableEquipment()
    {
        using var hrClient = await LoginAsNewHrAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, type.Id);

        var response = await CreateRequestAsync(hrClient, equipment.Id, "Avaria recorrente sem viabilidade de reparação.");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<EquipmentRetirementRequestResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal("Pending", created.Status.ToString());
        Assert.Equal(equipment.Id, created.EquipmentId);

        var equipmentAfter = await GetEquipmentAsync(hrClient, equipment.Id);
        Assert.Equal("Available", equipmentAfter.Status.ToString());
        Assert.NotNull(equipmentAfter.PendingRetirementRequest);
        Assert.Equal(created.Id, equipmentAfter.PendingRetirementRequest!.Id);
    }

    [Fact]
    public async Task Hr_CanRequestRetirementForDamagedEquipment()
    {
        using var adminClient = await LoginAsNewAdministratorAsync();
        var equipmentId = await Phase5TestData.DamageAGenericEquipmentAsync(adminClient, factory);

        using var hrClient = await LoginAsNewHrAsync();
        var response = await CreateRequestAsync(hrClient, equipmentId, "Danificado, sem reparação viável.");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var equipmentAfter = await GetEquipmentAsync(hrClient, equipmentId);
        Assert.Equal("Damaged", equipmentAfter.Status.ToString());
    }

    [Theory]
    [InlineData("Assigned")]
    [InlineData("ReturnPending")]
    [InlineData("UnderInspection")]
    [InlineData("UnderMaintenance")]
    [InlineData("Retired")]
    public async Task Hr_CannotRequestRetirement_ForIneligibleStatus(string status)
    {
        using var adminClient = await LoginAsNewAdministratorAsync();
        var equipmentId = await CreateEquipmentInStatusAsync(adminClient, status);

        using var hrClient = await LoginAsNewHrAsync();
        var response = await CreateRequestAsync(hrClient, equipmentId, "Motivo qualquer.");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DuplicatePendingRequest_ForSameEquipment_IsRejected()
    {
        using var hrClient = await LoginAsNewHrAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, type.Id);

        (await CreateRequestAsync(hrClient, equipment.Id, "Primeiro motivo.")).EnsureSuccessStatusCode();
        var second = await CreateRequestAsync(hrClient, equipment.Id, "Segundo motivo.");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Hr_CannotRetireEquipmentDirectly_EvenAfterCreatingARequest()
    {
        using var hrClient = await LoginAsNewHrAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, type.Id);
        (await CreateRequestAsync(hrClient, equipment.Id, "Motivo.")).EnsureSuccessStatusCode();

        var csrf = await hrClient.GetAntiforgeryTokenAsync();
        var response = await hrClient.PostWithCsrfAsync<object?>($"/api/equipment/{equipment.Id}/retire", null, csrf);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonAdministration_CannotApproveOrRejectRequests()
    {
        using var hrClient = await LoginAsNewHrAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, type.Id);
        var request = await CreateAndReadRequestAsync(hrClient, equipment.Id, "Motivo.");

        var approveCsrf = await hrClient.GetAntiforgeryTokenAsync();
        var approveResponse = await hrClient.PostWithCsrfAsync<object?>($"/api/equipment-retirement-requests/{request.Id}/approve", null, approveCsrf);
        Assert.Equal(HttpStatusCode.Forbidden, approveResponse.StatusCode);

        var rejectCsrf = await hrClient.GetAntiforgeryTokenAsync();
        var rejectResponse = await hrClient.PostWithCsrfAsync(
            $"/api/equipment-retirement-requests/{request.Id}/reject", new { Reason = "Não aplicável." }, rejectCsrf);
        Assert.Equal(HttpStatusCode.Forbidden, rejectResponse.StatusCode);
    }

    [Fact]
    public async Task Administration_CanApproveAPendingRequest_WhichRetiresEquipmentThroughExistingWorkflow()
    {
        using var hrClient = await LoginAsNewHrAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, type.Id);
        var request = await CreateAndReadRequestAsync(hrClient, equipment.Id, "Motivo.");

        using var adminClient = await LoginAsNewAdministratorAsync();
        var approveCsrf = await adminClient.GetAntiforgeryTokenAsync();
        var approveResponse = await adminClient.PostWithCsrfAsync<object?>($"/api/equipment-retirement-requests/{request.Id}/approve", null, approveCsrf);

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approved = (await approveResponse.Content.ReadFromJsonAsync<EquipmentRetirementRequestResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal("Approved", approved.Status.ToString());
        Assert.NotNull(approved.DecidedAtUtc);
        Assert.NotNull(approved.DecidedByDisplayName);

        var equipmentAfter = await GetEquipmentAsync(adminClient, equipment.Id);
        Assert.Equal("Retired", equipmentAfter.Status.ToString());
        Assert.Null(equipmentAfter.PendingRetirementRequest);

        var history = await GetHistoryAsync(adminClient, equipment.Id);
        Assert.Single(history, e => e.EventType.ToString() == "Retired");
        Assert.Single(history, e => e.EventType.ToString() == "RetirementRequested");
    }

    [Fact]
    public async Task Approval_FailsAndLeavesRequestPending_WhenEquipmentBecameIneligibleSinceRequest()
    {
        using var hrClient = await LoginAsNewHrAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, type.Id);
        var request = await CreateAndReadRequestAsync(hrClient, equipment.Id, "Motivo.");

        using var adminClient = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(adminClient);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(adminClient, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(adminClient, handover.Id)).EnsureSuccessStatusCode();

        var approveCsrf = await adminClient.GetAntiforgeryTokenAsync();
        var approveResponse = await adminClient.PostWithCsrfAsync<object?>($"/api/equipment-retirement-requests/{request.Id}/approve", null, approveCsrf);

        Assert.Equal(HttpStatusCode.Conflict, approveResponse.StatusCode);

        var equipmentAfter = await GetEquipmentAsync(adminClient, equipment.Id);
        Assert.Equal("Assigned", equipmentAfter.Status.ToString());
        Assert.NotNull(equipmentAfter.PendingRetirementRequest);
    }

    [Fact]
    public async Task Administration_CanRejectAPendingRequest_RequiringAReason()
    {
        using var hrClient = await LoginAsNewHrAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, type.Id);
        var request = await CreateAndReadRequestAsync(hrClient, equipment.Id, "Motivo.");

        using var adminClient = await LoginAsNewAdministratorAsync();

        var blankCsrf = await adminClient.GetAntiforgeryTokenAsync();
        var blankResponse = await adminClient.PostWithCsrfAsync($"/api/equipment-retirement-requests/{request.Id}/reject", new { Reason = "" }, blankCsrf);
        Assert.Equal(HttpStatusCode.BadRequest, blankResponse.StatusCode);

        var rejectCsrf = await adminClient.GetAntiforgeryTokenAsync();
        var rejectResponse = await adminClient.PostWithCsrfAsync(
            $"/api/equipment-retirement-requests/{request.Id}/reject", new { Reason = "Equipamento ainda em bom estado." }, rejectCsrf);

        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        var rejected = (await rejectResponse.Content.ReadFromJsonAsync<EquipmentRetirementRequestResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal("Rejected", rejected.Status.ToString());
        Assert.Equal("Equipamento ainda em bom estado.", rejected.DecisionReason);

        var equipmentAfter = await GetEquipmentAsync(adminClient, equipment.Id);
        Assert.Equal("Available", equipmentAfter.Status.ToString());
        Assert.Null(equipmentAfter.PendingRetirementRequest);

        var history = await GetHistoryAsync(adminClient, equipment.Id);
        Assert.Single(history, e => e.EventType.ToString() == "RetirementRequestRejected");
    }

    [Fact]
    public async Task DecidedRequest_CannotBeDecidedAgain_ButANewRequestMayFollow()
    {
        using var hrClient = await LoginAsNewHrAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, type.Id);
        var request = await CreateAndReadRequestAsync(hrClient, equipment.Id, "Motivo.");

        using var adminClient = await LoginAsNewAdministratorAsync();
        var rejectCsrf = await adminClient.GetAntiforgeryTokenAsync();
        (await adminClient.PostWithCsrfAsync(
                $"/api/equipment-retirement-requests/{request.Id}/reject", new { Reason = "Ainda não." }, rejectCsrf))
            .EnsureSuccessStatusCode();

        var secondRejectCsrf = await adminClient.GetAntiforgeryTokenAsync();
        var secondReject = await adminClient.PostWithCsrfAsync(
            $"/api/equipment-retirement-requests/{request.Id}/reject", new { Reason = "De novo." }, secondRejectCsrf);
        Assert.Equal(HttpStatusCode.Conflict, secondReject.StatusCode);

        var approveCsrf = await adminClient.GetAntiforgeryTokenAsync();
        var approveResponse = await adminClient.PostWithCsrfAsync<object?>($"/api/equipment-retirement-requests/{request.Id}/approve", null, approveCsrf);
        Assert.Equal(HttpStatusCode.Conflict, approveResponse.StatusCode);

        // Equipment is still Available (rejection never changes lifecycle), so a fresh request may follow.
        var secondRequest = await CreateRequestAsync(hrClient, equipment.Id, "Novo motivo depois da rejeição.");
        Assert.Equal(HttpStatusCode.Created, secondRequest.StatusCode);
    }

    [Fact]
    public async Task DirectAdministrationRetirement_ApprovesAnyPendingRequestForThatEquipment()
    {
        using var hrClient = await LoginAsNewHrAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, type.Id);
        var request = await CreateAndReadRequestAsync(hrClient, equipment.Id, "Motivo.");

        using var adminClient = await LoginAsNewAdministratorAsync();
        var retireCsrf = await adminClient.GetAntiforgeryTokenAsync();
        (await adminClient.PostWithCsrfAsync<object?>($"/api/equipment/{equipment.Id}/retire", null, retireCsrf)).EnsureSuccessStatusCode();

        var listResponse = await adminClient.GetFromJsonAsync<PagedResponse<EquipmentRetirementRequestResponse>>(
            "/api/equipment-retirement-requests?status=Approved", HttpClientTestExtensions.JsonOptions);
        var approved = Assert.Single(listResponse!.Items, r => r.Id == request.Id);
        Assert.Equal("Approved", approved.Status.ToString());
        Assert.NotNull(approved.DecidedAtUtc);

        var equipmentAfter = await GetEquipmentAsync(adminClient, equipment.Id);
        Assert.Null(equipmentAfter.PendingRetirementRequest);
    }

    [Fact]
    public async Task Creation_TriggersEmailToActiveAdministrationUsers_ButNotInactiveAdminsOrHr()
    {
        var (activeAdminEmail, _) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var (inactiveAdminEmail, _) = await ApiTestUsers.CreateAsync(factory, "Administration", isActive: false);
        var (hrRecipientEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR RetRequest, Lisboa");

        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrRecipientEmail, hrPassword);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, type.Id);

        GetEmailSender().Reset();
        var response = await CreateRequestAsync(hrClient, equipment.Id, "Avaria recorrente sem viabilidade de reparação.");
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<EquipmentRetirementRequestResponse>(HttpClientTestExtensions.JsonOptions))!;

        var sent = GetEmailSender().SentMessages;
        Assert.Contains(sent, m => m.ToAddress == activeAdminEmail);
        Assert.DoesNotContain(sent, m => m.ToAddress == inactiveAdminEmail);
        Assert.DoesNotContain(sent, m => m.ToAddress == hrRecipientEmail);

        var message = sent.Single(m => m.ToAddress == activeAdminEmail);
        Assert.Contains(equipment.AssetTag, message.Subject);
        Assert.Contains(equipment.AssetTag, message.PlainTextBody);
        Assert.Contains(type.Name, message.PlainTextBody);
        Assert.Contains("Avaria recorrente sem viabilidade de reparação.", message.PlainTextBody);
        Assert.Contains("Test HR", message.PlainTextBody);
        Assert.Null(message.Attachment);

        // Same explicit-UTC-label convention as ResponsibilityTermPdfGenerator's
        // "Documento gerado em ... UTC" line (DocumentCulture.FormatDateTimeUtc) —
        // never a guessed-timezone conversion, and never the persisted value
        // reformatted without a timezone label (which is what previously read as
        // a silent mismatch against the frontend's browser-local display of the
        // same instant).
        Assert.Contains(DocumentCulture.FormatDateTimeUtc(created.RequestedAtUtc), message.PlainTextBody);
    }

    [Fact]
    public async Task NotificationEmail_ContainsAuthenticatedAppCtaUrl_AndNoTokensOrCredentials()
    {
        var (activeAdminEmail, _) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR RetRequest CTA, Lisboa");

        await using var derivedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configBuilder) =>
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["App:PublicBaseUrl"] = "https://app.example.invalid",
                })));

        using var hrClient = derivedFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await hrClient.LoginAsync(hrEmail, hrPassword);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, type.Id);

        // WithWebHostBuilder spins up a fresh host/DI container, so its
        // RecordingEmailSender singleton is a different instance from the
        // outer factory's — resolve it from derivedFactory specifically.
        var derivedEmailSender = derivedFactory.Services.GetRequiredService<RecordingEmailSender>();
        derivedEmailSender.Reset();
        (await CreateRequestAsync(hrClient, equipment.Id, "Motivo qualquer.")).EnsureSuccessStatusCode();

        var message = derivedEmailSender.SentMessages.Single(m => m.ToAddress == activeAdminEmail);
        Assert.Contains("https://app.example.invalid/solicitacoes-abate", message.PlainTextBody);
        Assert.False(message.IncludeSignature); // internal notification, not an RH message
        Assert.DoesNotContain("token", message.PlainTextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(hrPassword, message.PlainTextBody);
    }

    [Fact]
    public async Task EmailDeliveryFailure_DoesNotRollBackTheRequestOrChangeEquipmentLifecycle()
    {
        await ApiTestUsers.CreateAsync(factory, "Administration");
        using var hrClient = await LoginAsNewHrAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, type.Id);

        GetEmailSender().Reset();
        GetEmailSender().FailNextSendWith = EmailSendFailureReason.Temporary;

        var response = await CreateRequestAsync(hrClient, equipment.Id, "Motivo qualquer.");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<EquipmentRetirementRequestResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal("Pending", created.Status.ToString());

        var equipmentAfter = await GetEquipmentAsync(hrClient, equipment.Id);
        Assert.Equal("Available", equipmentAfter.Status.ToString());
        Assert.NotNull(equipmentAfter.PendingRetirementRequest);
    }

    private static async Task<HttpResponseMessage> CreateRequestAsync(HttpClient client, Guid equipmentId, string reason)
    {
        var csrf = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync("/api/equipment-retirement-requests", new { EquipmentId = equipmentId, Reason = reason }, csrf);
    }

    private static async Task<EquipmentRetirementRequestResponse> CreateAndReadRequestAsync(HttpClient client, Guid equipmentId, string reason)
    {
        var response = await CreateRequestAsync(client, equipmentId, reason);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EquipmentRetirementRequestResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private async Task<Guid> CreateEquipmentInStatusAsync(HttpClient adminClient, string status)
    {
        switch (status)
        {
            case "Assigned":
            {
                var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(adminClient);
                var type = await Phase3TestData.CreateEquipmentTypeAsync(adminClient, kind: "Generic");
                var equipment = await Phase3TestData.CreateGenericEquipmentAsync(adminClient, type.Id);
                var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(adminClient, consultantId, equipment.Id, photoCount: 0);
                (await Phase4TestData.FinalizeAsync(adminClient, handover.Id)).EnsureSuccessStatusCode();
                return equipment.Id;
            }

            case "ReturnPending":
            {
                var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(adminClient);
                var type = await Phase3TestData.CreateEquipmentTypeAsync(adminClient, kind: "Generic");
                var equipment = await Phase3TestData.CreateGenericEquipmentAsync(adminClient, type.Id);
                var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(adminClient, consultantId, equipment.Id, photoCount: 0);
                (await Phase4TestData.FinalizeAsync(adminClient, handover.Id)).EnsureSuccessStatusCode();
                var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(adminClient, equipment.Id);
                await Phase5TestData.InitiateReturnSuccessfullyAsync(adminClient, assignmentId);
                return equipment.Id;
            }

            case "UnderInspection":
            {
                var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(adminClient);
                var type = await Phase3TestData.CreateEquipmentTypeAsync(adminClient, kind: "Generic");
                var equipment = await Phase3TestData.CreateGenericEquipmentAsync(adminClient, type.Id);
                var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(adminClient, consultantId, equipment.Id, photoCount: 0);
                (await Phase4TestData.FinalizeAsync(adminClient, handover.Id)).EnsureSuccessStatusCode();
                var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(adminClient, equipment.Id);
                var equipmentReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(adminClient, assignmentId);
                var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: $"Rua Receiver {Guid.NewGuid():N}, Lisboa");
                var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
                (await Phase5TestData.ReceiveReturnAsync(adminClient, equipmentReturn.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId))
                    .EnsureSuccessStatusCode();
                return equipment.Id;
            }

            case "UnderMaintenance":
            {
                var equipmentId = await Phase5TestData.DamageAGenericEquipmentAsync(adminClient, factory);
                (await Phase5TestData.StartMaintenanceAsync(adminClient, equipmentId)).EnsureSuccessStatusCode();
                return equipmentId;
            }

            case "Retired":
            {
                var type = await Phase3TestData.CreateEquipmentTypeAsync(adminClient, kind: "Generic");
                var equipment = await Phase3TestData.CreateGenericEquipmentAsync(adminClient, type.Id);
                var retireCsrf = await adminClient.GetAntiforgeryTokenAsync();
                (await adminClient.PostWithCsrfAsync<object?>($"/api/equipment/{equipment.Id}/retire", null, retireCsrf)).EnsureSuccessStatusCode();
                return equipment.Id;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }

    private static async Task<EquipmentDetailResponse> GetEquipmentAsync(HttpClient client, Guid equipmentId)
    {
        var response = await client.GetAsync($"/api/equipment/{equipmentId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task<List<EquipmentHistoryEventResponse>> GetHistoryAsync(HttpClient client, Guid equipmentId)
    {
        var response = await client.GetAsync($"/api/equipment/{equipmentId}/history");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<EquipmentHistoryEventResponse>>(HttpClientTestExtensions.JsonOptions))!;
    }

    private RecordingEmailSender GetEmailSender() => factory.Services.GetRequiredService<RecordingEmailSender>();

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }

    private async Task<HttpClient> LoginAsNewHrAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "HR", address: $"Rua HR {Guid.NewGuid():N}, Lisboa");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
