using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Audit;

/// <summary>
/// Phase 5 (Return/Damage/Maintenance) audit events (docs/PROJECT_SPEC.md,
/// section 15; Phase 5 scope).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class Phase5AuditTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task ReturnLifecycle_RecordsCreatedReceivedAndCompletedEvents()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();
        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, equipment.Id);

        var equipmentReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);

        var createdAudit = await GetAuditPageAsync(client, equipmentReturn.Id, "EquipmentReturn.Created");
        Assert.Single(createdAudit.Items);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Audit Receiver, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, equipmentReturn.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId)).EnsureSuccessStatusCode();

        var receivedAudit = await GetAuditPageAsync(client, equipmentReturn.Id, "EquipmentReturn.Received");
        Assert.Single(receivedAudit.Items);

        var assignmentEndedAudit = await GetAuditPageAsync(client, equipment.Id, "EquipmentAssignment.Ended");
        Assert.Single(assignmentEndedAudit.Items);

        (await Phase5TestData.RecordReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();

        var finalizedAudit = await GetAuditPageAsync(client, equipmentReturn.Id, "ReturnInspection.Finalized");
        Assert.Single(finalizedAudit.Items);

        var completedAudit = await GetAuditPageAsync(client, equipmentReturn.Id, "EquipmentReturn.Completed");
        Assert.Single(completedAudit.Items);
    }

    [Fact]
    public async Task DamageAndMaintenanceLifecycle_RecordsExpectedEvents()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();
        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, equipment.Id);
        var equipmentReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Audit Damage, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, equipmentReturn.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, equipmentReturn.Id, hasDamage: true, damageDescription: "Dent on the case"))
            .EnsureSuccessStatusCode();
        (await Phase5TestData.UploadDamagePhotoAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();

        var damageRecordedAudit = await GetAuditPageAsync(client, equipment.Id, "Damage.Recorded");
        var damageEntry = Assert.Single(damageRecordedAudit.Items);
        Assert.Contains(equipment.AssetTag, damageEntry.MetadataJson);
        Assert.DoesNotContain("Dent on the case", damageEntry.MetadataJson ?? string.Empty);

        var startResponse = await Phase5TestData.StartMaintenanceAsync(client, equipment.Id);
        startResponse.EnsureSuccessStatusCode();

        var maintenanceStartedAudit = await GetAuditPageAsync(client, equipment.Id, "Maintenance.Started");
        Assert.Single(maintenanceStartedAudit.Items);

        var maintenance = (await startResponse.Content.ReadFromJsonAsync<EquipmentManagement.Api.Contracts.Maintenance.MaintenanceDetailResponse>(
            HttpClientTestExtensions.JsonOptions))!;
        (await Phase5TestData.CompleteMaintenanceAsync(client, maintenance.Id, "Repaired")).EnsureSuccessStatusCode();

        var maintenanceCompletedAudit = await GetAuditPageAsync(client, equipment.Id, "Maintenance.Completed");
        Assert.Single(maintenanceCompletedAudit.Items);

        var damageResolvedAudit = await GetAuditPageAsync(client, equipment.Id, "Damage.Resolved");
        Assert.Single(damageResolvedAudit.Items);
    }

    [Fact]
    public async Task CancelledReturn_RecordsEventWithSafeMetadataAndNoReasonText()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();
        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, equipment.Id);
        var equipmentReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);

        const string reason = "Offboarding was cancelled at the last minute.";
        (await Phase5TestData.CancelReturnAsync(client, equipmentReturn.Id, reason)).EnsureSuccessStatusCode();

        var cancelledAudit = await GetAuditPageAsync(client, equipmentReturn.Id, "EquipmentReturn.Cancelled");
        var entry = Assert.Single(cancelledAudit.Items);

        Assert.Contains(equipment.AssetTag, entry.NewValuesJson);
        Assert.Contains(assignmentId.ToString(), entry.NewValuesJson);

        var combined = $"{entry.OldValuesJson}{entry.NewValuesJson}{entry.MetadataJson}";
        Assert.DoesNotContain(reason, combined);
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
