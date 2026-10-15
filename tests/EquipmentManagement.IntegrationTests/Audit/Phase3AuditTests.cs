using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Audit;

/// <summary>
/// Phase 3 (EquipmentType/Equipment) audit events
/// (Phase 3 scope).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class Phase3AuditTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task CreatingAnEquipmentType_RecordsAnAuditEvent()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client);

        var auditResponse = await client.GetAsync($"/api/audit?entityType=EquipmentType&entityId={type.Id}&action=EquipmentType.Created");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        Assert.Contains(page!.Items, e => e.Action == "EquipmentType.Created");
    }

    [Fact]
    public async Task CreatingEquipment_RecordsAnAuditEventWithAssetTag()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        var auditResponse = await client.GetAsync($"/api/audit?entityType=Equipment&entityId={equipment.Id}&action=Equipment.Created");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        var entry = Assert.Single(page!.Items);
        Assert.Contains(equipment.AssetTag, entry.NewValuesJson);
    }

    [Fact]
    public async Task UpdatingEquipment_RecordsChangedFieldsAndAssetTag()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        await client.PutWithCsrfAsync($"/api/equipment/{equipment.Id}", new
        {
            Manufacturer = "Brand New Manufacturer",
            Model = equipment.Model,
            SerialNumber = equipment.SerialNumber,
        }, csrfToken);

        var auditResponse = await client.GetAsync($"/api/audit?entityType=Equipment&entityId={equipment.Id}&action=Equipment.Updated");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        var entry = Assert.Single(page!.Items);
        Assert.Contains(equipment.AssetTag, entry.MetadataJson);
        Assert.Contains("manufacturer", entry.MetadataJson);
    }

    [Fact]
    public async Task RetiringEquipment_RecordsAnAuditEvent()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        await client.PostWithCsrfAsync<object?>($"/api/equipment/{equipment.Id}/retire", null, csrfToken);

        var auditResponse = await client.GetAsync($"/api/audit?entityType=Equipment&entityId={equipment.Id}&action=Equipment.Retired");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        var entry = Assert.Single(page!.Items);
        Assert.Contains(equipment.AssetTag, entry.NewValuesJson);
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
