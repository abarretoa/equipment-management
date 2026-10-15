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
using EquipmentManagement.Infrastructure.Allocations;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Allocations;

/// <summary>
/// Iteration 4: ending an allocation as end-of-engagement
/// initiates one independent <c>EquipmentReturn</c> per piece of equipment in
/// the Consultant's active custody, atomically with the ending, and sends one
/// consolidated collection email. Ending an allocation that is not an end of
/// engagement never touches equipment. Custody (the Active
/// <c>EquipmentAssignment</c>) is never ended here, only at physical receipt.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class EndOfEngagementReturnTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateOnly CollectionFrom = new(2026, 11, 1);

    [Fact]
    public async Task NotEndOfEngagement_WithThreeItems_EndsAllocation_CreatesNoReturn_LeavesEquipmentAssigned_NoEmail()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 3);
        GetEmailSender().Reset();

        var response = await EndAsync(client, setup, isEndOfEngagement: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ended = await ReadEndAsync(response);
        Assert.Equal(AllocationStatus.Ended, ended.Status);
        Assert.False(ended.IsEndOfEngagement);
        Assert.Equal(0, ended.ReturnsCreatedCount);
        Assert.Equal(0, ended.ReturnsAlreadyPendingCount);

        Assert.Empty(await GetReturnsAsync(client, setup.ConsultantId));
        var assignments = await GetActiveAssignmentsAsync(client, setup.ConsultantId);
        Assert.Equal(3, assignments.Count);
        Assert.All(assignments, a => Assert.Equal(EquipmentStatus.Assigned, a.EquipmentStatus));
        Assert.Empty(GetEmailSender().SentMessages);
    }

    [Fact]
    public async Task EndOfEngagement_WithThreeItems_CreatesOneReturnPerItem_EquipmentReturnPending_AssignmentsStayActive()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 3);

        var response = await EndAsync(client, setup, isEndOfEngagement: true, to: "2026-11-05");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ended = await ReadEndAsync(response);
        Assert.Equal(AllocationStatus.Ended, ended.Status);
        Assert.True(ended.IsEndOfEngagement);
        Assert.Equal(CollectionFrom, ended.ExpectedCollectionFromDate);
        Assert.Equal(new DateOnly(2026, 11, 5), ended.ExpectedCollectionToDate);
        Assert.Equal(3, ended.ReturnsCreatedCount);
        Assert.Equal(0, ended.ReturnsAlreadyPendingCount);

        // One independent return per equipment, never a grouped one.
        var returns = await GetReturnsAsync(client, setup.ConsultantId);
        Assert.Equal(3, returns.Count);
        Assert.Equal(setup.Items.Select(i => i.EquipmentId).Order(), returns.Select(r => r.EquipmentId).Order());
        Assert.Equal(setup.Items.Select(i => i.AssignmentId).Order(), returns.Select(r => r.EquipmentAssignmentId).Order());
        Assert.All(returns, r =>
        {
            Assert.Equal(ReturnStatus.Pending, r.Status);
            Assert.Equal(CollectionFrom, r.ExpectedReturnDate);
            Assert.Equal("Fim de vínculo", r.Reason);
            Assert.Null(r.ReceivedOn);
        });

        foreach (var item in setup.Items)
        {
            var equipment = await GetEquipmentAsync(client, item.EquipmentId);
            Assert.Equal(EquipmentStatus.ReturnPending, equipment.Status);
            Assert.NotNull(equipment.CurrentCustody);
        }

        // Custody is untouched: every assignment is still Active.
        var assignments = await GetActiveAssignmentsAsync(client, setup.ConsultantId);
        Assert.Equal(3, assignments.Count);
        Assert.All(assignments, a =>
        {
            Assert.Null(a.EndedAtUtc);
            Assert.Equal(EquipmentStatus.ReturnPending, a.EquipmentStatus);
        });

        var consultant = await client.GetFromJsonAsync<ConsultantDetailResponse>(
            $"/api/consultants/{setup.ConsultantId}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(3, consultant!.CurrentAssignedEquipment.Count);
        Assert.Null(consultant.CurrentAllocation);
    }

    [Fact]
    public async Task EndOfEngagement_WithOneReturnAlreadyPending_CreatesOnlyTheMissingReturns_AndPreservesTheExistingOne()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 3);

        var existingResponse = await Phase5TestData.InitiateReturnAsync(
            client, setup.Items[0].AssignmentId, expectedReturnDate: new DateOnly(2026, 12, 24), reason: "Troca de equipamento");
        existingResponse.EnsureSuccessStatusCode();
        var existingId = (await existingResponse.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!.Id;
        // Re-read as stored, so the comparison below is database value to database value.
        var existing = (await client.GetFromJsonAsync<EquipmentReturnDetailResponse>(
            $"/api/equipment-returns/{existingId}", HttpClientTestExtensions.JsonOptions))!;
        GetEmailSender().Reset();

        var response = await EndAsync(client, setup, isEndOfEngagement: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ended = await ReadEndAsync(response);
        Assert.Equal(2, ended.ReturnsCreatedCount);
        Assert.Equal(1, ended.ReturnsAlreadyPendingCount);

        var returns = await GetReturnsAsync(client, setup.ConsultantId);
        Assert.Equal(3, returns.Count);
        Assert.Equal(3, returns.Select(r => r.EquipmentAssignmentId).Distinct().Count());

        // The pre-existing return is the same row, with nothing rewritten.
        var preserved = await client.GetFromJsonAsync<EquipmentReturnDetailResponse>(
            $"/api/equipment-returns/{existing.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ReturnStatus.Pending, preserved!.Status);
        Assert.Equal(new DateOnly(2026, 12, 24), preserved.ExpectedReturnDate);
        Assert.Equal("Troca de equipamento", preserved.Reason);
        Assert.Equal(existing.CreatedAtUtc, preserved.CreatedAtUtc);
        Assert.Equal(existing.UpdatedAtUtc, preserved.UpdatedAtUtc);

        // Its audit trail gained nothing either: still only its own manual creation.
        var existingAudit = await GetAuditAsync(client, existing.Id);
        var onlyEntry = Assert.Single(existingAudit);
        Assert.Equal(AuditActions.EquipmentReturn.Created, onlyEntry.Action);
        Assert.Equal("Manual", JsonDocument.Parse(onlyEntry.MetadataJson!).RootElement.GetProperty("trigger").GetString());

        // And it stays in the group it was created in, not the offboarding one.
        Assert.NotNull(existing.ReturnGroupId);
        Assert.Equal(existing.ReturnGroupId, preserved.ReturnGroupId);
        Assert.NotEqual(existing.ReturnGroupId, ended.ReturnGroupId);

        // Still part of the Consultant's outstanding equipment: listed once in the one email.
        var email = Assert.Single(GetEmailSender().SentMessages);
        Assert.All(setup.Items, item => Assert.Equal(1, CountEquipmentLines(email.PlainTextBody, item.AssetTag)));
    }

    /// <summary>
    /// The frontend's success message is built from these two numbers, so the
    /// wire contract is asserted on the raw JSON (exact camelCase property
    /// names, JSON numbers) and against what the operation really persisted.
    /// </summary>
    [Theory]
    [InlineData(3, 0, 3, 0)] // 3 new returns, none existing
    [InlineData(3, 1, 2, 1)] // 2 new, 1 already pending
    [InlineData(0, 0, 0, 0)] // no equipment
    [InlineData(2, 2, 0, 2)] // every item already pending
    [InlineData(1, 0, 1, 0)]
    public async Task EndOfEngagement_Response_CarriesReturnCountsMatchingThePersistedState(
        int itemCount, int alreadyPendingCount, int expectedCreated, int expectedAlreadyPending)
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, itemCount);
        var preExistingReturnIds = new List<Guid>();
        foreach (var item in setup.Items.Take(alreadyPendingCount))
        {
            preExistingReturnIds.Add((await Phase5TestData.InitiateReturnSuccessfullyAsync(client, item.AssignmentId)).Id);
        }

        var response = await EndAsync(client, setup, isEndOfEngagement: true, from: itemCount > 0 ? "2026-11-01" : null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(JsonValueKind.Number, json.GetProperty("returnsCreatedCount").ValueKind);
        Assert.Equal(JsonValueKind.Number, json.GetProperty("returnsAlreadyPendingCount").ValueKind);
        Assert.Equal(expectedCreated, json.GetProperty("returnsCreatedCount").GetInt32());
        Assert.Equal(expectedAlreadyPending, json.GetProperty("returnsAlreadyPendingCount").GetInt32());
        Assert.Equal("Ended", json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("isEndOfEngagement").GetBoolean());

        // The counts describe rows that exist: the Returns query shows exactly
        // one pending return per item, the new ones being those not there before.
        var returns = await GetReturnsAsync(client, setup.ConsultantId);
        Assert.Equal(itemCount, returns.Count);
        Assert.All(returns, r => Assert.Equal(ReturnStatus.Pending, r.Status));
        Assert.Equal(expectedCreated, returns.Count(r => !preExistingReturnIds.Contains(r.Id)));
        Assert.Equal(expectedAlreadyPending, returns.Count(r => preExistingReturnIds.Contains(r.Id)));
        Assert.Equal(setup.Items.Select(i => i.AssignmentId).Order(), returns.Select(r => r.EquipmentAssignmentId).Order());

        var pendingList = await client.GetFromJsonAsync<PagedResponse<EquipmentReturnSummaryResponse>>(
            $"/api/equipment-returns?consultantId={setup.ConsultantId}&status=Pending&pageSize=100", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(itemCount, pendingList!.Items.Count);

        var assignments = await GetActiveAssignmentsAsync(client, setup.ConsultantId);
        Assert.Equal(itemCount, assignments.Count);
        Assert.All(assignments, a => Assert.Equal(EquipmentStatus.ReturnPending, a.EquipmentStatus));
        Assert.Equal(AllocationStatus.Ended, (await GetAllocationAsync(client, setup)).Status);
    }

    [Fact]
    public async Task NotEndOfEngagement_Response_StillCarriesZeroReturnCounts()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 1);

        var response = await EndAsync(client, setup, isEndOfEngagement: false);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, json.GetProperty("returnsCreatedCount").GetInt32());
        Assert.Equal(0, json.GetProperty("returnsAlreadyPendingCount").GetInt32());
    }

    [Fact]
    public async Task EndOfEngagement_WithNoActiveEquipment_EndsAllocation_NoReturns_NoEmail()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 0);
        GetEmailSender().Reset();

        var response = await EndAsync(client, setup, isEndOfEngagement: true, from: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ended = await ReadEndAsync(response);
        Assert.Equal(AllocationStatus.Ended, ended.Status);
        Assert.True(ended.IsEndOfEngagement);
        Assert.Null(ended.ExpectedCollectionFromDate);
        Assert.Equal(0, ended.ReturnsCreatedCount);
        Assert.Equal(0, ended.ReturnsAlreadyPendingCount);
        Assert.Empty(await GetReturnsAsync(client, setup.ConsultantId));
        Assert.Empty(GetEmailSender().SentMessages);
    }

    [Fact]
    public async Task EndOfEngagement_WithEquipment_WithoutCollectionDate_IsRejected_AndChangesNothing()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 2);
        GetEmailSender().Reset();

        var response = await EndAsync(client, setup, isEndOfEngagement: true, from: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingChangedAsync(client, setup);
    }

    [Fact]
    public async Task EndOfEngagement_WithOneItemInAnUnsupportedLifecycleState_IsRejectedEntirely()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 3);
        // An Active assignment whose equipment is under maintenance: neither
        // "can initiate a return" nor "a return is already pending".
        await ForceEquipmentStatusAsync(setup.Items[1].EquipmentId, EquipmentStatus.UnderMaintenance);
        GetEmailSender().Reset();

        var response = await EndAsync(client, setup, isEndOfEngagement: true);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var title = await ReadProblemTitleAsync(response);
        Assert.Contains("Não foi possível encerrar o vínculo", title);
        Assert.Contains(setup.Items[1].AssetTag, title);
        Assert.DoesNotContain(setup.Items[0].AssetTag, title);

        var allocation = await GetAllocationAsync(client, setup);
        Assert.Equal(AllocationStatus.Active, allocation.Status);
        Assert.False(allocation.IsEndOfEngagement);
        Assert.Null(allocation.EndDate);
        Assert.Empty(await GetReturnsAsync(client, setup.ConsultantId));
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, setup.Items[0].EquipmentId)).Status);
        Assert.Equal(EquipmentStatus.UnderMaintenance, (await GetEquipmentAsync(client, setup.Items[1].EquipmentId)).Status);
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, setup.Items[2].EquipmentId)).Status);
        Assert.Equal(3, (await GetActiveAssignmentsAsync(client, setup.ConsultantId)).Count);
        Assert.Empty(GetEmailSender().SentMessages);
        Assert.Empty(await GetAuditAsync(client, setup.AllocationId, AuditActions.ConsultantAllocation.Ended));
    }

    [Fact]
    public async Task EndOfEngagement_WithAPendingReturnWhoseEquipmentIsNotReturnPending_IsRejectedEntirely()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 2);
        var existing = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, setup.Items[0].AssignmentId);
        // A Pending return whose equipment says Assigned: inconsistent, and
        // never "fixed" by guessing which of the two is right.
        await ForceEquipmentStatusAsync(setup.Items[0].EquipmentId, EquipmentStatus.Assigned);
        GetEmailSender().Reset();

        var response = await EndAsync(client, setup, isEndOfEngagement: true);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(setup.Items[0].AssetTag, await ReadProblemTitleAsync(response));
        Assert.Equal(AllocationStatus.Active, (await GetAllocationAsync(client, setup)).Status);
        var returns = await GetReturnsAsync(client, setup.ConsultantId);
        Assert.Equal(existing.Id, Assert.Single(returns).Id);
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, setup.Items[0].EquipmentId)).Status);
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, setup.Items[1].EquipmentId)).Status);
        Assert.Empty(GetEmailSender().SentMessages);
    }

    [Fact]
    public async Task EndOfEngagement_AfterACancelledReturn_InitiatesAFreshReturnForThatItem()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 1);
        var cancelled = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, setup.Items[0].AssignmentId);
        (await Phase5TestData.CancelReturnAsync(client, cancelled.Id)).EnsureSuccessStatusCode();

        var response = await EndAsync(client, setup, isEndOfEngagement: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ended = await ReadEndAsync(response);
        Assert.Equal(1, ended.ReturnsCreatedCount);
        Assert.Equal(0, ended.ReturnsAlreadyPendingCount);
        var returns = await GetReturnsAsync(client, setup.ConsultantId);
        Assert.Equal(2, returns.Count);
        Assert.Single(returns, r => r.Status == ReturnStatus.Cancelled && r.Id == cancelled.Id);
        Assert.Single(returns, r => r.Status == ReturnStatus.Pending);
    }

    [Fact]
    public async Task ReceivingOneReturn_EndsOnlyThatItemsCustody_OthersStayPendingInConsultantCustody()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 3);
        (await EndAsync(client, setup, isEndOfEngagement: true)).EnsureSuccessStatusCode();

        // After initiation and before any receipt, all three are still in custody.
        Assert.Equal(3, (await GetActiveAssignmentsAsync(client, setup.ConsultantId)).Count);

        var returns = await GetReturnsAsync(client, setup.ConsultantId);
        var received = returns.Single(r => r.EquipmentId == setup.Items[0].EquipmentId);
        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: $"Rua Receiver {Guid.NewGuid():N}, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);

        (await Phase5TestData.ReceiveReturnAsync(client, received.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId)).EnsureSuccessStatusCode();

        // The received item: custody ended, equipment under inspection.
        var receivedEquipment = await GetEquipmentAsync(client, setup.Items[0].EquipmentId);
        Assert.Equal(EquipmentStatus.UnderInspection, receivedEquipment.Status);
        Assert.Null(receivedEquipment.CurrentCustody);
        var receivedAssignments = await client.GetFromJsonAsync<PagedResponse<EquipmentAssignmentResponse>>(
            $"/api/equipment-assignments?equipmentId={setup.Items[0].EquipmentId}", HttpClientTestExtensions.JsonOptions);
        var endedAssignment = Assert.Single(receivedAssignments!.Items);
        Assert.Equal(AssignmentStatus.Ended, endedAssignment.Status);
        Assert.NotNull(endedAssignment.EndedAtUtc);

        // The other two: untouched.
        var stillActive = await GetActiveAssignmentsAsync(client, setup.ConsultantId);
        Assert.Equal(
            new[] { setup.Items[1].AssignmentId, setup.Items[2].AssignmentId }.Order(), stillActive.Select(a => a.Id).Order());
        foreach (var item in setup.Items.Skip(1))
        {
            var equipment = await GetEquipmentAsync(client, item.EquipmentId);
            Assert.Equal(EquipmentStatus.ReturnPending, equipment.Status);
            Assert.NotNull(equipment.CurrentCustody);
        }

        var returnsAfter = await GetReturnsAsync(client, setup.ConsultantId);
        Assert.Equal(ReturnStatus.Received, returnsAfter.Single(r => r.Id == received.Id).Status);
        Assert.All(returnsAfter.Where(r => r.Id != received.Id), r =>
        {
            Assert.Equal(ReturnStatus.Pending, r.Status);
            Assert.Null(r.ReceivedOn);
        });
    }

    [Fact]
    public async Task EndOfEngagement_SendsExactlyOneEmail_ListingEveryItemOnce()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 4);
        GetEmailSender().Reset();

        (await EndAsync(client, setup, isEndOfEngagement: true)).EnsureSuccessStatusCode();

        // One consolidated message in total: no per-return email exists.
        var email = Assert.Single(GetEmailSender().SentMessages);
        Assert.Equal(setup.ConsultantEmail, email.ToAddress);
        Assert.Null(email.Attachment);
        Assert.All(setup.Items, item => Assert.Equal(1, CountEquipmentLines(email.PlainTextBody, item.AssetTag)));
        Assert.Equal(4, email.PlainTextBody.Split('\n').Count(line => line.TrimStart().StartsWith("- ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task EndOfEngagement_WhenEmailDeliveryFails_KeepsTheEndingAndTheReturns()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 2);
        GetEmailSender().Reset();
        GetEmailSender().FailNextSendWith = EmailSendFailureReason.Rejected;

        var response = await EndAsync(client, setup, isEndOfEngagement: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await ReadEndAsync(response)).ReturnsCreatedCount);
        Assert.Empty(GetEmailSender().SentMessages);
        Assert.Equal(AllocationStatus.Ended, (await GetAllocationAsync(client, setup)).Status);
        var returns = await GetReturnsAsync(client, setup.ConsultantId);
        Assert.Equal(2, returns.Count);
        Assert.All(returns, r => Assert.Equal(ReturnStatus.Pending, r.Status));
        Assert.Single(await GetAuditAsync(client, setup.AllocationId, AuditActions.ConsultantAllocation.CollectionNotificationFailed));
    }

    [Fact]
    public async Task EndOfEngagement_RepeatedInvocation_IsRejected_AndCreatesNoDuplicateReturn()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 3);
        (await EndAsync(client, setup, isEndOfEngagement: true)).EnsureSuccessStatusCode();
        GetEmailSender().Reset();

        var second = await EndAsync(client, setup, isEndOfEngagement: true);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var returns = await GetReturnsAsync(client, setup.ConsultantId);
        Assert.Equal(3, returns.Count);
        Assert.Equal(3, returns.Select(r => r.EquipmentAssignmentId).Distinct().Count());
        Assert.Empty(GetEmailSender().SentMessages);
    }

    [Fact]
    public async Task EndOfEngagement_ConcurrentForSameAllocation_OnlyOneSucceeds_NoDuplicateReturns_OneEmail()
    {
        using var clientA = await LoginAsync("Administration");
        using var clientB = await LoginAsync("HR");
        var setup = await SetUpConsultantWithEquipmentAsync(clientA, 3);
        GetEmailSender().Reset();

        var results = await Task.WhenAll(
            EndAsync(clientA, setup, isEndOfEngagement: true),
            EndAsync(clientB, setup, isEndOfEngagement: true));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var returns = await GetReturnsAsync(clientA, setup.ConsultantId);
        Assert.Equal(3, returns.Count);
        Assert.Equal(3, returns.Select(r => r.EquipmentAssignmentId).Distinct().Count());
        Assert.Single(GetEmailSender().SentMessages);
        Assert.Single(await GetAuditAsync(clientA, setup.AllocationId, AuditActions.ConsultantAllocation.Ended));
    }

    [Fact]
    public async Task EndOfEngagement_ConcurrentWithManualReturnForTheSameItem_NeverLeavesTwoOpenReturns()
    {
        using var clientA = await LoginAsync("Administration");
        using var clientB = await LoginAsync("Administration");

        // Repeated to exercise both interleavings; the invariant must hold in each.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var setup = await SetUpConsultantWithEquipmentAsync(clientA, 2);

            var endTask = EndAsync(clientA, setup, isEndOfEngagement: true);
            var manualTask = Phase5TestData.InitiateReturnAsync(clientB, setup.Items[0].AssignmentId);
            await Task.WhenAll(endTask, manualTask);
            var endResponse = await endTask;

            var openReturns = (await GetReturnsAsync(clientA, setup.ConsultantId)).Where(r => r.Status != ReturnStatus.Cancelled).ToList();
            Assert.Equal(openReturns.Count, openReturns.Select(r => r.EquipmentAssignmentId).Distinct().Count());

            var allocation = await GetAllocationAsync(clientA, setup);
            if (endResponse.StatusCode == HttpStatusCode.OK)
            {
                // Atomic success: every item has exactly one open return.
                var ended = await ReadEndAsync(endResponse);
                Assert.Equal(2, ended.ReturnsCreatedCount + ended.ReturnsAlreadyPendingCount);
                Assert.Equal(AllocationStatus.Ended, allocation.Status);
                Assert.Equal(2, openReturns.Count);
            }
            else
            {
                // Atomic failure: the engagement was not ended and it created nothing.
                Assert.Equal(HttpStatusCode.Conflict, endResponse.StatusCode);
                Assert.Equal(AllocationStatus.Active, allocation.Status);
                Assert.Equal(HttpStatusCode.Created, (await manualTask).StatusCode);
                Assert.Equal(setup.Items[0].AssignmentId, Assert.Single(openReturns).EquipmentAssignmentId);
                Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(clientA, setup.Items[1].EquipmentId)).Status);
            }
        }
    }

    [Fact]
    public async Task EndOfEngagement_Audit_RecordsTheEndingAndWhichReturnsItInitiated_WithoutPii()
    {
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 3);
        var alreadyPending = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, setup.Items[2].AssignmentId);

        (await EndAsync(client, setup, isEndOfEngagement: true)).EnsureSuccessStatusCode();

        var returns = await GetReturnsAsync(client, setup.ConsultantId);
        var createdIds = returns.Where(r => r.Id != alreadyPending.Id).Select(r => r.Id).ToList();
        Assert.Equal(2, createdIds.Count);

        var allocationAudit = await GetAuditAsync(client, setup.AllocationId);
        var endedEntry = Assert.Single(allocationAudit, e => e.Action == AuditActions.ConsultantAllocation.Ended);
        Assert.True(JsonDocument.Parse(endedEntry.NewValuesJson!).RootElement.GetProperty("isEndOfEngagement").GetBoolean());

        var initiatedEntry = Assert.Single(allocationAudit, e => e.Action == AuditActions.ConsultantAllocation.EndOfEngagementReturnsInitiated);
        var initiated = JsonDocument.Parse(initiatedEntry.NewValuesJson!).RootElement;
        Assert.Equal(
            createdIds.Order(), initiated.GetProperty("createdReturnIds").EnumerateArray().Select(e => e.GetGuid()).Order());
        Assert.Equal(alreadyPending.Id, Assert.Single(initiated.GetProperty("alreadyPendingReturnIds").EnumerateArray()).GetGuid());

        // Each initiated return keeps its normal Created entry, tagged with what triggered it.
        foreach (var returnId in createdIds)
        {
            var entry = Assert.Single(await GetAuditAsync(client, returnId));
            Assert.Equal(AuditActions.EquipmentReturn.Created, entry.Action);
            var metadata = JsonDocument.Parse(entry.MetadataJson!).RootElement;
            Assert.Equal("EndOfEngagement", metadata.GetProperty("trigger").GetString());
            Assert.Equal(setup.AllocationId, metadata.GetProperty("consultantAllocationId").GetGuid());
        }

        var consultant = await client.GetFromJsonAsync<ConsultantDetailResponse>(
            $"/api/consultants/{setup.ConsultantId}", HttpClientTestExtensions.JsonOptions);
        foreach (var entry in allocationAudit)
        {
            var combined = $"{entry.OldValuesJson}{entry.NewValuesJson}{entry.MetadataJson}";
            Assert.DoesNotContain(consultant!.Email, combined);
            Assert.DoesNotContain(consultant.Nif, combined);
            Assert.DoesNotContain(consultant.FullName, combined);
        }
    }

    [Fact]
    public async Task Hr_CanEndAnEngagementAndInitiateTheReturns()
    {
        using var admin = await LoginAsync("Administration");
        using var hr = await LoginAsync("HR");
        var setup = await SetUpConsultantWithEquipmentAsync(admin, 2);

        var response = await EndAsync(hr, setup, isEndOfEngagement: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await ReadEndAsync(response)).ReturnsCreatedCount);
    }

    [Fact]
    public async Task EndOfEngagement_ByAnActorWithoutReturnPermission_IsRefusedWhenItWouldInitiateReturns()
    {
        // No role today holds ConsultantAllocation.End without Equipment.Return,
        // so this boundary is exercised at the service the endpoint calls.
        using var client = await LoginAsync("Administration");
        var setup = await SetUpConsultantWithEquipmentAsync(client, 2);
        GetEmailSender().Reset();

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ConsultantAllocationService>();
            var result = await service.EndAsync(
                setup.ConsultantId, setup.AllocationId,
                new EndAllocationData(new DateOnly(2026, 10, 15), IsEndOfEngagement: true, ActorMayInitiateReturns: false, CollectionFrom),
                Guid.NewGuid(), "Test Actor");

            Assert.False(result.Succeeded);
            Assert.Equal(ServiceErrorCode.ReturnPermissionRequired, result.ErrorCode);
        }

        await AssertNothingChangedAsync(client, setup);

        // The same actor may still end an allocation that initiates nothing.
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ConsultantAllocationService>();
            var result = await service.EndAsync(
                setup.ConsultantId, setup.AllocationId,
                new EndAllocationData(new DateOnly(2026, 10, 15), IsEndOfEngagement: false, ActorMayInitiateReturns: false),
                Guid.NewGuid(), "Test Actor");

            Assert.True(result.Succeeded);
        }
    }

    private sealed record CustodySetup(
        Guid ConsultantId, string ConsultantEmail, Guid AllocationId, IReadOnlyList<(Guid EquipmentId, string AssetTag, Guid AssignmentId)> Items);

    /// <summary>A Consultant with one Active allocation and <paramref name="equipmentCount"/> Generic items in active custody, each delivered through its own finalized handover.</summary>
    private static async Task<CustodySetup> SetUpConsultantWithEquipmentAsync(HttpClient client, int equipmentCount)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var consultant = await client.GetFromJsonAsync<ConsultantDetailResponse>($"/api/consultants/{consultantId}", HttpClientTestExtensions.JsonOptions);
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

        return new CustodySetup(consultantId, consultant!.Email, allocation.Id, items);
    }

    private static async Task<HttpResponseMessage> EndAsync(
        HttpClient client, CustodySetup setup, bool isEndOfEngagement, string? from = "2026-11-01", string? to = null)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync(
            $"/api/consultants/{setup.ConsultantId}/allocations/{setup.AllocationId}/end",
            new
            {
                EndDate = "2026-10-15",
                IsEndOfEngagement = isEndOfEngagement,
                ExpectedCollectionFromDate = isEndOfEngagement ? from : null,
                ExpectedCollectionToDate = isEndOfEngagement ? to : null,
            },
            csrfToken);
    }

    private static async Task<EndConsultantAllocationResponse> ReadEndAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<EndConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions))!;

    private static async Task<string> ReadProblemTitleAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString()!;

    private static async Task<ConsultantAllocationResponse> GetAllocationAsync(HttpClient client, CustodySetup setup) =>
        (await client.GetFromJsonAsync<ConsultantAllocationResponse>(
            $"/api/consultants/{setup.ConsultantId}/allocations/{setup.AllocationId}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<IReadOnlyList<EquipmentReturnSummaryResponse>> GetReturnsAsync(HttpClient client, Guid consultantId) =>
        (await client.GetFromJsonAsync<PagedResponse<EquipmentReturnSummaryResponse>>(
            $"/api/equipment-returns?consultantId={consultantId}&pageSize=100", HttpClientTestExtensions.JsonOptions))!.Items;

    private static async Task<IReadOnlyList<EquipmentAssignmentResponse>> GetActiveAssignmentsAsync(HttpClient client, Guid consultantId) =>
        (await client.GetFromJsonAsync<PagedResponse<EquipmentAssignmentResponse>>(
            $"/api/equipment-assignments?consultantId={consultantId}&status=Active&pageSize=100", HttpClientTestExtensions.JsonOptions))!.Items;

    private static async Task<EquipmentDetailResponse> GetEquipmentAsync(HttpClient client, Guid equipmentId) =>
        (await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipmentId}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<IReadOnlyList<AuditLogResponse>> GetAuditAsync(HttpClient client, Guid entityId, string? action = null)
    {
        var page = await client.GetFromJsonAsync<PagedResponse<AuditLogResponse>>(
            $"/api/audit?entityId={entityId}&page=1&pageSize=100", HttpClientTestExtensions.JsonOptions);
        return action is null ? page!.Items : page!.Items.Where(e => e.Action == action).ToList();
    }

    private static async Task AssertNothingChangedAsync(HttpClient client, CustodySetup setup)
    {
        Assert.Equal(AllocationStatus.Active, (await GetAllocationAsync(client, setup)).Status);
        Assert.Empty(await GetReturnsAsync(client, setup.ConsultantId));
        var assignments = await GetActiveAssignmentsAsync(client, setup.ConsultantId);
        Assert.Equal(setup.Items.Count, assignments.Count);
        Assert.All(assignments, a => Assert.Equal(EquipmentStatus.Assigned, a.EquipmentStatus));
    }

    /// <summary>Lines of the collection email's equipment list that are for exactly this asset tag (the template ends each line with it).</summary>
    private static int CountEquipmentLines(string body, string assetTag) =>
        body.Split('\n').Count(line => line.TrimEnd().EndsWith($"· {assetTag}", StringComparison.Ordinal));

    /// <summary>Test-only: writes an equipment status directly, bypassing every application rule, to simulate inconsistent data no normal flow can produce.</summary>
    private async Task ForceEquipmentStatusAsync(Guid equipmentId, EquipmentStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var equipment = await dbContext.Equipment.FirstAsync(e => e.Id == equipmentId);
        equipment.Status = status;
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
