using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.ResponsibilityTerms;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Handovers;

/// <summary>
/// Carrier delivery: Available -> (Registar envio) InTransit -> (Confirmar
/// entrega) Assigned. InTransit is never custody: no Assignment exists until
/// the confirmation. In-person delivery never passes through InTransit.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class InTransitLifecycleTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Carrier_DraftKeepsAvailable_DispatchMovesToInTransitWithoutCustody_ConfirmationAssigns()
    {
        using var client = await LoginAsync("HR");
        var (equipmentId, _) = await CreateGenericEquipmentAsync(client);
        var (handover, _) = await PrepareDraftAsync(client, equipmentId, "Carrier");

        // A. Draft: still Available.
        Assert.Equal(EquipmentStatus.Available, (await GetEquipmentAsync(client, equipmentId)).Status);

        // B. Registar envio: InTransit, no Assignment, no custody.
        (await Phase4TestData.RegisterDispatchAsync(client, handover.Id)).EnsureSuccessStatusCode();
        var inTransit = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal(EquipmentStatus.InTransit, inTransit.Status);
        Assert.Null(inTransit.CurrentCustody);
        Assert.Equal(0, await CountAssignmentsAsync(equipmentId));
        Assert.Equal(EquipmentStatus.InTransit, Assert.Single((await GetHandoverAsync(client, handover.Id)).Items).EquipmentStatus);
        await AssertAuditTransitionAsync(AuditActions.EquipmentDispatch.Registered, handover.Id, "Available", "InTransit");

        // C. Confirmar entrega: Assigned, Assignment created, custody begins.
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();
        var assigned = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal(EquipmentStatus.Assigned, assigned.Status);
        Assert.NotNull(assigned.CurrentCustody);
        Assert.Equal(1, await CountAssignmentsAsync(equipmentId, AssignmentStatus.Active));
        await AssertAuditTransitionAsync(AuditActions.EquipmentHandover.Finalized, handover.Id, "InTransit", "Assigned");
    }

    [Fact]
    public async Task InPerson_GoesFromAvailableStraightToAssigned_NeverInTransit()
    {
        using var client = await LoginAsync("HR");
        var (equipmentId, _) = await CreateGenericEquipmentAsync(client);
        var (handover, _) = await PrepareDraftAsync(client, equipmentId, "InPerson");

        Assert.Equal(EquipmentStatus.Available, (await GetEquipmentAsync(client, equipmentId)).Status);

        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, equipmentId)).Status);
        Assert.Equal(1, await CountAssignmentsAsync(equipmentId, AssignmentStatus.Active));
        await AssertAuditTransitionAsync(AuditActions.EquipmentHandover.Finalized, handover.Id, "Available", "Assigned");
    }

    [Fact]
    public async Task InTransit_CannotBeAddedToANewDraft_AndIsNotOfferedAsAvailable()
    {
        using var client = await LoginAsync("HR");
        var (equipmentId, typeId) = await CreateDispatchedEquipmentAsync(client);

        var (otherConsultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var newDraft = await Phase4TestData.CreateDraftHandoverAsync(client, otherConsultantId);
        var add = await Phase4TestData.AddItemAsync(client, newDraft.Id, equipmentId);

        Assert.Equal(HttpStatusCode.Conflict, add.StatusCode);
        Assert.Empty((await GetHandoverAsync(client, newDraft.Id)).Items);
        Assert.DoesNotContain(await ListEquipmentIdsAsync(client, "Available", typeId), id => id == equipmentId);
    }

    [Fact]
    public async Task CompetingDrafts_FirstDispatchWins_OtherDraftsKeepTheItemButCannotProgress()
    {
        using var client = await LoginAsync("HR");
        var (equipmentId, _) = await CreateGenericEquipmentAsync(client);

        // The same Available item in three Drafts: allowed until one commits it.
        var (carrierA, _) = await PrepareDraftAsync(client, equipmentId, "Carrier");
        var (inPersonB, _) = await PrepareDraftAsync(client, equipmentId, "InPerson");
        var (carrierC, _) = await PrepareDraftAsync(client, equipmentId, "Carrier");

        (await Phase4TestData.RegisterDispatchAsync(client, carrierA.Id)).EnsureSuccessStatusCode();

        // B is not deleted or edited: the item is still there, shown as InTransit.
        var b = await GetHandoverAsync(client, inPersonB.Id);
        Assert.Equal(HandoverStatus.Draft, b.Status);
        Assert.Equal(EquipmentStatus.InTransit, Assert.Single(b.Items).EquipmentStatus);

        var finalizeB = await Phase4TestData.FinalizeAsync(client, inPersonB.Id);
        Assert.Equal(HttpStatusCode.Conflict, finalizeB.StatusCode);
        Assert.Contains("já não está disponível", await finalizeB.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var dispatchC = await Phase4TestData.RegisterDispatchAsync(client, carrierC.Id);
        Assert.Equal(HttpStatusCode.Conflict, dispatchC.StatusCode);
        Assert.Contains("já não está disponível", await dispatchC.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(client, equipmentId)).Status);
        Assert.Equal(0, await CountAssignmentsAsync(equipmentId));
        Assert.Null((await GetHandoverAsync(client, carrierC.Id)).Dispatch);
        Assert.Equal(HandoverStatus.Draft, (await GetHandoverAsync(client, inPersonB.Id)).Status);
    }

    [Fact]
    public async Task Race_TwoCarrierDispatchesOfTheSameEquipment_ExactlyOneWins()
    {
        using var clientA = await LoginAsync("HR");
        using var clientB = await LoginAsync("HR");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (equipmentId, _) = await CreateGenericEquipmentAsync(clientA);
            var (draftA, _) = await PrepareDraftAsync(clientA, equipmentId, "Carrier");
            var (draftB, _) = await PrepareDraftAsync(clientB, equipmentId, "Carrier");

            var results = await Task.WhenAll(
                Phase4TestData.RegisterDispatchAsync(clientA, draftA.Id),
                Phase4TestData.RegisterDispatchAsync(clientB, draftB.Id));

            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

            Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(clientA, equipmentId)).Status);
            Assert.Equal(0, await CountAssignmentsAsync(equipmentId));
            await using var scope = factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await dbContext.EquipmentDispatches.CountAsync(d => d.EquipmentHandoverId == draftA.Id || d.EquipmentHandoverId == draftB.Id));
        }
    }

    [Fact]
    public async Task Race_CarrierDispatchVersusInPersonConfirmation_ExactlyOneCommitsTheEquipment()
    {
        using var clientA = await LoginAsync("HR");
        using var clientB = await LoginAsync("HR");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (equipmentId, _) = await CreateGenericEquipmentAsync(clientA);
            var (carrier, _) = await PrepareDraftAsync(clientA, equipmentId, "Carrier");
            var (inPerson, _) = await PrepareDraftAsync(clientB, equipmentId, "InPerson");

            var results = await Task.WhenAll(
                Phase4TestData.RegisterDispatchAsync(clientA, carrier.Id),
                Phase4TestData.FinalizeAsync(clientB, inPerson.Id));

            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

            var equipment = await GetEquipmentAsync(clientA, equipmentId);
            if (results[0].StatusCode == HttpStatusCode.OK)
            {
                // Dispatch won: shipped, no custody; the in-person Draft did not finalize.
                Assert.Equal(EquipmentStatus.InTransit, equipment.Status);
                Assert.Equal(0, await CountAssignmentsAsync(equipmentId));
                Assert.Equal(HandoverStatus.Draft, (await GetHandoverAsync(clientA, inPerson.Id)).Status);
            }
            else
            {
                // Confirmation won: custody; the carrier Draft has no dispatch.
                Assert.Equal(EquipmentStatus.Assigned, equipment.Status);
                Assert.Equal(1, await CountAssignmentsAsync(equipmentId, AssignmentStatus.Active));
                Assert.Null((await GetHandoverAsync(clientA, carrier.Id)).Dispatch);
            }
        }
    }

    [Fact]
    public async Task ResponsibilityTerm_WhileInTransit_GeneratesButCannotBeSentOrSigned_ThenTheSameTermIsSentAfterConfirmation()
    {
        using var client = await LoginAsync("HR");
        var (equipmentId, _) = await CreateGenericEquipmentAsync(client);
        var (handover, _) = await PrepareDraftAsync(client, equipmentId, "Carrier");
        (await Phase4TestData.RegisterDispatchAsync(client, handover.Id)).EnsureSuccessStatusCode();
        Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(client, equipmentId)).Status);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var generate = await client.PostWithCsrfAsync<object?>($"/api/equipment-handovers/{handover.Id}/responsibility-terms", null, csrfToken);
        Assert.Equal(HttpStatusCode.Created, generate.StatusCode);
        var term = (await generate.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(HttpStatusCode.Conflict, (await Phase7TestData.SendAsync(client, term.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Phase7TestData.UploadSignedDocumentAsync(client, term.Id)).StatusCode);
        Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(client, equipmentId)).Status);

        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, equipmentId)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Phase7TestData.SendAsync(client, term.Id)).StatusCode);
    }

    [Fact]
    public async Task InTransit_IsNotEligibleForRetirementRetirementRequestOrMaintenance()
    {
        using var admin = await LoginAsync("Administration");
        using var hr = await LoginAsync("HR");
        var (equipmentId, _) = await CreateDispatchedEquipmentAsync(hr);

        var adminCsrf = await admin.GetAntiforgeryTokenAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostWithCsrfAsync<object?>($"/api/equipment/{equipmentId}/retire", null, adminCsrf)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await admin.PostWithCsrfAsync($"/api/equipment/{equipmentId}/maintenance", new { Notes = "x" }, adminCsrf)).StatusCode);

        var hrCsrf = await hr.GetAntiforgeryTokenAsync();
        var request = await hr.PostWithCsrfAsync("/api/equipment-retirement-requests", new { EquipmentId = equipmentId, Reason = "Sem uso" }, hrCsrf);
        Assert.Equal(HttpStatusCode.Conflict, request.StatusCode);

        Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(hr, equipmentId)).Status);
        Assert.Equal(0, await CountAssignmentsAsync(equipmentId));
    }

    [Fact]
    public async Task ListFilterAndStats_CountInTransitOnce_AndNeverAsAvailable()
    {
        using var client = await LoginAsync("HR");
        var (inTransitId, typeId) = await CreateDispatchedEquipmentAsync(client);
        var availableId = (await Phase3TestData.CreateGenericEquipmentAsync(client, typeId)).Id;

        Assert.Equal([inTransitId], await ListEquipmentIdsAsync(client, "InTransit", typeId));
        Assert.Equal([availableId], await ListEquipmentIdsAsync(client, "Available", typeId));

        var stats = (await client.GetFromJsonAsync<EquipmentStatsResponse>($"/api/equipment/stats?equipmentTypeId={typeId}", HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(2, stats.TotalCount);
        Assert.Equal(1, stats.ByStatus.Single(s => s.Status == EquipmentStatus.InTransit).Count);
        Assert.Equal(1, stats.ByStatus.Single(s => s.Status == EquipmentStatus.Available).Count);

        // The JSON carries the status by name.
        var json = await client.GetStringAsync($"/api/equipment/{inTransitId}");
        Assert.Contains("\"status\":\"InTransit\"", json, StringComparison.Ordinal);
    }

    private static async Task<(Guid EquipmentId, Guid TypeId)> CreateDispatchedEquipmentAsync(HttpClient client)
    {
        var (equipmentId, typeId) = await CreateGenericEquipmentAsync(client);
        var (handover, _) = await PrepareDraftAsync(client, equipmentId, "Carrier");
        (await Phase4TestData.RegisterDispatchAsync(client, handover.Id)).EnsureSuccessStatusCode();
        return (equipmentId, typeId);
    }

    private static async Task<(Guid EquipmentId, Guid TypeId)> CreateGenericEquipmentAsync(HttpClient client)
    {
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        return (equipment.Id, type.Id);
    }

    /// <summary>A Draft for a new Consultant with this one item inspected and a delivery date set — ready to dispatch (carrier) or confirm (in person).</summary>
    private static async Task<(EquipmentHandoverDetailResponse Handover, Guid ItemId)> PrepareDraftAsync(HttpClient client, Guid equipmentId, string deliveryMethod)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        return await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipmentId, photoCount: 0, deliveryMethod: deliveryMethod);
    }

    private async Task<int> CountAssignmentsAsync(Guid equipmentId, AssignmentStatus? status = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.EquipmentAssignments.CountAsync(a => a.EquipmentId == equipmentId && (status == null || a.Status == status));
    }

    private async Task AssertAuditTransitionAsync(string action, Guid handoverId, string from, string to)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entry = await dbContext.AuditLogs.SingleAsync(a => a.Action == action && a.EntityId == handoverId.ToString());
        using var json = System.Text.Json.JsonDocument.Parse(entry.NewValuesJson!);
        Assert.Equal(from, json.RootElement.GetProperty("equipmentStatusFrom").GetString());
        Assert.Equal(to, json.RootElement.GetProperty("equipmentStatusTo").GetString());
    }

    private static async Task<List<Guid>> ListEquipmentIdsAsync(HttpClient client, string status, Guid typeId)
    {
        var page = await client.GetFromJsonAsync<PagedResponse<EquipmentSummaryResponse>>(
            $"/api/equipment?status={status}&equipmentTypeId={typeId}&pageSize=100", HttpClientTestExtensions.JsonOptions);
        return page!.Items.Select(e => e.Id).ToList();
    }

    private static async Task<EquipmentDetailResponse> GetEquipmentAsync(HttpClient client, Guid equipmentId) =>
        (await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipmentId}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<EquipmentHandoverDetailResponse> GetHandoverAsync(HttpClient client, Guid handoverId) =>
        (await client.GetFromJsonAsync<EquipmentHandoverDetailResponse>($"/api/equipment-handovers/{handoverId}", HttpClientTestExtensions.JsonOptions))!;

    private async Task<HttpClient> LoginAsync(string role)
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, role);
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
