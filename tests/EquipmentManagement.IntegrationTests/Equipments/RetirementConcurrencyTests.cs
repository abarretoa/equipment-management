using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Equipments;

/// <summary>
/// Retirement against concurrent lifecycle operations, interleaved
/// deterministically with <see cref="CommandGate"/>: the retirement's
/// Equipment UPDATE is held after it has decided the item is eligible, a
/// competing "Registar envio" or "Confirmar entrega" commits in that gap, and
/// only then is the retirement released. Retired equipment must never end up
/// with an active dispatch or an Active assignment; the late retirement must
/// be a clean 409, never a lost update and never a 500.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class RetirementConcurrencyTests(CommandGateFixture fixture) : IClassFixture<CommandGateFixture>
{
    [Fact]
    public async Task DirectRetire_HeldAfterItsCheck_WhileACarrierDispatchCommits_IsAConflict_AndTheItemStaysInTransit()
    {
        using var admin = await fixture.LoginAsync("Administration");
        using var hr = await fixture.LoginAsync("HR");
        var equipmentId = await CreateGenericEquipmentAsync(hr);
        var handoverId = await PrepareDraftAsync(hr, equipmentId, "Carrier");

        fixture.Gate.Arm(CommandGate.EquipmentStatusUpdate(nameof(EquipmentStatus.Retired)));
        var retireTask = RetireAsync(admin, equipmentId);
        await fixture.Gate.WaitUntilHeldAsync(retireTask);

        Assert.Equal(HttpStatusCode.OK, (await Phase4TestData.RegisterDispatchAsync(hr, handoverId)).StatusCode);
        fixture.Gate.Release();
        var retire = await retireTask;

        Assert.Equal(HttpStatusCode.Conflict, retire.StatusCode);
        Assert.Equal(EquipmentStatus.InTransit, await GetStatusAsync(equipmentId));
        Assert.Equal(1, await CountActiveDispatchesAsync(handoverId));
        Assert.Equal(0, await CountActiveAssignmentsAsync(equipmentId));
    }

    [Fact]
    public async Task DirectRetire_HeldAfterItsCheck_WhileAnInPersonConfirmationCommits_IsAConflict_AndCustodyIsKept()
    {
        using var admin = await fixture.LoginAsync("Administration");
        using var hr = await fixture.LoginAsync("HR");
        var equipmentId = await CreateGenericEquipmentAsync(hr);
        var handoverId = await PrepareDraftAsync(hr, equipmentId, "InPerson");

        fixture.Gate.Arm(CommandGate.EquipmentStatusUpdate(nameof(EquipmentStatus.Retired)));
        var retireTask = RetireAsync(admin, equipmentId);
        await fixture.Gate.WaitUntilHeldAsync(retireTask);

        Assert.Equal(HttpStatusCode.OK, (await Phase4TestData.FinalizeAsync(hr, handoverId)).StatusCode);
        fixture.Gate.Release();
        var retire = await retireTask;

        Assert.Equal(HttpStatusCode.Conflict, retire.StatusCode);
        Assert.Equal(EquipmentStatus.Assigned, await GetStatusAsync(equipmentId));
        Assert.Equal(1, await CountActiveAssignmentsAsync(equipmentId));
    }

    /// <summary>The opposite order: the dispatch is held after reading the item as Available, the retirement commits, then the dispatch is released.</summary>
    [Fact]
    public async Task CarrierDispatch_HeldAfterItsCheck_WhileADirectRetireCommits_IsAConflict_AndNothingIsShipped()
    {
        using var admin = await fixture.LoginAsync("Administration");
        using var hr = await fixture.LoginAsync("HR");
        var equipmentId = await CreateGenericEquipmentAsync(hr);
        var handoverId = await PrepareDraftAsync(hr, equipmentId, "Carrier");

        fixture.Gate.Arm(CommandGate.EquipmentStatusUpdate(nameof(EquipmentStatus.InTransit)));
        var dispatchTask = Phase4TestData.RegisterDispatchAsync(hr, handoverId);
        await fixture.Gate.WaitUntilHeldAsync(dispatchTask);

        Assert.Equal(HttpStatusCode.OK, (await RetireAsync(admin, equipmentId)).StatusCode);
        fixture.Gate.Release();
        var dispatch = await dispatchTask;

        Assert.Equal(HttpStatusCode.Conflict, dispatch.StatusCode);
        Assert.Equal(EquipmentStatus.Retired, await GetStatusAsync(equipmentId));
        Assert.Equal(0, await CountActiveDispatchesAsync(handoverId));
    }

    /// <summary>
    /// "Aprovar abate" owns a Serializable transaction and runs RetireAsync
    /// inside it. A dispatch committing between its eligibility read and its
    /// write makes PostgreSQL reject the write with a serialization failure,
    /// which must surface as 409 with nothing committed: the request stays
    /// Pending and the item stays InTransit.
    /// </summary>
    [Fact]
    public async Task ApproveRetirement_HeldAfterItsCheck_WhileACarrierDispatchCommits_IsAConflict_NotA500()
    {
        using var admin = await fixture.LoginAsync("Administration");
        using var hr = await fixture.LoginAsync("HR");
        var equipmentId = await CreateGenericEquipmentAsync(hr);
        var handoverId = await PrepareDraftAsync(hr, equipmentId, "Carrier");
        var requestId = await CreateRetirementRequestAsync(hr, equipmentId);

        fixture.Gate.Arm(CommandGate.EquipmentStatusUpdate(nameof(EquipmentStatus.Retired)));
        var approveTask = ApproveAsync(admin, requestId);
        await fixture.Gate.WaitUntilHeldAsync(approveTask);

        Assert.Equal(HttpStatusCode.OK, (await Phase4TestData.RegisterDispatchAsync(hr, handoverId)).StatusCode);
        fixture.Gate.Release();
        var approve = await approveTask;

        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Equal(EquipmentStatus.InTransit, await GetStatusAsync(equipmentId));
        Assert.Equal(1, await CountActiveDispatchesAsync(handoverId));
        Assert.Equal(EquipmentRetirementRequestStatus.Pending, await GetRequestStatusAsync(requestId));
    }

    [Fact]
    public async Task ApproveRetirement_WithoutContention_StillRetiresAndApproves()
    {
        using var admin = await fixture.LoginAsync("Administration");
        using var hr = await fixture.LoginAsync("HR");
        var equipmentId = await CreateGenericEquipmentAsync(hr);
        var requestId = await CreateRetirementRequestAsync(hr, equipmentId);

        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(admin, requestId)).StatusCode);

        Assert.Equal(EquipmentStatus.Retired, await GetStatusAsync(equipmentId));
        Assert.Equal(EquipmentRetirementRequestStatus.Approved, await GetRequestStatusAsync(requestId));
    }

    private static async Task<HttpResponseMessage> RetireAsync(HttpClient client, Guid equipmentId)
    {
        var csrf = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync<object?>($"/api/equipment/{equipmentId}/retire", null, csrf);
    }

    private static async Task<HttpResponseMessage> ApproveAsync(HttpClient client, Guid requestId)
    {
        var csrf = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync<object?>($"/api/equipment-retirement-requests/{requestId}/approve", null, csrf);
    }

    private static async Task<Guid> CreateRetirementRequestAsync(HttpClient client, Guid equipmentId)
    {
        var csrf = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync("/api/equipment-retirement-requests", new { EquipmentId = equipmentId, Reason = "Sem uso" }, csrf);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EquipmentRetirementRequestResponse>(HttpClientTestExtensions.JsonOptions))!.Id;
    }

    private static async Task<Guid> CreateGenericEquipmentAsync(HttpClient client)
    {
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client);
        return (await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id)).Id;
    }

    private static async Task<Guid> PrepareDraftAsync(HttpClient client, Guid equipmentId, string deliveryMethod)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipmentId, photoCount: 0, deliveryMethod: deliveryMethod);
        return handover.Id;
    }

    private async Task<EquipmentStatus> GetStatusAsync(Guid equipmentId) =>
        await QueryAsync(db => db.Equipment.Where(e => e.Id == equipmentId).Select(e => e.Status).SingleAsync());

    private Task<int> CountActiveDispatchesAsync(Guid handoverId) =>
        QueryAsync(db => db.EquipmentDispatches.CountAsync(d => d.EquipmentHandoverId == handoverId && d.CancelledAtUtc == null));

    private Task<int> CountActiveAssignmentsAsync(Guid equipmentId) =>
        QueryAsync(db => db.EquipmentAssignments.CountAsync(a => a.EquipmentId == equipmentId && a.Status == AssignmentStatus.Active));

    private Task<EquipmentRetirementRequestStatus> GetRequestStatusAsync(Guid requestId) =>
        QueryAsync(db => db.EquipmentRetirementRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync());

    private async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = fixture.Users.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
}
