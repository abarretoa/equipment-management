using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Allocations;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.Returns;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.Returns;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Returns;

/// <summary>
/// Iteration 4 extension: returns started together — by ending an
/// engagement, or manually from the Returns area for one or more items — share
/// one <c>EquipmentReturnGroup</c> so they can be shown as one collection.
/// Underneath, every item still has its own independent
/// <c>EquipmentReturn</c>; custody (the Active assignment) ends only at
/// physical receipt, per item; and a manual return never touches the
/// Consultant's allocation.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class EquipmentReturnGroupTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    // ---------- Offboarding group ----------

    [Fact]
    public async Task EndOfEngagement_WithThreeItems_CreatesOneGroupHoldingThreeIndependentReturns()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 3);

        var ended = await EndEngagementAsync(client, setup, from: "2026-10-03", to: "2026-10-05");

        Assert.Equal(3, ended.ReturnsCreatedCount);
        Assert.NotNull(ended.ReturnGroupId);

        var detail = await GetGroupAsync(client, ended.ReturnGroupId!.Value);
        var group = detail.Group;
        Assert.True(group.IsEndOfEngagement);
        Assert.Equal(setup.ConsultantId, group.ConsultantId);
        Assert.Equal(setup.ConsultantName, group.ConsultantFullName);
        Assert.Equal(ReturnGroupCollectionStatus.Awaiting, group.CollectionStatus);
        Assert.Equal(0, group.ReceivedCount);
        // The group stores no date: it shows the allocation's collection window.
        Assert.Equal(new DateOnly(2026, 10, 3), group.ExpectedFromDate);
        Assert.Equal(new DateOnly(2026, 10, 5), group.ExpectedToDate);
        Assert.Equal(setup.Items.Select(i => i.AssetTag).Order(StringComparer.Ordinal), group.Items.Select(i => i.AssetTag));
        Assert.Empty(detail.OtherPendingReturns);

        // Three independent returns, each a member of that one group.
        var returns = await GetFlatReturnsAsync(client, setup.ConsultantId);
        Assert.Equal(3, returns.Count);
        Assert.Equal(group.Items.Select(i => i.ReturnId).Order(), returns.Select(r => r.Id).Order());
        foreach (var item in group.Items)
        {
            Assert.Equal(ReturnStatus.Pending, item.Status);
            Assert.Equal(EquipmentStatus.ReturnPending, item.EquipmentStatus);
            var single = await GetReturnAsync(client, item.ReturnId);
            Assert.Equal(group.Id, single.ReturnGroupId);
            Assert.Equal(3, single.ReturnGroupSize);
        }

        // Custody is untouched.
        var assignments = await GetActiveAssignmentsAsync(client, setup.ConsultantId);
        Assert.Equal(3, assignments.Count);

        // The operational list shows ONE row for the collection; the flat list still counts three returns.
        var overview = await GetOverviewAsync(client, setup.ConsultantName);
        var row = Assert.Single(overview.Items);
        Assert.Null(row.Return);
        Assert.Equal(group.Id, row.Group!.Id);
        Assert.Equal(3, row.Group.Items.Count);
        Assert.Equal(1, overview.TotalCount);
    }

    [Fact]
    public async Task EndOfEngagement_WithAReturnAlreadyPending_LeavesItInItsOwnGroup_AndShowsItAsOtherPending()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 3);
        var earlier = await StartManualAsync(client, setup.ConsultantId, [setup.Items[0].AssignmentId], "2026-12-24", "Troca de equipamento");
        var earlierReturnId = Assert.Single(earlier.ReturnIds);
        var before = await GetReturnAsync(client, earlierReturnId);

        var ended = await EndEngagementAsync(client, setup);

        Assert.Equal(2, ended.ReturnsCreatedCount);
        Assert.Equal(1, ended.ReturnsAlreadyPendingCount);
        Assert.NotEqual(earlier.ReturnGroupId, ended.ReturnGroupId);

        // The pre-existing return is not duplicated, not moved and not rewritten.
        var after = await GetReturnAsync(client, earlierReturnId);
        Assert.Equal(earlier.ReturnGroupId, after.ReturnGroupId);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
        Assert.Equal(new DateOnly(2026, 12, 24), after.ExpectedReturnDate);
        Assert.Equal("Troca de equipamento", after.Reason);
        Assert.Equal(3, (await GetFlatReturnsAsync(client, setup.ConsultantId)).Count);

        // The offboarding collection holds only what it created, and points at the other outstanding return.
        var detail = await GetGroupAsync(client, ended.ReturnGroupId!.Value);
        Assert.Equal(
            new[] { setup.Items[1].AssetTag, setup.Items[2].AssetTag }.Order(StringComparer.Ordinal),
            detail.Group.Items.Select(i => i.AssetTag));
        var other = Assert.Single(detail.OtherPendingReturns);
        Assert.Equal(earlierReturnId, other.Id);
        Assert.Equal(setup.Items[0].AssetTag, other.AssetTag);

        // Every outstanding item is represented exactly once across the list rows.
        var overview = await GetOverviewAsync(client, setup.ConsultantName);
        Assert.Equal(2, overview.Items.Count);
        Assert.Equal(
            setup.Items.Select(i => i.AssetTag).Order(StringComparer.Ordinal),
            overview.Items.SelectMany(r => r.Group!.Items).Select(i => i.AssetTag).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task EndOfEngagement_WhenEveryItemIsAlreadyPending_CreatesNoEmptyGroup()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 2);
        var earlier = await StartManualAsync(client, setup.ConsultantId, setup.Items.Select(i => i.AssignmentId).ToList());

        var ended = await EndEngagementAsync(client, setup);

        Assert.Equal(0, ended.ReturnsCreatedCount);
        Assert.Equal(2, ended.ReturnsAlreadyPendingCount);
        Assert.Null(ended.ReturnGroupId);
        Assert.Equal(earlier.ReturnGroupId, Assert.Single((await GetOverviewAsync(client, setup.ConsultantName)).Items).Group!.Id);
        Assert.Equal(1, await CountGroupsAsync(setup.ConsultantId));
    }

    [Fact]
    public async Task ReceivingItems_MovesTheDerivedGroupStatus_FromAwaiting_ToPartial_ToCollected_WithoutTouchingOtherItems()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 3);
        var ended = await EndEngagementAsync(client, setup);
        var groupId = ended.ReturnGroupId!.Value;
        var receiverId = await CreateReceiverAsync();
        var items = (await GetGroupAsync(client, groupId)).Group.Items;

        (await Phase5TestData.ReceiveReturnAsync(client, items[0].ReturnId, Today, receiverId)).EnsureSuccessStatusCode();

        var partial = (await GetGroupAsync(client, groupId)).Group;
        Assert.Equal(ReturnGroupCollectionStatus.Partial, partial.CollectionStatus);
        Assert.Equal(1, partial.ReceivedCount);
        Assert.Equal(ReturnStatus.Received, partial.Items[0].Status);
        Assert.Equal(EquipmentStatus.UnderInspection, partial.Items[0].EquipmentStatus);
        Assert.All(partial.Items.Skip(1), i =>
        {
            Assert.Equal(ReturnStatus.Pending, i.Status);
            Assert.Equal(EquipmentStatus.ReturnPending, i.EquipmentStatus);
            Assert.Null(i.ReceivedOn);
        });
        // Only the received item's custody ended.
        Assert.Equal(
            partial.Items.Skip(1).Select(i => i.EquipmentId).Order(),
            (await GetActiveAssignmentsAsync(client, setup.ConsultantId)).Select(a => a.EquipmentId).Order());

        // Finishing the first item's inspection (equipment Available again) does not change what was collected.
        (await Phase5TestData.RecordReturnInspectionAsync(client, items[0].ReturnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, items[0].ReturnId)).EnsureSuccessStatusCode();
        var afterInspection = (await GetGroupAsync(client, groupId)).Group;
        Assert.Equal(ReturnGroupCollectionStatus.Partial, afterInspection.CollectionStatus);
        Assert.Equal(EquipmentStatus.Available, afterInspection.Items[0].EquipmentStatus);

        (await Phase5TestData.ReceiveReturnAsync(client, items[1].ReturnId, Today, receiverId)).EnsureSuccessStatusCode();
        (await Phase5TestData.ReceiveReturnAsync(client, items[2].ReturnId, Today, receiverId)).EnsureSuccessStatusCode();

        var collected = (await GetGroupAsync(client, groupId)).Group;
        Assert.Equal(ReturnGroupCollectionStatus.Collected, collected.CollectionStatus);
        Assert.Equal(3, collected.ReceivedCount);
        Assert.Empty(await GetActiveAssignmentsAsync(client, setup.ConsultantId));
    }

    // The status filter places every list row under exactly one status. A
    // group is placed by its derived collection state — never by "any of its
    // returns matches" — so it cannot show under two filters at once.

    [Fact]
    public async Task Overview_StatusFilter_GroupWithNothingReceived_IsPendingOnly()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 3);
        var ended = await EndEngagementAsync(client, setup);

        var row = await AssertListedOnlyUnderAsync(client, setup.ConsultantName, "Pending");

        Assert.Equal(ended.ReturnGroupId, row.Group!.Id);
        Assert.Equal(3, row.Group.Items.Count);
        Assert.Equal(ReturnGroupCollectionStatus.Awaiting, row.Group.CollectionStatus);
    }

    [Fact]
    public async Task Overview_StatusFilter_PartlyReceivedGroup_IsPendingOnly_NeverAlsoReceived()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 3);
        var ended = await EndEngagementAsync(client, setup);
        var items = (await GetGroupAsync(client, ended.ReturnGroupId!.Value)).Group.Items;
        (await Phase5TestData.ReceiveReturnAsync(client, items[0].ReturnId, Today, await CreateReceiverAsync())).EnsureSuccessStatusCode();

        var row = await AssertListedOnlyUnderAsync(client, setup.ConsultantName, "Pending");

        // Shown whole, with its real derived state.
        Assert.Equal(3, row.Group!.Items.Count);
        Assert.Equal(1, row.Group.ReceivedCount);
        Assert.Equal(ReturnGroupCollectionStatus.Partial, row.Group.CollectionStatus);

        // Still findable by any of its asset tags, received or not.
        Assert.Contains((await GetOverviewAsync(client, items[0].AssetTag, "Pending")).Items, r => r.Group?.Id == ended.ReturnGroupId);
        Assert.Contains((await GetOverviewAsync(client, items[2].AssetTag, "Pending")).Items, r => r.Group?.Id == ended.ReturnGroupId);

        // The flat per-return list, which counts are based on, is unchanged: one return received, two pending.
        Assert.Equal(1, (await GetFlatReturnsAsync(client, setup.ConsultantId)).Count(r => r.Status == ReturnStatus.Received));
        Assert.Equal(2, (await GetFlatReturnsAsync(client, setup.ConsultantId)).Count(r => r.Status == ReturnStatus.Pending));
        var flatReceived = await client.GetFromJsonAsync<PagedResponse<EquipmentReturnSummaryResponse>>(
            $"/api/equipment-returns?consultantId={setup.ConsultantId}&status=Received", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(1, flatReceived!.TotalCount);
    }

    [Fact]
    public async Task Overview_StatusFilter_FullyReceivedGroup_IsReceivedOnly_ThenCompletedOnlyOnceEveryInspectionIsFinalized()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 2);
        var ended = await EndEngagementAsync(client, setup);
        var items = (await GetGroupAsync(client, ended.ReturnGroupId!.Value)).Group.Items;
        var receiverId = await CreateReceiverAsync();
        foreach (var item in items)
        {
            (await Phase5TestData.ReceiveReturnAsync(client, item.ReturnId, Today, receiverId)).EnsureSuccessStatusCode();
        }

        var received = await AssertListedOnlyUnderAsync(client, setup.ConsultantName, "Received");
        Assert.Equal(ReturnGroupCollectionStatus.Collected, received.Group!.CollectionStatus);

        // One inspection finalized, one still open: everything is collected, but the row is not finished.
        (await Phase5TestData.RecordReturnInspectionAsync(client, items[0].ReturnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, items[0].ReturnId)).EnsureSuccessStatusCode();
        await AssertListedOnlyUnderAsync(client, setup.ConsultantName, "Received");

        (await Phase5TestData.RecordReturnInspectionAsync(client, items[1].ReturnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, items[1].ReturnId)).EnsureSuccessStatusCode();

        var completed = await AssertListedOnlyUnderAsync(client, setup.ConsultantName, "Completed");
        Assert.Equal(ReturnGroupCollectionStatus.Collected, completed.Group!.CollectionStatus);
    }

    [Fact]
    public async Task Overview_StatusFilter_GroupWithEveryReturnCancelled_IsCancelledOnly_AndACancelledItemDoesNotHoldTheGroupBack()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 2);
        var started = await StartManualAsync(client, setup.ConsultantId, setup.Items.Select(i => i.AssignmentId).ToList());

        // One cancelled, one received: nothing is awaiting receipt any more.
        (await Phase5TestData.CancelReturnAsync(client, started.ReturnIds[0])).EnsureSuccessStatusCode();
        (await Phase5TestData.ReceiveReturnAsync(client, started.ReturnIds[1], Today, await CreateReceiverAsync())).EnsureSuccessStatusCode();
        var row = await AssertListedOnlyUnderAsync(client, setup.ConsultantName, "Received");
        Assert.Equal(ReturnGroupCollectionStatus.Collected, row.Group!.CollectionStatus);

        var other = await SetUpAsync(client, 1);
        var otherStarted = await StartManualAsync(client, other.ConsultantId, [other.Items[0].AssignmentId]);
        (await Phase5TestData.CancelReturnAsync(client, otherStarted.ReturnIds[0])).EnsureSuccessStatusCode();
        await AssertListedOnlyUnderAsync(client, other.ConsultantName, "Cancelled");
    }

    [Fact]
    public async Task Overview_StatusFilter_StandaloneHistoricalReturn_IsFilteredByItsOwnStatus()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 1);
        var created = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, setup.Items[0].AssignmentId);
        await ClearReturnGroupAsync(created.Id);

        var pending = await AssertListedOnlyUnderAsync(client, setup.ConsultantName, "Pending");
        Assert.Null(pending.Group);
        Assert.Equal(created.Id, pending.Return!.Id);

        (await Phase5TestData.ReceiveReturnAsync(client, created.Id, Today, await CreateReceiverAsync())).EnsureSuccessStatusCode();
        var received = await AssertListedOnlyUnderAsync(client, setup.ConsultantName, "Received");
        Assert.Equal(ReturnStatus.Received, received.Return!.Status);

        (await Phase5TestData.RecordReturnInspectionAsync(client, created.Id)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, created.Id)).EnsureSuccessStatusCode();
        var completed = await AssertListedOnlyUnderAsync(client, setup.ConsultantName, "Completed");
        Assert.Equal(ReturnStatus.Completed, completed.Return!.Status);
    }

    // ---------- Manual return from the Returns area ----------

    [Fact]
    public async Task ManualReturn_OfOneOfThreeItems_ChangesOnlyThatItem_AndNeverTheAllocation()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 3);
        GetEmailSender().Reset();

        var started = await StartManualAsync(client, setup.ConsultantId, [setup.Items[0].AssignmentId], "2026-10-05", "Substituição do portátil");

        Assert.Equal(1, started.ReturnsCreatedCount);
        var created = await GetReturnAsync(client, Assert.Single(started.ReturnIds));
        Assert.Equal(ReturnStatus.Pending, created.Status);
        Assert.Equal(setup.Items[0].EquipmentId, created.EquipmentId);
        Assert.Equal(new DateOnly(2026, 10, 5), created.ExpectedReturnDate);
        Assert.Equal("Substituição do portátil", created.Reason);
        Assert.Equal(started.ReturnGroupId, created.ReturnGroupId);
        Assert.Equal(1, created.ReturnGroupSize);

        Assert.Equal(AllocationStatus.Active, (await GetAllocationAsync(client, setup)).Status);
        Assert.Equal(EquipmentStatus.ReturnPending, (await GetEquipmentAsync(client, setup.Items[0].EquipmentId)).Status);
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, setup.Items[1].EquipmentId)).Status);
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, setup.Items[2].EquipmentId)).Status);
        Assert.Equal(3, (await GetActiveAssignmentsAsync(client, setup.ConsultantId)).Count);
        Assert.Single(await GetFlatReturnsAsync(client, setup.ConsultantId));

        var group = (await GetGroupAsync(client, started.ReturnGroupId)).Group;
        Assert.False(group.IsEndOfEngagement);
        Assert.Equal(new DateOnly(2026, 10, 5), group.ExpectedFromDate);
        Assert.Null(group.ExpectedToDate);

        // Same as a return started from an equipment's page: no email.
        Assert.Empty(GetEmailSender().SentMessages);
    }

    [Fact]
    public async Task ManualReturn_OfThreeItems_CreatesThreeIndependentReturnsInOneGroup()
    {
        using var client = await LoginAsync("HR");
        using var admin = await LoginAsync("Administration");
        var setup = await SetUpAsync(admin, 3);
        GetEmailSender().Reset();

        var started = await StartManualAsync(client, setup.ConsultantId, setup.Items.Select(i => i.AssignmentId).ToList(), "2026-10-05");

        Assert.Equal(3, started.ReturnsCreatedCount);
        Assert.Equal(3, started.ReturnIds.Distinct().Count());

        var group = (await GetGroupAsync(admin, started.ReturnGroupId)).Group;
        Assert.False(group.IsEndOfEngagement);
        Assert.Equal(ReturnGroupCollectionStatus.Awaiting, group.CollectionStatus);
        Assert.Equal(started.ReturnIds.Order(), group.Items.Select(i => i.ReturnId).Order());
        Assert.All(group.Items, i => Assert.Equal(EquipmentStatus.ReturnPending, i.EquipmentStatus));

        Assert.Equal(AllocationStatus.Active, (await GetAllocationAsync(admin, setup)).Status);
        Assert.Equal(3, (await GetActiveAssignmentsAsync(admin, setup.ConsultantId)).Count);
        Assert.Equal(3, (await GetFlatReturnsAsync(admin, setup.ConsultantId)).Count);
        Assert.Single((await GetOverviewAsync(admin, setup.ConsultantName)).Items);
        Assert.Empty(GetEmailSender().SentMessages);

        // Each child is still received on its own.
        (await Phase5TestData.ReceiveReturnAsync(admin, group.Items[0].ReturnId, Today, await CreateReceiverAsync())).EnsureSuccessStatusCode();
        var afterReceive = (await GetGroupAsync(admin, started.ReturnGroupId)).Group;
        Assert.Equal(ReturnGroupCollectionStatus.Partial, afterReceive.CollectionStatus);
        Assert.Equal(2, (await GetActiveAssignmentsAsync(admin, setup.ConsultantId)).Count);
    }

    [Fact]
    public async Task ManualReturn_IncludingAnItemAlreadyInReturn_IsRejectedEntirely()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 3);
        await StartManualAsync(client, setup.ConsultantId, [setup.Items[0].AssignmentId]);

        // The picker's source marks that item as already in return.
        var assignments = await GetActiveAssignmentsAsync(client, setup.ConsultantId);
        Assert.Equal(EquipmentStatus.ReturnPending, assignments.Single(a => a.Id == setup.Items[0].AssignmentId).EquipmentStatus);
        Assert.All(assignments.Where(a => a.Id != setup.Items[0].AssignmentId), a => Assert.Equal(EquipmentStatus.Assigned, a.EquipmentStatus));

        var response = await PostManualAsync(client, setup.ConsultantId, setup.Items.Select(i => i.AssignmentId).ToList());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var title = await ReadProblemTitleAsync(response);
        Assert.Contains(setup.Items[0].AssetTag, title);
        Assert.Contains("Nenhuma devolução foi iniciada", title);

        Assert.Single(await GetFlatReturnsAsync(client, setup.ConsultantId));
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, setup.Items[1].EquipmentId)).Status);
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, setup.Items[2].EquipmentId)).Status);
        Assert.Equal(1, await CountGroupsAsync(setup.ConsultantId));
    }

    [Fact]
    public async Task ManualReturn_WithOneItemInAnInvalidState_RollsBackEverything()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 3);
        await ForceEquipmentStatusAsync(setup.Items[2].EquipmentId, EquipmentStatus.UnderMaintenance);

        var response = await PostManualAsync(client, setup.ConsultantId, setup.Items.Select(i => i.AssignmentId).ToList());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(setup.Items[2].AssetTag, await ReadProblemTitleAsync(response));
        Assert.Empty(await GetFlatReturnsAsync(client, setup.ConsultantId));
        Assert.Equal(0, await CountGroupsAsync(setup.ConsultantId));
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, setup.Items[0].EquipmentId)).Status);
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, setup.Items[1].EquipmentId)).Status);
        Assert.Equal(EquipmentStatus.UnderMaintenance, (await GetEquipmentAsync(client, setup.Items[2].EquipmentId)).Status);
        Assert.Equal(3, (await GetActiveAssignmentsAsync(client, setup.ConsultantId)).Count);
    }

    [Fact]
    public async Task ManualReturn_IsBasedOnCustody_SoAConsultantWithAnEndedAllocationIsStillEligible()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 2);
        // An allocation change, not an offboarding: the allocation ends, the equipment stays with the consultant.
        (await Phase4TestData.EndAllocationAsync(client, setup.ConsultantId, setup.AllocationId, new DateOnly(2026, 10, 15))).EnsureSuccessStatusCode();
        Assert.Equal(AllocationStatus.Ended, (await GetAllocationAsync(client, setup)).Status);

        // Listed among the people a return can be started for...
        var holders = await client.GetFromJsonAsync<PagedResponse<ConsultantSummaryResponse>>(
            $"/api/consultants?hasActiveCustody=true&search={Uri.EscapeDataString(setup.ConsultantName)}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(setup.ConsultantId, Assert.Single(holders!.Items).Id);
        // ...while no longer eligible for a new handover.
        var handoverEligible = await client.GetFromJsonAsync<PagedResponse<ConsultantSummaryResponse>>(
            $"/api/consultants?hasActiveAllocation=true&search={Uri.EscapeDataString(setup.ConsultantName)}", HttpClientTestExtensions.JsonOptions);
        Assert.Empty(handoverEligible!.Items);

        var started = await StartManualAsync(client, setup.ConsultantId, setup.Items.Select(i => i.AssignmentId).ToList());

        Assert.Equal(2, started.ReturnsCreatedCount);
        Assert.Equal(AllocationStatus.Ended, (await GetAllocationAsync(client, setup)).Status);
        Assert.Equal(2, (await GetActiveAssignmentsAsync(client, setup.ConsultantId)).Count);
    }

    [Fact]
    public async Task HasActiveCustodyFilter_ExcludesConsultantsHoldingNothing()
    {
        using var client = await LoginAsync("Administration");
        var withNothing = await SetUpAsync(client, 0);

        var holders = await client.GetFromJsonAsync<PagedResponse<ConsultantSummaryResponse>>(
            $"/api/consultants?hasActiveCustody=true&search={Uri.EscapeDataString(withNothing.ConsultantName)}", HttpClientTestExtensions.JsonOptions);
        Assert.Empty(holders!.Items);

        var nonHolders = await client.GetFromJsonAsync<PagedResponse<ConsultantSummaryResponse>>(
            $"/api/consultants?hasActiveCustody=false&search={Uri.EscapeDataString(withNothing.ConsultantName)}", HttpClientTestExtensions.JsonOptions);
        Assert.Single(nonHolders!.Items);
    }

    [Fact]
    public async Task ManualReturn_RejectsItemsOfAnotherConsultant_UnknownItems_AndAnEmptySelection()
    {
        using var client = await LoginAsync("Administration");
        var first = await SetUpAsync(client, 1);
        var second = await SetUpAsync(client, 1);

        var mixed = await PostManualAsync(client, first.ConsultantId, [first.Items[0].AssignmentId, second.Items[0].AssignmentId]);
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);

        var unknown = await PostManualAsync(client, first.ConsultantId, [first.Items[0].AssignmentId, Guid.NewGuid()]);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var empty = await PostManualAsync(client, first.ConsultantId, []);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        Assert.Empty(await GetFlatReturnsAsync(client, first.ConsultantId));
        Assert.Empty(await GetFlatReturnsAsync(client, second.ConsultantId));
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, first.Items[0].EquipmentId)).Status);
    }

    [Fact]
    public async Task ManualReturn_ConcurrentForTheSameItems_OnlyOneSucceeds_AndNoDuplicateReturnExists()
    {
        using var clientA = await LoginAsync("Administration");
        using var clientB = await LoginAsync("HR");
        var setup = await SetUpAsync(clientA, 2);
        var ids = setup.Items.Select(i => i.AssignmentId).ToList();

        var results = await Task.WhenAll(
            PostManualAsync(clientA, setup.ConsultantId, ids),
            PostManualAsync(clientB, setup.ConsultantId, ids));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var returns = await GetFlatReturnsAsync(clientA, setup.ConsultantId);
        Assert.Equal(2, returns.Count);
        Assert.Equal(2, returns.Select(r => r.EquipmentAssignmentId).Distinct().Count());
        Assert.Equal(1, await CountGroupsAsync(setup.ConsultantId));
    }

    [Fact]
    public async Task ManualReturn_Audit_RecordsTheGroupAndEachReturn_WithoutPii()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 2);

        var started = await StartManualAsync(
            client, setup.ConsultantId, setup.Items.Select(i => i.AssignmentId).ToList(), reason: "Motivo interno sensível");

        var groupEntry = Assert.Single(await GetAuditAsync(client, started.ReturnGroupId));
        Assert.Equal(AuditActions.EquipmentReturnGroup.Created, groupEntry.Action);
        Assert.NotNull(groupEntry.ActorUserId);
        var values = JsonDocument.Parse(groupEntry.NewValuesJson!).RootElement;
        Assert.Equal(setup.ConsultantId, values.GetProperty("consultantId").GetGuid());
        Assert.Equal(started.ReturnIds.Order(), values.GetProperty("returnIds").EnumerateArray().Select(e => e.GetGuid()).Order());
        Assert.Equal("Manual", JsonDocument.Parse(groupEntry.MetadataJson!).RootElement.GetProperty("trigger").GetString());

        foreach (var returnId in started.ReturnIds)
        {
            var entry = Assert.Single(await GetAuditAsync(client, returnId));
            Assert.Equal(AuditActions.EquipmentReturn.Created, entry.Action);
            var metadata = JsonDocument.Parse(entry.MetadataJson!).RootElement;
            Assert.Equal("Manual", metadata.GetProperty("trigger").GetString());
            Assert.Equal(started.ReturnGroupId, metadata.GetProperty("returnGroupId").GetGuid());
            Assert.DoesNotContain("Motivo interno sensível", $"{entry.NewValuesJson}{entry.MetadataJson}");
            Assert.DoesNotContain(setup.ConsultantName, $"{entry.NewValuesJson}{entry.MetadataJson}");
        }

        Assert.DoesNotContain(setup.ConsultantName, $"{groupEntry.NewValuesJson}{groupEntry.MetadataJson}");
    }

    // ---------- Existing single-equipment entry point and historical data ----------

    [Fact]
    public async Task ReturnStartedFromAnEquipmentsPage_StillWorks_AndIsAOneItemGroup()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 2);
        GetEmailSender().Reset();

        var response = await Phase5TestData.InitiateReturnAsync(client, setup.Items[0].AssignmentId, new DateOnly(2026, 10, 9), "Avaria");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(ReturnStatus.Pending, created.Status);
        Assert.Equal(setup.Items[0].AssetTag, created.AssetTag);
        Assert.Equal(new DateOnly(2026, 10, 9), created.ExpectedReturnDate);
        Assert.Equal("Avaria", created.Reason);
        Assert.NotNull(created.ReturnGroupId);
        Assert.Equal(1, created.ReturnGroupSize);

        var row = Assert.Single((await GetOverviewAsync(client, setup.ConsultantName)).Items);
        Assert.Equal(created.Id, Assert.Single(row.Group!.Items).ReturnId);
        Assert.False(row.Group.IsEndOfEngagement);

        Assert.Equal(AllocationStatus.Active, (await GetAllocationAsync(client, setup)).Status);
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, setup.Items[1].EquipmentId)).Status);
        Assert.Empty(GetEmailSender().SentMessages);
    }

    [Fact]
    public async Task HistoricalReturnWithNoGroup_StaysVisibleAndFullyWorkable()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 1);
        var created = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, setup.Items[0].AssignmentId);
        // A return as it existed before grouping: no group at all.
        await ClearReturnGroupAsync(created.Id);

        var row = Assert.Single((await GetOverviewAsync(client, setup.ConsultantName)).Items);
        Assert.Null(row.Group);
        Assert.Equal(created.Id, row.Return!.Id);
        Assert.Equal(setup.Items[0].AssetTag, row.Return.AssetTag);

        var detail = await GetReturnAsync(client, created.Id);
        Assert.Null(detail.ReturnGroupId);
        Assert.Equal(0, detail.ReturnGroupSize);

        (await Phase5TestData.ReceiveReturnAsync(client, created.Id, Today, await CreateReceiverAsync())).EnsureSuccessStatusCode();
        Assert.Equal(ReturnStatus.Received, (await GetReturnAsync(client, created.Id)).Status);
        Assert.Equal(ReturnStatus.Received, Assert.Single((await GetOverviewAsync(client, setup.ConsultantName)).Items).Return!.Status);
    }

    [Fact]
    public async Task GetGroup_Unknown_IsNotFound_AndStartingAGroupRequiresCsrf()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpAsync(client, 1);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/equipment-returns/groups/{Guid.NewGuid()}")).StatusCode);

        var withoutCsrf = await client.PostAsJsonAsync(
            "/api/equipment-returns/groups", new { ConsultantId = setup.ConsultantId, EquipmentAssignmentIds = new[] { setup.Items[0].AssignmentId } });
        Assert.NotEqual(HttpStatusCode.Created, withoutCsrf.StatusCode);
        Assert.Empty(await GetFlatReturnsAsync(client, setup.ConsultantId));
    }

    // ---------- helpers ----------

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Setup(
        Guid ConsultantId, string ConsultantName, Guid AllocationId, IReadOnlyList<(Guid EquipmentId, string AssetTag, Guid AssignmentId)> Items);

    /// <summary>A uniquely named Consultant with one Active allocation and <paramref name="equipmentCount"/> Generic items in active custody.</summary>
    private static async Task<Setup> SetUpAsync(HttpClient client, int equipmentCount)
    {
        var name = $"Grupo {Guid.NewGuid():N}";
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client, name);
        var allocation = await Phase4TestData.CreateAllocationAsync(client, consultantId, new DateOnly(2026, 1, 1));

        var items = new List<(Guid, string, Guid)>();
        if (equipmentCount > 0)
        {
            var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
            for (var i = 0; i < equipmentCount; i++)
            {
                var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);
                var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
                (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();
                items.Add((equipment.Id, equipment.AssetTag, await Phase5TestData.GetActiveAssignmentIdAsync(client, equipment.Id)));
            }
        }

        return new Setup(consultantId, name, allocation.Id, items);
    }

    private static async Task<EndConsultantAllocationResponse> EndEngagementAsync(
        HttpClient client, Setup setup, string from = "2026-11-01", string? to = null)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync(
            $"/api/consultants/{setup.ConsultantId}/allocations/{setup.AllocationId}/end",
            new { EndDate = "2026-10-15", IsEndOfEngagement = true, ExpectedCollectionFromDate = from, ExpectedCollectionToDate = to },
            csrfToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EndConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task<HttpResponseMessage> PostManualAsync(
        HttpClient client, Guid consultantId, IReadOnlyList<Guid> assignmentIds, string? expectedReturnDate = null, string? reason = null)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync(
            "/api/equipment-returns/groups",
            new { ConsultantId = consultantId, EquipmentAssignmentIds = assignmentIds, ExpectedReturnDate = expectedReturnDate, Reason = reason },
            csrfToken);
    }

    private static async Task<StartReturnGroupResponse> StartManualAsync(
        HttpClient client, Guid consultantId, IReadOnlyList<Guid> assignmentIds, string? expectedReturnDate = null, string? reason = null)
    {
        var response = await PostManualAsync(client, consultantId, assignmentIds, expectedReturnDate, reason);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StartReturnGroupResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task<string> ReadProblemTitleAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString()!;

    private static async Task<EquipmentReturnGroupDetailResponse> GetGroupAsync(HttpClient client, Guid groupId) =>
        (await client.GetFromJsonAsync<EquipmentReturnGroupDetailResponse>(
            $"/api/equipment-returns/groups/{groupId}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<PagedResponse<EquipmentReturnListRowResponse>> GetOverviewAsync(HttpClient client, string search, string? status = null) =>
        (await client.GetFromJsonAsync<PagedResponse<EquipmentReturnListRowResponse>>(
            $"/api/equipment-returns/overview?search={Uri.EscapeDataString(search)}&pageSize=100{(status is null ? "" : $"&status={status}")}",
            HttpClientTestExtensions.JsonOptions))!;

    /// <summary>The consultant's one list row appears under <paramref name="expectedStatus"/>, under no other status, and in the unfiltered list.</summary>
    private static async Task<EquipmentReturnListRowResponse> AssertListedOnlyUnderAsync(HttpClient client, string consultantName, string expectedStatus)
    {
        EquipmentReturnListRowResponse? found = null;
        foreach (var status in new[] { "Pending", "Received", "Completed", "Cancelled" })
        {
            var page = await GetOverviewAsync(client, consultantName, status);
            if (status == expectedStatus)
            {
                found = Assert.Single(page.Items);
                Assert.Equal(1, page.TotalCount);
            }
            else
            {
                Assert.Empty(page.Items);
                Assert.Equal(0, page.TotalCount);
            }
        }

        Assert.Single((await GetOverviewAsync(client, consultantName)).Items);
        return found!;
    }

    private static async Task<IReadOnlyList<EquipmentReturnSummaryResponse>> GetFlatReturnsAsync(HttpClient client, Guid consultantId) =>
        (await client.GetFromJsonAsync<PagedResponse<EquipmentReturnSummaryResponse>>(
            $"/api/equipment-returns?consultantId={consultantId}&pageSize=100", HttpClientTestExtensions.JsonOptions))!.Items;

    private static async Task<EquipmentReturnDetailResponse> GetReturnAsync(HttpClient client, Guid returnId) =>
        (await client.GetFromJsonAsync<EquipmentReturnDetailResponse>($"/api/equipment-returns/{returnId}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<IReadOnlyList<EquipmentAssignmentResponse>> GetActiveAssignmentsAsync(HttpClient client, Guid consultantId) =>
        (await client.GetFromJsonAsync<PagedResponse<EquipmentAssignmentResponse>>(
            $"/api/equipment-assignments?consultantId={consultantId}&status=Active&pageSize=100", HttpClientTestExtensions.JsonOptions))!.Items;

    private static async Task<EquipmentDetailResponse> GetEquipmentAsync(HttpClient client, Guid equipmentId) =>
        (await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipmentId}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<ConsultantAllocationResponse> GetAllocationAsync(HttpClient client, Setup setup) =>
        (await client.GetFromJsonAsync<ConsultantAllocationResponse>(
            $"/api/consultants/{setup.ConsultantId}/allocations/{setup.AllocationId}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<IReadOnlyList<AuditLogResponse>> GetAuditAsync(HttpClient client, Guid entityId) =>
        (await client.GetFromJsonAsync<PagedResponse<AuditLogResponse>>(
            $"/api/audit?entityId={entityId}&page=1&pageSize=100", HttpClientTestExtensions.JsonOptions))!.Items;

    private async Task<Guid> CreateReceiverAsync()
    {
        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: $"Rua Receiver {Guid.NewGuid():N}, Lisboa");
        return await ApiTestUsers.GetIdAsync(factory, receiverEmail);
    }

    private async Task<int> CountGroupsAsync(Guid consultantId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.EquipmentReturnGroups.CountAsync(g => g.ConsultantId == consultantId);
    }

    /// <summary>Test-only: writes an equipment status directly to simulate inconsistent data no normal flow can produce.</summary>
    private async Task ForceEquipmentStatusAsync(Guid equipmentId, EquipmentStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var equipment = await dbContext.Equipment.FirstAsync(e => e.Id == equipmentId);
        equipment.Status = status;
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Test-only: removes a return's group to reproduce a row created before grouping existed.</summary>
    private async Task ClearReturnGroupAsync(Guid returnId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var equipmentReturn = await dbContext.EquipmentReturns.FirstAsync(r => r.Id == returnId);
        equipmentReturn.ReturnGroupId = null;
        await dbContext.SaveChangesAsync();
    }

    private RecordingEmailSender GetEmailSender() => factory.Services.GetRequiredService<RecordingEmailSender>();

    private async Task<HttpClient> LoginAsync(string role)
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, role, address: $"Rua Teste {Guid.NewGuid():N}, Lisboa");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
