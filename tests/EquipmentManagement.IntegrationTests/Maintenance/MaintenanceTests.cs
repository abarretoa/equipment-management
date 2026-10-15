using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Maintenance;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Maintenance;

/// <summary>
/// The maintenance workflow against a real PostgreSQL database
/// (docs/PROJECT_SPEC.md, section 13; Phase 5 scope).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class MaintenanceTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task StartMaintenance_OnAvailableEquipment_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var response = await Phase5TestData.StartMaintenanceAsync(client, equipment.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task FullDamageToMaintenanceToRepaired_ReturnsEquipmentToAvailableAndResolvesDamage()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var equipmentId = await DamageAGenericEquipmentAsync(client);

        var startResponse = await Phase5TestData.StartMaintenanceAsync(client, equipmentId, "Sent to vendor for screen replacement.");
        startResponse.EnsureSuccessStatusCode();
        var maintenance = (await startResponse.Content.ReadFromJsonAsync<MaintenanceDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal("Active", maintenance.Status.ToString());
        Assert.NotNull(maintenance.DamageId);

        var equipmentAfterStart = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal("UnderMaintenance", equipmentAfterStart.Status.ToString());

        var completeResponse = await Phase5TestData.CompleteMaintenanceAsync(client, maintenance.Id, "Repaired");
        completeResponse.EnsureSuccessStatusCode();
        var completed = (await completeResponse.Content.ReadFromJsonAsync<MaintenanceDetailResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal("Completed", completed.Status.ToString());
        Assert.Equal("Repaired", completed.Outcome!.ToString());
        Assert.NotNull(completed.CompletedAtUtc);

        var equipmentAfterComplete = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal("Available", equipmentAfterComplete.Status.ToString());
    }

    [Fact]
    public async Task NotRepairableOutcome_ReturnsEquipmentToDamaged_NotRetired()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var equipmentId = await DamageAGenericEquipmentAsync(client);

        var startResponse = await Phase5TestData.StartMaintenanceAsync(client, equipmentId);
        startResponse.EnsureSuccessStatusCode();
        var maintenance = (await startResponse.Content.ReadFromJsonAsync<MaintenanceDetailResponse>(HttpClientTestExtensions.JsonOptions))!;

        var completeResponse = await Phase5TestData.CompleteMaintenanceAsync(client, maintenance.Id, "NotRepairable");
        completeResponse.EnsureSuccessStatusCode();
        var completed = (await completeResponse.Content.ReadFromJsonAsync<MaintenanceDetailResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal("NotRepairable", completed.Outcome!.ToString());

        var equipmentAfterComplete = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal("Damaged", equipmentAfterComplete.Status.ToString());

        // Retirement remains a separate, explicit Administration decision — never automatic.
        // Administration may still choose to deliberately retire the now-Damaged, non-repairable
        // equipment (see EquipmentTests.Retire_FromDamaged_Succeeds for the dedicated coverage).
    }

    [Fact]
    public async Task StartMaintenance_WhenAlreadyActive_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var equipmentId = await DamageAGenericEquipmentAsync(client);
        (await Phase5TestData.StartMaintenanceAsync(client, equipmentId)).EnsureSuccessStatusCode();

        var secondResponse = await Phase5TestData.StartMaintenanceAsync(client, equipmentId);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task StartMaintenance_ConcurrentForSameEquipment_OnlyOneSucceeds()
    {
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();
        var equipmentId = await DamageAGenericEquipmentAsync(clientA);

        var results = await Task.WhenAll(
            Phase5TestData.StartMaintenanceAsync(clientA, equipmentId),
            Phase5TestData.StartMaintenanceAsync(clientB, equipmentId));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Hr_CanStartAndCompleteMaintenance()
    {
        using var adminClient = await LoginAsNewAdministratorAsync();
        var equipmentId = await DamageAGenericEquipmentAsync(adminClient);

        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR Maintenance, Lisboa");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);

        var startResponse = await Phase5TestData.StartMaintenanceAsync(hrClient, equipmentId);
        startResponse.EnsureSuccessStatusCode();
        var maintenance = (await startResponse.Content.ReadFromJsonAsync<MaintenanceDetailResponse>(HttpClientTestExtensions.JsonOptions))!;

        var completeResponse = await Phase5TestData.CompleteMaintenanceAsync(hrClient, maintenance.Id, "Repaired");
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
    }

    private Task<Guid> DamageAGenericEquipmentAsync(HttpClient client) => Phase5TestData.DamageAGenericEquipmentAsync(client, factory);

    [Fact]
    public async Task List_Summary_IncludesEquipmentTypeManufacturerAndModel()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var equipmentId = await DamageAGenericEquipmentAsync(client);
        (await Phase5TestData.StartMaintenanceAsync(client, equipmentId)).EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<PagedResponse<MaintenanceSummaryResponse>>(
            $"/api/maintenance?equipmentId={equipmentId}", HttpClientTestExtensions.JsonOptions);

        var entry = Assert.Single(list!.Items);
        Assert.False(string.IsNullOrWhiteSpace(entry.EquipmentTypeName));
        Assert.Equal("Logitech", entry.Manufacturer);
        Assert.Equal("MX Master", entry.Model);
    }

    private static async Task<EquipmentDetailResponse> GetEquipmentAsync(HttpClient client, Guid equipmentId)
    {
        var response = await client.GetAsync($"/api/equipment/{equipmentId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
