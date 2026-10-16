using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Handovers;

/// <summary>
/// A Handover's composition (items, delivery method) is locked once
/// "Registar envio" commits and becomes history once "Confirmar entrega"
/// commits. These tests interleave a composition edit with those operations
/// deterministically (<see cref="CommandGate"/>), in both orders: the edit held
/// after its lock check while the dispatch/confirmation commits, and the
/// dispatch/confirmation held before its transaction's first read while the
/// edit commits. Either way the outcome must be all-or-nothing and the losing
/// operation a clean 409: never an item added to or removed from a shipped
/// Handover, never equipment shipped or assigned that is no longer part of it.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class HandoverCompositionConcurrencyTests(CommandGateFixture fixture) : IClassFixture<CommandGateFixture>
{
    [Fact]
    public async Task AddItem_HeldAfterItsLockCheck_WhileTheDispatchCommits_IsAConflict_AndTheShipmentIsUnchanged()
    {
        using var hr = await fixture.LoginAsync("HR");
        var shippedId = await CreateGenericEquipmentAsync(hr);
        var lateId = await CreateGenericEquipmentAsync(hr);
        var handoverId = await PrepareDraftAsync(hr, shippedId, "Carrier");

        fixture.Gate.Arm(CommandGate.Sql("INSERT INTO \"EquipmentHandoverItems\""));
        var addTask = Phase4TestData.AddItemAsync(hr, handoverId, lateId);
        await fixture.Gate.WaitUntilHeldAsync(addTask);

        Assert.Equal(HttpStatusCode.OK, (await Phase4TestData.RegisterDispatchAsync(hr, handoverId)).StatusCode);
        fixture.Gate.Release();
        var add = await addTask;

        Assert.Equal(HttpStatusCode.Conflict, add.StatusCode);
        Assert.Equal([shippedId], await GetItemEquipmentIdsAsync(handoverId));
        Assert.Equal(EquipmentStatus.InTransit, await GetStatusAsync(shippedId));
        Assert.Equal(EquipmentStatus.Available, await GetStatusAsync(lateId));
    }

    [Fact]
    public async Task RemoveItem_HeldAfterItsLockCheck_WhileTheDispatchCommits_IsAConflict_AndTheShippedItemStays()
    {
        using var hr = await fixture.LoginAsync("HR");
        var keptId = await CreateGenericEquipmentAsync(hr);
        var removedId = await CreateGenericEquipmentAsync(hr);
        var handoverId = await PrepareDraftAsync(hr, keptId, "Carrier");
        var removedItemId = await AddItemAsync(hr, handoverId, removedId);

        fixture.Gate.Arm(CommandGate.Sql("DELETE FROM \"EquipmentHandoverItems\""));
        var removeTask = RemoveItemAsync(hr, handoverId, removedItemId);
        await fixture.Gate.WaitUntilHeldAsync(removeTask);

        Assert.Equal(HttpStatusCode.OK, (await Phase4TestData.RegisterDispatchAsync(hr, handoverId)).StatusCode);
        fixture.Gate.Release();
        var remove = await removeTask;

        Assert.Equal(HttpStatusCode.Conflict, remove.StatusCode);
        Assert.Equal([keptId, removedId], await GetItemEquipmentIdsAsync(handoverId));
        Assert.Equal(EquipmentStatus.InTransit, await GetStatusAsync(keptId));
        Assert.Equal(EquipmentStatus.InTransit, await GetStatusAsync(removedId));
    }

    [Fact]
    public async Task Dispatch_HeldBeforeItsTransactionReads_WhileAnItemIsRemoved_IsAConflict_AndNothingIsShipped()
    {
        using var hr = await fixture.LoginAsync("HR");
        var keptId = await CreateGenericEquipmentAsync(hr);
        var removedId = await CreateGenericEquipmentAsync(hr);
        var handoverId = await PrepareDraftAsync(hr, keptId, "Carrier");
        var removedItemId = await AddItemAsync(hr, handoverId, removedId);

        fixture.Gate.Arm(CommandGate.FirstSerializableCommand());
        var dispatchTask = Phase4TestData.RegisterDispatchAsync(hr, handoverId);
        await fixture.Gate.WaitUntilHeldAsync(dispatchTask);

        Assert.Equal(HttpStatusCode.NoContent, (await RemoveItemAsync(hr, handoverId, removedItemId)).StatusCode);
        fixture.Gate.Release();
        var dispatch = await dispatchTask;

        Assert.Equal(HttpStatusCode.Conflict, dispatch.StatusCode);
        Assert.Equal(0, await CountActiveDispatchesAsync(handoverId));
        Assert.Equal([keptId], await GetItemEquipmentIdsAsync(handoverId));
        Assert.Equal(EquipmentStatus.Available, await GetStatusAsync(keptId));
        Assert.Equal(EquipmentStatus.Available, await GetStatusAsync(removedId));
    }

    [Fact]
    public async Task Dispatch_HeldBeforeItsTransactionReads_WhileAnItemIsAdded_IsAConflict_AndNothingIsShipped()
    {
        using var hr = await fixture.LoginAsync("HR");
        var firstId = await CreateGenericEquipmentAsync(hr);
        var addedId = await CreateGenericEquipmentAsync(hr);
        var handoverId = await PrepareDraftAsync(hr, firstId, "Carrier");

        fixture.Gate.Arm(CommandGate.FirstSerializableCommand());
        var dispatchTask = Phase4TestData.RegisterDispatchAsync(hr, handoverId);
        await fixture.Gate.WaitUntilHeldAsync(dispatchTask);

        Assert.Equal(HttpStatusCode.OK, (await Phase4TestData.AddItemAsync(hr, handoverId, addedId)).StatusCode);
        fixture.Gate.Release();
        var dispatch = await dispatchTask;

        Assert.Equal(HttpStatusCode.Conflict, dispatch.StatusCode);
        Assert.Equal(0, await CountActiveDispatchesAsync(handoverId));
        Assert.Equal(EquipmentStatus.Available, await GetStatusAsync(firstId));
        Assert.Equal(EquipmentStatus.Available, await GetStatusAsync(addedId));
    }

    [Fact]
    public async Task DeliveryMethodChange_HeldAfterItsLockCheck_WhileTheDispatchCommits_IsAConflict_AndStaysCarrier()
    {
        using var hr = await fixture.LoginAsync("HR");
        var equipmentId = await CreateGenericEquipmentAsync(hr);
        var handoverId = await PrepareDraftAsync(hr, equipmentId, "Carrier");

        fixture.Gate.Arm(CommandGate.Sql("UPDATE \"EquipmentHandovers\""));
        var changeTask = Phase4TestData.SetDeliveryMethodAsync(hr, handoverId, "InPerson");
        await fixture.Gate.WaitUntilHeldAsync(changeTask);

        Assert.Equal(HttpStatusCode.OK, (await Phase4TestData.RegisterDispatchAsync(hr, handoverId)).StatusCode);
        fixture.Gate.Release();
        var change = await changeTask;

        Assert.Equal(HttpStatusCode.Conflict, change.StatusCode);
        Assert.Equal(HandoverDeliveryMethod.Carrier, await QueryAsync(db => db.EquipmentHandovers.Where(h => h.Id == handoverId).Select(h => h.DeliveryMethod).SingleAsync()));
        Assert.Equal(1, await CountActiveDispatchesAsync(handoverId));
    }

    [Fact]
    public async Task Confirmation_HeldBeforeItsTransactionReads_WhileAnItemIsRemoved_IsAConflict_AndNoCustodyIsCreated()
    {
        using var hr = await fixture.LoginAsync("HR");
        var keptId = await CreateGenericEquipmentAsync(hr);
        var removedId = await CreateGenericEquipmentAsync(hr);
        var handoverId = await PrepareDraftAsync(hr, keptId, "InPerson");
        var removedItemId = await AddItemAsync(hr, handoverId, removedId);
        (await Phase4TestData.RecordInspectionAsync(hr, handoverId, removedItemId)).EnsureSuccessStatusCode();

        fixture.Gate.Arm(CommandGate.FirstSerializableCommand());
        var confirmTask = Phase4TestData.FinalizeAsync(hr, handoverId);
        await fixture.Gate.WaitUntilHeldAsync(confirmTask);

        Assert.Equal(HttpStatusCode.NoContent, (await RemoveItemAsync(hr, handoverId, removedItemId)).StatusCode);
        fixture.Gate.Release();
        var confirm = await confirmTask;

        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        Assert.Equal(HandoverStatus.Draft, await QueryAsync(db => db.EquipmentHandovers.Where(h => h.Id == handoverId).Select(h => h.Status).SingleAsync()));
        Assert.Equal(0, await QueryAsync(db => db.EquipmentAssignments.CountAsync(a => a.EquipmentId == keptId || a.EquipmentId == removedId)));
        Assert.Equal(EquipmentStatus.Available, await GetStatusAsync(keptId));
        Assert.Equal(EquipmentStatus.Available, await GetStatusAsync(removedId));
    }

    [Fact]
    public async Task DraftEditing_WithoutContention_StillAddsAndRemovesItems()
    {
        using var hr = await fixture.LoginAsync("HR");
        var firstId = await CreateGenericEquipmentAsync(hr);
        var secondId = await CreateGenericEquipmentAsync(hr);
        var handoverId = await PrepareDraftAsync(hr, firstId, "Carrier");

        var secondItemId = await AddItemAsync(hr, handoverId, secondId);
        Assert.Equal([firstId, secondId], await GetItemEquipmentIdsAsync(handoverId));

        Assert.Equal(HttpStatusCode.NoContent, (await RemoveItemAsync(hr, handoverId, secondItemId)).StatusCode);
        Assert.Equal([firstId], await GetItemEquipmentIdsAsync(handoverId));
        Assert.Equal(EquipmentStatus.Available, await GetStatusAsync(secondId));
    }

    private static async Task<Guid> AddItemAsync(HttpClient client, Guid handoverId, Guid equipmentId)
    {
        var response = await Phase4TestData.AddItemAsync(client, handoverId, equipmentId);
        response.EnsureSuccessStatusCode();
        var handover = (await response.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        return handover.Items.Single(i => i.EquipmentId == equipmentId).Id;
    }

    private static async Task<HttpResponseMessage> RemoveItemAsync(HttpClient client, Guid handoverId, Guid itemId)
    {
        var csrf = await client.GetAntiforgeryTokenAsync();
        return await client.DeleteWithCsrfAsync($"/api/equipment-handovers/{handoverId}/items/{itemId}", csrf);
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

    private Task<List<Guid>> GetItemEquipmentIdsAsync(Guid handoverId) =>
        QueryAsync(db => db.EquipmentHandoverItems.Where(i => i.EquipmentHandoverId == handoverId).OrderBy(i => i.CreatedAtUtc).Select(i => i.EquipmentId).ToListAsync());

    private Task<EquipmentStatus> GetStatusAsync(Guid equipmentId) =>
        QueryAsync(db => db.Equipment.Where(e => e.Id == equipmentId).Select(e => e.Status).SingleAsync());

    private Task<int> CountActiveDispatchesAsync(Guid handoverId) =>
        QueryAsync(db => db.EquipmentDispatches.CountAsync(d => d.EquipmentHandoverId == handoverId && d.CancelledAtUtc == null));

    private async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = fixture.Users.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
}
