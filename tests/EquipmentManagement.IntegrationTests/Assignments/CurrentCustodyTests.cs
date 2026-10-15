using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Assignments;

/// <summary>
/// Current-custody visibility on Consultant Detail, Equipment Detail, and the
/// Equipment list — all three derived exclusively from the Active-status
/// <see cref="EquipmentManagement.Domain.Handovers.EquipmentAssignment"/>, never
/// from Equipment.Status or historical Handover/Return records (client-demo
/// custody-visibility improvement).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class CurrentCustodyTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task ConsultantDetail_TwoActiveAssignments_ReturnsExactlyThoseTwoEquipmentItems()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: $"Duplo {Guid.NewGuid():N}");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipmentA = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        var equipmentB = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        await FinalizeSimpleHandoverAsync(client, consultantId, equipmentA.Id);
        await FinalizeSimpleHandoverAsync(client, consultantId, equipmentB.Id);

        var response = await client.GetAsync($"/api/consultants/{consultantId}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConsultantDetailResponse>(HttpClientTestExtensions.JsonOptions);

        Assert.Equal(2, body!.CurrentAssignedEquipment.Count);
        var equipmentIds = body.CurrentAssignedEquipment.Select(a => a.EquipmentId).ToHashSet();
        Assert.Contains(equipmentA.Id, equipmentIds);
        Assert.Contains(equipmentB.Id, equipmentIds);
        Assert.All(body.CurrentAssignedEquipment, a => Assert.Equal("Active", a.Status.ToString()));
    }

    [Fact]
    public async Task ConsultantDetail_EndedAssignment_IsNotReturnedAsCurrentEquipment()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: $"Devolvido {Guid.NewGuid():N}");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        await FinalizeSimpleHandoverAsync(client, consultantId, equipment.Id);
        await ReturnAndReceiveWithoutDamageAsync(client, equipment.Id);

        var response = await client.GetAsync($"/api/consultants/{consultantId}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConsultantDetailResponse>(HttpClientTestExtensions.JsonOptions);

        Assert.Empty(body!.CurrentAssignedEquipment);
    }

    [Fact]
    public async Task ConsultantDetail_NoAssignments_ReturnsEmptyCollection()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: $"SemEquipamento {Guid.NewGuid():N}");

        var response = await client.GetAsync($"/api/consultants/{consultantId}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConsultantDetailResponse>(HttpClientTestExtensions.JsonOptions);

        Assert.NotNull(body!.CurrentAssignedEquipment);
        Assert.Empty(body.CurrentAssignedEquipment);
    }

    [Fact]
    public async Task EquipmentDetail_Assigned_ReturnsActiveConsultantAsCurrentCustody()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: $"Custodia {Guid.NewGuid():N}");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        await FinalizeSimpleHandoverAsync(client, consultantId, equipment.Id);

        var response = await client.GetAsync($"/api/equipment/{equipment.Id}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions);

        Assert.NotNull(body!.CurrentCustody);
        Assert.Equal(consultantId, body.CurrentCustody!.ConsultantId);
    }

    [Fact]
    public async Task EquipmentDetail_Available_HasNoCurrentCustody()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        var response = await client.GetAsync($"/api/equipment/{equipment.Id}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions);

        Assert.Equal("Available", body!.Status.ToString());
        Assert.Null(body.CurrentCustody);
    }

    [Fact]
    public async Task EquipmentDetail_ReturnedAssignment_IsNotPresentedAsCurrentCustody()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: $"Histórico {Guid.NewGuid():N}");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        await FinalizeSimpleHandoverAsync(client, consultantId, equipment.Id);
        await ReturnAndReceiveWithoutDamageAsync(client, equipment.Id);

        var response = await client.GetAsync($"/api/equipment/{equipment.Id}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions);

        Assert.Null(body!.CurrentCustody);
    }

    [Fact]
    public async Task EquipmentList_AssignedEquipment_IncludesCurrentConsultantNameAndId()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: $"Listagem {Guid.NewGuid():N}");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        await FinalizeSimpleHandoverAsync(client, consultantId, equipment.Id);

        var row = await GetEquipmentSummaryAsync(client, equipment.AssetTag);

        Assert.Equal(consultantId, row.CurrentConsultantId);
        Assert.False(string.IsNullOrWhiteSpace(row.CurrentConsultantName));
    }

    [Fact]
    public async Task EquipmentList_UnassignedEquipment_HasNoCurrentConsultant()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        var row = await GetEquipmentSummaryAsync(client, equipment.AssetTag);

        Assert.Null(row.CurrentConsultantId);
        Assert.Null(row.CurrentConsultantName);
    }

    /// <summary>
    /// The most important scenario: equipment assigned to Consultant A, returned,
    /// then assigned to Consultant B. Current custody must resolve to B — never
    /// A — in every one of the three surfaces, and A's current-equipment list
    /// must no longer include this equipment.
    /// </summary>
    [Fact]
    public async Task Reassignment_CustodyMovesFromConsultantAToConsultantB_EverywhereItIsShown()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantAId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: $"Consultor A {Guid.NewGuid():N}");
        var (consultantBId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: $"Consultor B {Guid.NewGuid():N}");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        await FinalizeSimpleHandoverAsync(client, consultantAId, equipment.Id);
        await ReturnAndReceiveWithoutDamageAsync(client, equipment.Id);
        await FinalizeSimpleHandoverAsync(client, consultantBId, equipment.Id);

        var equipmentDetail = await client.GetFromJsonAsync<EquipmentDetailResponse>(
            $"/api/equipment/{equipment.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.NotNull(equipmentDetail!.CurrentCustody);
        Assert.Equal(consultantBId, equipmentDetail.CurrentCustody!.ConsultantId);
        Assert.NotEqual(consultantAId, equipmentDetail.CurrentCustody.ConsultantId);

        var consultantB = await client.GetFromJsonAsync<ConsultantDetailResponse>(
            $"/api/consultants/{consultantBId}", HttpClientTestExtensions.JsonOptions);
        Assert.Contains(consultantB!.CurrentAssignedEquipment, a => a.EquipmentId == equipment.Id);

        var consultantA = await client.GetFromJsonAsync<ConsultantDetailResponse>(
            $"/api/consultants/{consultantAId}", HttpClientTestExtensions.JsonOptions);
        Assert.DoesNotContain(consultantA!.CurrentAssignedEquipment, a => a.EquipmentId == equipment.Id);

        var row = await GetEquipmentSummaryAsync(client, equipment.AssetTag);
        Assert.Equal(consultantBId, row.CurrentConsultantId);
        Assert.NotEqual(consultantAId, row.CurrentConsultantId);
    }

    private static async Task FinalizeSimpleHandoverAsync(HttpClient client, Guid consultantId, Guid equipmentId)
    {
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipmentId, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();
    }

    private async Task ReturnAndReceiveWithoutDamageAsync(HttpClient client, Guid equipmentId)
    {
        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, equipmentId);
        var equipmentReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: $"Rua Receiver {Guid.NewGuid():N}, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, equipmentReturn.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId))
            .EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();
    }

    /// <summary>
    /// The list endpoint has no filter-by-id and the shared test database
    /// accumulates rows across the whole suite run, so this searches by the
    /// equipment's own (unique) asset tag rather than paging for it.
    /// </summary>
    private static async Task<EquipmentSummaryResponse> GetEquipmentSummaryAsync(HttpClient client, string assetTag)
    {
        var response = await client.GetFromJsonAsync<PagedResponse<EquipmentSummaryResponse>>(
            $"/api/equipment?search={Uri.EscapeDataString(assetTag)}", HttpClientTestExtensions.JsonOptions);
        return response!.Items.Single(i => i.AssetTag == assetTag);
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
