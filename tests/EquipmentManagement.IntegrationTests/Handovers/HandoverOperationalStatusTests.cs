using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.ResponsibilityTerms;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Handovers;

/// <summary>
/// Iteration 1: the derived, user-facing
/// <c>HandoverOperationalStatus</c> (Rascunho / Aguardando termo assinado /
/// Finalizada) must never be confused with the real physical-handover
/// lifecycle (<c>HandoverStatus</c>), and must never falsely report
/// "Finalizada" from a stale/superseded signed version. This is a mandatory
/// regression suite for the Iteration 1 operational status.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class HandoverOperationalStatusTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Draft_ReportsDraftOperationalStatus()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var handover = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId);

        var detail = await GetDetailAsync(client, handover.Id);
        Assert.Equal("Draft", detail.Status.ToString());
        Assert.Equal("Draft", detail.OperationalStatus.ToString());

        var list = await client.GetFromJsonAsync<PagedResponse<EquipmentHandoverSummaryResponse>>(
            "/api/equipment-handovers", HttpClientTestExtensions.JsonOptions);
        var row = list!.Items.Single(h => h.Id == handover.Id);
        Assert.Equal("Draft", row.OperationalStatus.ToString());
    }

    [Fact]
    public async Task FinalizedWithNoTermGenerated_IsAwaitingSignedTerm()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, _, _) = await CreateFinalizedHandoverAsync(client);

        var detail = await GetDetailAsync(client, handover.Id);
        Assert.Equal("Finalized", detail.Status.ToString());
        Assert.Equal("AwaitingSignedTerm", detail.OperationalStatus.ToString());
    }

    [Fact]
    public async Task TermGeneratedButUnsigned_IsAwaitingSignedTerm()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, _, _) = await CreateFinalizedHandoverAsync(client);
        (await GenerateTermAsync(client, handover.Id)).EnsureSuccessStatusCode();

        var detail = await GetDetailAsync(client, handover.Id);
        Assert.Equal("AwaitingSignedTerm", detail.OperationalStatus.ToString());
    }

    [Fact]
    public async Task CurrentTermSigned_IsCompleted()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, _, _) = await CreateFinalizedHandoverAsync(client);
        var termResponse = await GenerateTermAsync(client, handover.Id);
        termResponse.EnsureSuccessStatusCode();
        var term = (await termResponse.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;

        (await Phase7TestData.UploadSignedDocumentAsync(client, term.Id)).EnsureSuccessStatusCode();

        var detail = await GetDetailAsync(client, handover.Id);
        Assert.Equal("Completed", detail.OperationalStatus.ToString());

        var list = await client.GetFromJsonAsync<PagedResponse<EquipmentHandoverSummaryResponse>>(
            "/api/equipment-handovers", HttpClientTestExtensions.JsonOptions);
        Assert.Equal("Completed", list!.Items.Single(h => h.Id == handover.Id).OperationalStatus.ToString());
    }

    /// <summary>
    /// Mandatory regression: a signed OLD version must never
    /// falsely complete the current delivery once a newer, unsigned version
    /// has been generated — only the current (Status == Generated) term's
    /// signature counts.
    /// </summary>
    [Fact]
    public async Task SupersededSignedVersion_DoesNotCompleteCurrentDelivery()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, _, _) = await CreateFinalizedHandoverAsync(client);

        var v1Response = await GenerateTermAsync(client, handover.Id);
        v1Response.EnsureSuccessStatusCode();
        var v1 = (await v1Response.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;
        (await Phase7TestData.UploadSignedDocumentAsync(client, v1.Id)).EnsureSuccessStatusCode();

        // v1 is now signed and (at this point) current -> Completed.
        Assert.Equal("Completed", (await GetDetailAsync(client, handover.Id)).OperationalStatus.ToString());

        // Regenerating supersedes v1; the new current version (v2) has no signature yet.
        var v2Response = await GenerateTermAsync(client, handover.Id);
        v2Response.EnsureSuccessStatusCode();
        var v2 = (await v2Response.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.NotEqual(v1.Id, v2.Id);

        var detail = await GetDetailAsync(client, handover.Id);
        Assert.Equal("AwaitingSignedTerm", detail.OperationalStatus.ToString());

        // Signing the now-current v2 is what completes it again.
        (await Phase7TestData.UploadSignedDocumentAsync(client, v2.Id)).EnsureSuccessStatusCode();
        Assert.Equal("Completed", (await GetDetailAsync(client, handover.Id)).OperationalStatus.ToString());
    }

    /// <summary>Item C10: while AwaitingSignedTerm, custody must already be fully transferred — the equipment is Assigned with an Active assignment to the consultant, never Available.</summary>
    [Fact]
    public async Task WhileAwaitingSignedTerm_CustodyIsAlreadyFullyTransferred()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, consultantId, equipmentId) = await CreateFinalizedHandoverAsync(client);

        var detail = await GetDetailAsync(client, handover.Id);
        Assert.Equal("AwaitingSignedTerm", detail.OperationalStatus.ToString());

        var equipment = await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipmentId}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal("Assigned", equipment!.Status.ToString());
        Assert.NotNull(equipment.CurrentCustody);
        Assert.Equal(consultantId, equipment.CurrentCustody!.ConsultantId);

        var assignments = await client.GetFromJsonAsync<PagedResponse<EquipmentAssignmentResponse>>(
            $"/api/equipment-assignments?equipmentId={equipmentId}&status=Active", HttpClientTestExtensions.JsonOptions);
        Assert.Single(assignments!.Items);
        Assert.Equal("Active", assignments.Items[0].Status.ToString());
    }

    private static async Task<HttpResponseMessage> GenerateTermAsync(HttpClient client, Guid handoverId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync<object?>($"/api/equipment-handovers/{handoverId}/responsibility-terms", null, csrfToken);
    }

    private static async Task<EquipmentHandoverDetailResponse> GetDetailAsync(HttpClient client, Guid handoverId) =>
        (await client.GetFromJsonAsync<EquipmentHandoverDetailResponse>($"/api/equipment-handovers/{handoverId}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<(EquipmentHandoverDetailResponse Handover, Guid ConsultantId, Guid EquipmentId)> CreateFinalizedHandoverAsync(HttpClient client)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

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
