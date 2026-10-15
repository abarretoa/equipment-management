using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Allocations;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.Maintenance;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Equipments;

/// <summary>
/// Iteration 1: the read-only "Histórico do
/// equipamento" projection assembled from existing domain rows — never a new
/// persisted timeline table, never the Audit Log. Historical
/// consultant/company/role must stay frozen at the originating handover's
/// snapshot even after the live consultant later changes.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class EquipmentHistoryTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task NewlyCreatedEquipment_HistoryContainsOnlyRegistration()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        var history = await GetHistoryAsync(client, equipment.Id);

        var registered = Assert.Single(history);
        Assert.Equal("Registered", registered.EventType.ToString());
    }

    [Fact]
    public async Task AssignedEquipment_HistoryShowsConsultantAssignment()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var companyName = $"Example Digital {Guid.NewGuid():N}";
        var (handover, consultantId, equipmentId) = await CreateFinalizedHandoverAsync(client, "Ana Exemplo", companyName, "Software Engineer");

        var history = await GetHistoryAsync(client, equipmentId);

        var assigned = Assert.Single(history, e => e.EventType.ToString() == "Assigned");
        Assert.Equal(consultantId, assigned.ConsultantId);
        Assert.Equal("Ana Exemplo", assigned.ConsultantName);
        Assert.Equal(companyName, assigned.CompanyName);
        Assert.Equal("Software Engineer", assigned.Role);
        Assert.Equal(handover.Id, assigned.HandoverId);
    }

    [Fact]
    public async Task ReturnedEquipment_HistoryShowsEndedCustody()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, equipmentId) = await CreateFinalizedHandoverAsync(client, "Miguel Test", $"Example Labs {Guid.NewGuid():N}", "Data Analyst");
        await CompleteUndamagedReturnAsync(client, equipmentId);

        var history = await GetHistoryAsync(client, equipmentId);

        var returned = Assert.Single(history, e => e.EventType.ToString() == "Returned");
        Assert.Equal("Miguel Test", returned.ConsultantName);
        var inspectionCompleted = Assert.Single(history, e => e.EventType.ToString() == "ReturnInspectionCompleted");
        Assert.Equal("Good", inspectionCompleted.Condition.ToString());

        // The returned/inspection events must not precede the assignment that produced them.
        var assigned = Assert.Single(history, e => e.EventType.ToString() == "Assigned");
        Assert.True(returned.OccurredAtUtc >= assigned.OccurredAtUtc);
    }

    /// <summary>Reassignment A -> return -> B must show both consultants, newest-first.</summary>
    [Fact]
    public async Task ReassignmentAThenB_ShowsBothConsultantsInCorrectOrder()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        var (consultantAId, _) = await Phase2TestData.CreateConsultantAsync(client, "Consultant A");
        var (handoverA, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantAId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handoverA.Id)).EnsureSuccessStatusCode();
        await CompleteUndamagedReturnAsync(client, equipment.Id);

        var (consultantBId, _) = await Phase2TestData.CreateConsultantAsync(client, "Consultant B");
        var (handoverB, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantBId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handoverB.Id)).EnsureSuccessStatusCode();

        var history = await GetHistoryAsync(client, equipment.Id);
        var assignedEvents = history.Where(e => e.EventType.ToString() == "Assigned").ToList();
        Assert.Equal(2, assignedEvents.Count);

        // Newest-first: B's assignment (no return yet) must appear before A's.
        Assert.Equal(consultantBId, assignedEvents[0].ConsultantId);
        Assert.Equal(consultantAId, assignedEvents[1].ConsultantId);

        var indexOf = history.Select((e, i) => (e, i)).ToDictionary(x => (x.e.EventType.ToString(), x.e.ConsultantId), x => x.i);
        Assert.True(indexOf[("Assigned", consultantBId)] < indexOf[("Returned", consultantAId)]);
        Assert.True(indexOf[("Returned", consultantAId)] < indexOf[("Assigned", consultantAId)]);
    }

    /// <summary>
    /// Item D4/J5: a consultant's later company/role/name change must never
    /// rewrite the historical snapshot already shown for an old assignment.
    /// </summary>
    [Fact]
    public async Task ConsultantCompanyAndRoleChangeLater_DoesNotRewriteHistoricalSnapshot()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var companyAName = $"Company A {Guid.NewGuid():N}";
        var (companyAId, _) = await Phase2TestData.CreateCompanyAsync(client, companyAName);
        var (consultantId, nif) = await Phase2TestData.CreateConsultantAsync(client, "João Original");

        var allocationCsrf = await client.GetAntiforgeryTokenAsync();
        var allocationResponse = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "External",
            EngagementType = "ServiceProvider",
            RoleOrFunction = "QA Engineer",
            CompanyId = companyAId,
            StartDate = "2026-01-01",
        }, allocationCsrf);
        allocationResponse.EnsureSuccessStatusCode();
        var allocationA = (await allocationResponse.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions))!;
        var allocationAId = allocationA.Id;

        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        // Now the consultant's name, company and role all change.
        var updateCsrf = await client.GetAntiforgeryTokenAsync();
        (await client.PutWithCsrfAsync($"/api/consultants/{consultantId}", new
        {
            FullName = "João Renomeado",
            Nif = nif,
            Email = $"joao.{Guid.NewGuid():N}@example.invalid",
            Address = "Rua Nova 1, Lisboa",
            Phone = "+351900000099",
        }, updateCsrf)).EnsureSuccessStatusCode();

        var endCsrf = await client.GetAntiforgeryTokenAsync();
        (await client.PostWithCsrfAsync(
            $"/api/consultants/{consultantId}/allocations/{allocationAId}/end", new { EndDate = "2026-06-30", IsEndOfEngagement = false }, endCsrf))
            .EnsureSuccessStatusCode();

        var (companyBId, _) = await Phase2TestData.CreateCompanyAsync(client, $"Company B {Guid.NewGuid():N}");
        var newAllocationCsrf = await client.GetAntiforgeryTokenAsync();
        (await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "External",
            EngagementType = "ServiceProvider",
            RoleOrFunction = "QA Lead",
            CompanyId = companyBId,
            StartDate = "2026-07-01",
        }, newAllocationCsrf)).EnsureSuccessStatusCode();

        var history = await GetHistoryAsync(client, equipment.Id);
        var assigned = Assert.Single(history, e => e.EventType.ToString() == "Assigned");

        // The history must still show the ORIGINAL snapshot, not the consultant's current name/company/role.
        Assert.Equal("João Original", assigned.ConsultantName);
        Assert.Equal(companyAName, assigned.CompanyName);
        Assert.Equal("QA Engineer", assigned.Role);
    }

    [Fact]
    public async Task DamagedReturn_ProducesDamageReportedEvent()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var equipmentId = await Phase5TestData.DamageAGenericEquipmentAsync(client, factory);

        var history = await GetHistoryAsync(client, equipmentId);

        var damage = Assert.Single(history, e => e.EventType.ToString() == "DamageReported");
        Assert.Equal("Broken casing", damage.Description);
    }

    [Fact]
    public async Task Maintenance_ProducesStartAndCompletionEvents()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var equipmentId = await Phase5TestData.DamageAGenericEquipmentAsync(client, factory);

        var startResponse = await Phase5TestData.StartMaintenanceAsync(client, equipmentId);
        startResponse.EnsureSuccessStatusCode();
        var maintenance = (await startResponse.Content.ReadFromJsonAsync<MaintenanceDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        var maintenanceId = maintenance.Id;
        (await Phase5TestData.CompleteMaintenanceAsync(client, maintenanceId, "Repaired")).EnsureSuccessStatusCode();

        var history = await GetHistoryAsync(client, equipmentId);

        var started = Assert.Single(history, e => e.EventType.ToString() == "MaintenanceStarted");
        Assert.Equal(maintenanceId, started.MaintenanceId);
        var completed = Assert.Single(history, e => e.EventType.ToString() == "MaintenanceCompleted");
        Assert.Equal("Repaired", completed.Outcome.ToString());
        Assert.True(completed.OccurredAtUtc >= started.OccurredAtUtc);
    }

    [Fact]
    public async Task RetiredEquipment_ShowsRetirementEvent()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        var retireCsrf = await client.GetAntiforgeryTokenAsync();
        (await client.PostWithCsrfAsync<object?>($"/api/equipment/{equipment.Id}/retire", null, retireCsrf)).EnsureSuccessStatusCode();

        var history = await GetHistoryAsync(client, equipment.Id);

        Assert.Single(history, e => e.EventType.ToString() == "Retired");
    }

    [Fact]
    public async Task History_ContainsNoProhibitedPii()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, equipmentId) = await CreateFinalizedHandoverAsync(client, "Sara Sample", $"Company X {Guid.NewGuid():N}", "Consultant");

        var response = await client.GetAsync($"/api/equipment/{equipmentId}/history");
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("nif", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("address", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@example.invalid", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task History_OrderingIsDeterministicAcrossCalls()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var equipmentId = await Phase5TestData.DamageAGenericEquipmentAsync(client, factory);

        var first = await GetHistoryAsync(client, equipmentId);
        var second = await GetHistoryAsync(client, equipmentId);

        Assert.Equal(
            first.Select(e => (e.EventType.ToString(), e.OccurredAtUtc, e.HandoverId, e.ReturnId, e.MaintenanceId)),
            second.Select(e => (e.EventType.ToString(), e.OccurredAtUtc, e.HandoverId, e.ReturnId, e.MaintenanceId)));

        // Newest-first.
        for (var i = 1; i < first.Count; i++)
        {
            Assert.True(first[i - 1].OccurredAtUtc >= first[i].OccurredAtUtc);
        }
    }

    private static async Task<List<EquipmentHistoryEventResponse>> GetHistoryAsync(HttpClient client, Guid equipmentId)
    {
        var response = await client.GetAsync($"/api/equipment/{equipmentId}/history");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<EquipmentHistoryEventResponse>>(HttpClientTestExtensions.JsonOptions))!;
    }

    private async Task CompleteUndamagedReturnAsync(HttpClient client, Guid equipmentId)
    {
        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, equipmentId);
        var equipmentReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);
        var receiverId = await GetOrCreateReceiverAsync(client);
        (await Phase5TestData.ReceiveReturnAsync(client, equipmentReturn.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();
    }

    private async Task<Guid> GetOrCreateReceiverAsync(HttpClient client)
    {
        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: $"Rua Receiver {Guid.NewGuid():N}, Lisboa");
        return await ApiTestUsers.GetIdAsync(factory, receiverEmail);
    }

    private static async Task<(EquipmentHandoverDetailResponse Handover, Guid ConsultantId, Guid EquipmentId)> CreateFinalizedHandoverAsync(
        HttpClient client, string consultantName, string companyName, string role)
    {
        var (companyId, _) = await Phase2TestData.CreateCompanyAsync(client, companyName);
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client, consultantName);

        var allocationCsrf = await client.GetAntiforgeryTokenAsync();
        (await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "External",
            EngagementType = "ServiceProvider",
            RoleOrFunction = role,
            CompanyId = companyId,
            StartDate = "2026-01-01",
        }, allocationCsrf)).EnsureSuccessStatusCode();

        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        var finalizeResponse = await Phase4TestData.FinalizeAsync(client, handover.Id);
        finalizeResponse.EnsureSuccessStatusCode();
        var finalized = (await finalizeResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;

        return (finalized, consultantId, equipment.Id);
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
