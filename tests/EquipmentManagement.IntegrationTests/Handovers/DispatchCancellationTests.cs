using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.ResponsibilityTerms;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Handovers;

/// <summary>
/// "Anular envio": the active carrier dispatch is cancelled before delivery
/// is confirmed. Every shipped item returns InTransit -> Available as one
/// unit, no Assignment ever exists, the attempt stays as history, a current
/// pre-delivery term is superseded, and the Draft can be dispatched again.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class DispatchCancellationTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string Reason = "Transportadora não recolheu a encomenda";

    [Fact]
    public async Task Hr_CancelsTheDispatch_EveryItemReturnsToAvailable_AndTheAttemptStaysAsHistory()
    {
        using var hr = await LoginAsync("HR");
        var (handoverId, equipmentIds) = await CreateDispatchedCarrierAsync(hr, itemCount: 3);
        var dispatchId = await ActiveDispatchIdAsync(handoverId);

        var response = await CancelAsync(hr, handoverId, $"  {Reason}  ");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        foreach (var id in equipmentIds)
        {
            var equipment = await GetEquipmentAsync(hr, id);
            Assert.Equal(EquipmentStatus.Available, equipment.Status);
            Assert.Null(equipment.CurrentCustody);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await dbContext.EquipmentAssignments.AnyAsync(a => equipmentIds.Contains(a.EquipmentId)));

        // The row is kept, never deleted: who/when dispatched, plus who/when/why cancelled.
        var dispatch = await dbContext.EquipmentDispatches.SingleAsync(d => d.EquipmentHandoverId == handoverId);
        Assert.Equal(dispatchId, dispatch.Id);
        Assert.NotNull(dispatch.CancelledAtUtc);
        Assert.Equal(Reason, dispatch.CancellationReason);
        Assert.NotNull(dispatch.CancelledByUserId);
        Assert.False(string.IsNullOrWhiteSpace(dispatch.DispatchedByDisplayNameSnapshot));

        var detail = await GetHandoverAsync(hr, handoverId);
        Assert.Equal(HandoverStatus.Draft, detail.Status);
        Assert.Null(detail.Dispatch);
        var cancelled = Assert.Single(detail.CancelledDispatches!);
        Assert.Equal(Reason, cancelled.CancellationReason);
        Assert.All(detail.Items, i => Assert.Equal(EquipmentStatus.Available, i.EquipmentStatus));

        // Audit: the original dispatch row is untouched, the cancellation adds the reverse transition.
        Assert.Single(await dbContext.AuditLogs.Where(a => a.Action == AuditActions.EquipmentDispatch.Registered && a.EntityId == handoverId.ToString()).ToListAsync());
        var audit = await dbContext.AuditLogs.SingleAsync(a => a.Action == AuditActions.EquipmentDispatch.Cancelled && a.EntityId == handoverId.ToString());
        Assert.NotNull(audit.ActorUserId);
        using var json = JsonDocument.Parse(audit.NewValuesJson!);
        Assert.Equal(Reason, json.RootElement.GetProperty("reason").GetString());
        Assert.Equal("InTransit", json.RootElement.GetProperty("equipmentStatusFrom").GetString());
        Assert.Equal("Available", json.RootElement.GetProperty("equipmentStatusTo").GetString());
        Assert.Equal(dispatchId, json.RootElement.GetProperty("dispatchId").GetGuid());
        Assert.Equal(3, json.RootElement.GetProperty("assetTags").GetArrayLength());
    }

    [Fact]
    public async Task Administration_CancelsTheDispatch()
    {
        using var admin = await LoginAsync("Administration");
        var (handoverId, equipmentIds) = await CreateDispatchedCarrierAsync(admin);

        Assert.Equal(HttpStatusCode.NoContent, (await CancelAsync(admin, handoverId, Reason)).StatusCode);
        Assert.Equal(EquipmentStatus.Available, (await GetEquipmentAsync(admin, equipmentIds[0])).Status);
    }

    [Fact]
    public async Task WithoutThePermission_OrWithoutCsrf_NothingChanges()
    {
        using var hr = await LoginAsync("HR");
        var (handoverId, equipmentIds) = await CreateDispatchedCarrierAsync(hr);

        var (email, password) = await ApiTestUsers.CreateAsync(factory, "HR");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await userManager.RemoveFromRoleAsync((await userManager.FindByEmailAsync(email))!, "HR")).Succeeded);
        }

        using var noRole = factory.CreateClient();
        await noRole.LoginAsync(email, password);
        Assert.Equal(HttpStatusCode.Forbidden, (await CancelAsync(noRole, handoverId, Reason)).StatusCode);

        var noCsrf = await hr.PostAsJsonAsync($"/api/equipment-handovers/{handoverId}/dispatch/cancel", new { Reason });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);

        Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(hr, equipmentIds[0])).Status);
        Assert.NotNull((await GetHandoverAsync(hr, handoverId)).Dispatch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingOrBlankReason_IsAValidationFailure(string? reason)
    {
        using var hr = await LoginAsync("HR");
        var (handoverId, equipmentIds) = await CreateDispatchedCarrierAsync(hr);

        Assert.Equal(HttpStatusCode.BadRequest, (await CancelAsync(hr, handoverId, reason)).StatusCode);
        Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(hr, equipmentIds[0])).Status);
    }

    [Fact]
    public async Task OverlongReason_IsAValidationFailure()
    {
        using var hr = await LoginAsync("HR");
        var (handoverId, _) = await CreateDispatchedCarrierAsync(hr);

        Assert.Equal(HttpStatusCode.BadRequest, (await CancelAsync(hr, handoverId, new string('x', EquipmentDispatch.MaxCancellationReasonLength + 1))).StatusCode);
    }

    [Fact]
    public async Task InPersonHandover_AndCarrierDraftBeforeDispatch_AreNotCancellable()
    {
        using var hr = await LoginAsync("HR");
        var (inPersonEquipment, _) = await CreateGenericEquipmentAsync(hr);
        var (inPerson, _) = await PrepareDraftAsync(hr, inPersonEquipment, "InPerson");
        var (carrierEquipment, _) = await CreateGenericEquipmentAsync(hr);
        var (carrier, _) = await PrepareDraftAsync(hr, carrierEquipment, "Carrier");

        Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(hr, inPerson.Id, Reason)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(hr, carrier.Id, Reason)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CancelAsync(hr, Guid.NewGuid(), Reason)).StatusCode);
        Assert.Equal(EquipmentStatus.Available, (await GetEquipmentAsync(hr, carrierEquipment)).Status);
    }

    [Fact]
    public async Task AfterConfirmarEntrega_TheDispatchCanNoLongerBeCancelled()
    {
        using var hr = await LoginAsync("HR");
        var (handoverId, equipmentIds) = await CreateDispatchedCarrierAsync(hr);
        (await Phase4TestData.FinalizeAsync(hr, handoverId)).EnsureSuccessStatusCode();

        var response = await CancelAsync(hr, handoverId, Reason);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("devolução", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(hr, equipmentIds[0])).Status);
        Assert.Equal(1, await CountActiveAssignmentsAsync(equipmentIds[0]));
    }

    [Fact]
    public async Task InconsistentActiveAssignment_BlocksTheWholeCancellation()
    {
        using var hr = await LoginAsync("HR");
        var (handoverId, equipmentIds) = await CreateDispatchedCarrierAsync(hr, itemCount: 2);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var handover = await dbContext.EquipmentHandovers.SingleAsync(h => h.Id == handoverId);
            var item = await dbContext.EquipmentHandoverItems.FirstAsync(i => i.EquipmentHandoverId == handoverId && i.EquipmentId == equipmentIds[0]);
            dbContext.EquipmentAssignments.Add(new EquipmentAssignment
            {
                Id = Guid.NewGuid(), EquipmentId = equipmentIds[0], ConsultantId = handover.ConsultantId, Origin = EquipmentAssignmentOrigin.Handover,
                EquipmentHandoverId = handoverId, EquipmentHandoverItemId = item.Id, Status = AssignmentStatus.Active,
                StartedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            });
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(hr, handoverId, Reason)).StatusCode);

        // Nothing was freed partially.
        foreach (var id in equipmentIds)
        {
            Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(hr, id)).Status);
        }

        Assert.NotNull((await GetHandoverAsync(hr, handoverId)).Dispatch);
    }

    [Fact]
    public async Task OneItemNoLongerInTransit_BlocksTheWholeCancellation()
    {
        using var hr = await LoginAsync("HR");
        var (handoverId, equipmentIds) = await CreateDispatchedCarrierAsync(hr, itemCount: 2);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Equipment.Where(e => e.Id == equipmentIds[1]).ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EquipmentStatus.Damaged));
        }

        Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(hr, handoverId, Reason)).StatusCode);
        Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(hr, equipmentIds[0])).Status);
        Assert.Equal(EquipmentStatus.Damaged, (await GetEquipmentAsync(hr, equipmentIds[1])).Status);
    }

    [Fact]
    public async Task CancellingTwice_RecordsOneCancellation_SequentiallyAndConcurrently()
    {
        using var hr = await LoginAsync("HR");
        using var hr2 = await LoginAsync("HR");

        var (sequential, _) = await CreateDispatchedCarrierAsync(hr);
        Assert.Equal(HttpStatusCode.NoContent, (await CancelAsync(hr, sequential, Reason)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(hr, sequential, "Outra vez")).StatusCode);
        Assert.Equal(1, await CountCancellationsAsync(sequential));

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (concurrent, _) = await CreateDispatchedCarrierAsync(hr);
            var results = await Task.WhenAll(CancelAsync(hr, concurrent, Reason), CancelAsync(hr2, concurrent, Reason));

            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.NoContent));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, await CountCancellationsAsync(concurrent));
        }
    }

    [Fact]
    public async Task Race_CancelVersusConfirmarEntrega_ExactlyOneWins()
    {
        using var hr = await LoginAsync("HR");
        using var hr2 = await LoginAsync("HR");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (handoverId, equipmentIds) = await CreateDispatchedCarrierAsync(hr, itemCount: 2);

            var results = await Task.WhenAll(CancelAsync(hr, handoverId, Reason), Phase4TestData.FinalizeAsync(hr2, handoverId));

            Assert.Equal(1, results.Count(r => r.IsSuccessStatusCode));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

            var detail = await GetHandoverAsync(hr, handoverId);
            if (results[0].IsSuccessStatusCode)
            {
                Assert.Equal(HandoverStatus.Draft, detail.Status);
                Assert.Null(detail.Dispatch);
                foreach (var id in equipmentIds)
                {
                    Assert.Equal(EquipmentStatus.Available, (await GetEquipmentAsync(hr, id)).Status);
                    Assert.Equal(0, await CountActiveAssignmentsAsync(id));
                }
            }
            else
            {
                Assert.Equal(HandoverStatus.Finalized, detail.Status);
                Assert.Equal(0, await CountCancellationsAsync(handoverId));
                foreach (var id in equipmentIds)
                {
                    Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(hr, id)).Status);
                    Assert.Equal(1, await CountActiveAssignmentsAsync(id));
                }
            }
        }
    }

    [Fact]
    public async Task Race_CancelVersusACompetingDraftDispatch_NeverLeavesTwoActiveDispatchesOrAPartialState()
    {
        using var hr = await LoginAsync("HR");
        using var hr2 = await LoginAsync("HR");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (equipmentId, _) = await CreateGenericEquipmentAsync(hr);
            var (draftA, _) = await PrepareDraftAsync(hr, equipmentId, "Carrier");
            var (draftB, _) = await PrepareDraftAsync(hr2, equipmentId, "Carrier");
            (await Phase4TestData.RegisterDispatchAsync(hr, draftA.Id)).EnsureSuccessStatusCode();

            var results = await Task.WhenAll(CancelAsync(hr, draftA.Id, Reason), Phase4TestData.RegisterDispatchAsync(hr2, draftB.Id));
            Assert.All(results, r => Assert.True(r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.Conflict, $"Unexpected {r.StatusCode}"));

            var a = await GetHandoverAsync(hr, draftA.Id);
            var b = await GetHandoverAsync(hr, draftB.Id);
            Assert.False(a.Dispatch is not null && b.Dispatch is not null, "Two active dispatches hold the same item.");
            var status = (await GetEquipmentAsync(hr, equipmentId)).Status;
            Assert.Equal(a.Dispatch is not null || b.Dispatch is not null ? EquipmentStatus.InTransit : EquipmentStatus.Available, status);
            Assert.Equal(0, await CountActiveAssignmentsAsync(equipmentId));
        }
    }

    [Fact]
    public async Task AfterCancellation_TheDraftCanBeCorrected_DispatchedAgain_AndConfirmed()
    {
        using var hr = await LoginAsync("HR");
        var (handoverId, equipmentIds) = await CreateDispatchedCarrierAsync(hr);
        (await CancelAsync(hr, handoverId, Reason)).EnsureSuccessStatusCode();

        // The composition lock is gone: an item may be added before re-dispatch.
        var (extraId, _) = await CreateGenericEquipmentAsync(hr);
        var add = await Phase4TestData.AddItemAsync(hr, handoverId, extraId);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var extraItem = (await add.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!
            .Items.Single(i => i.EquipmentId == extraId);
        (await Phase4TestData.RecordInspectionAsync(hr, handoverId, extraItem.Id)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await Phase4TestData.RegisterDispatchAsync(hr, handoverId)).StatusCode);
        Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(hr, equipmentIds[0])).Status);
        Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(hr, extraId)).Status);

        (await Phase4TestData.FinalizeAsync(hr, handoverId)).EnsureSuccessStatusCode();
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(hr, equipmentIds[0])).Status);
        Assert.Equal(1, await CountActiveAssignmentsAsync(equipmentIds[0]));
        Assert.Equal(1, await CountActiveAssignmentsAsync(extraId));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dispatches = await dbContext.EquipmentDispatches.Where(d => d.EquipmentHandoverId == handoverId).ToListAsync();
        Assert.Equal(2, dispatches.Count);
        Assert.Single(dispatches, d => d.CancelledAtUtc is null);
    }

    [Fact]
    public async Task ACompetingDraft_BecomesEligibleAgain_OnceTheItemIsAvailable()
    {
        using var hr = await LoginAsync("HR");
        var (equipmentId, _) = await CreateGenericEquipmentAsync(hr);
        var (carrier, _) = await PrepareDraftAsync(hr, equipmentId, "Carrier");
        var (inPerson, _) = await PrepareDraftAsync(hr, equipmentId, "InPerson");
        (await Phase4TestData.RegisterDispatchAsync(hr, carrier.Id)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Phase4TestData.FinalizeAsync(hr, inPerson.Id)).StatusCode);

        (await CancelAsync(hr, carrier.Id, Reason)).EnsureSuccessStatusCode();

        Assert.Equal(EquipmentStatus.Available, Assert.Single((await GetHandoverAsync(hr, inPerson.Id)).Items).EquipmentStatus);
        Assert.Equal(HttpStatusCode.OK, (await Phase4TestData.FinalizeAsync(hr, inPerson.Id)).StatusCode);
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(hr, equipmentId)).Status);
    }

    [Fact]
    public async Task ACurrentPreparedTerm_IsSuperseded_ItsFileKept_AndAReDispatchGetsANewTerm()
    {
        using var hr = await LoginAsync("HR");
        var (handoverId, _) = await CreateDispatchedCarrierAsync(hr);
        var v1 = await GenerateTermAsync(hr, handoverId);
        var v1Bytes = await DownloadAsync(hr, v1.Id);
        var (v1Before, v1ItemsBefore) = await TermSnapshotTestData.GetAsync(factory, v1.Id);
        Assert.NotNull(v1Before.ConsultantSnapshot);

        (await CancelAsync(hr, handoverId, Reason)).EnsureSuccessStatusCode();

        // The superseded version keeps its own snapshot, untouched.
        var (v1After, v1ItemsAfter) = await TermSnapshotTestData.GetAsync(factory, v1.Id);
        Assert.False(v1After.SnapshotUnavailable);
        Assert.Equal(v1Before.ConsultantSnapshot!.FullName, v1After.ConsultantSnapshot!.FullName);
        Assert.Equal(v1Before.ConsultantSnapshot.Email, v1After.ConsultantSnapshot.Email);
        Assert.Equal(v1Before.AllocationSnapshot?.RoleOrFunction, v1After.AllocationSnapshot?.RoleOrFunction);
        Assert.Equal(
            v1ItemsBefore.Select(i => (i.EquipmentId, i.EquipmentSnapshot.AssetTag, i.EquipmentSnapshot.Model, i.EquipmentPhotoSetId)),
            v1ItemsAfter.Select(i => (i.EquipmentId, i.EquipmentSnapshot.AssetTag, i.EquipmentSnapshot.Model, i.EquipmentPhotoSetId)));

        var afterCancel = Assert.Single(await ListTermsAsync(hr, handoverId));
        Assert.Equal(ResponsibilityTermStatus.Superseded, afterCancel.Status);
        Assert.Equal(v1.Sha256, afterCancel.Sha256);
        Assert.Equal(v1Bytes, await DownloadAsync(hr, v1.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await Phase7TestData.SendAsync(hr, v1.Id)).StatusCode);
        Assert.Null((await GetHandoverAsync(hr, handoverId)).ConsultantSnapshot);

        // Not generatable until the Draft is dispatched again.
        Assert.False((await PostGenerateAsync(hr, handoverId)).IsSuccessStatusCode);

        (await Phase4TestData.RegisterDispatchAsync(hr, handoverId)).EnsureSuccessStatusCode();
        var v2 = await GenerateTermAsync(hr, handoverId);
        Assert.Equal(2, v2.Version);
        Assert.Equal(ResponsibilityTermStatus.Generated, v2.Status);

        (await Phase4TestData.FinalizeAsync(hr, handoverId)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await Phase7TestData.SendAsync(hr, v2.Id)).StatusCode);
        (await Phase7TestData.UploadSignedDocumentAsync(hr, v2.Id)).EnsureSuccessStatusCode();
        Assert.Equal(HandoverOperationalStatus.Completed, (await GetHandoverAsync(hr, handoverId)).OperationalStatus);
    }

    [Fact]
    public async Task InconsistentSignedTerm_BlocksTheCancellation()
    {
        using var hr = await LoginAsync("HR");
        var (handoverId, equipmentIds) = await CreateDispatchedCarrierAsync(hr);
        var term = await GenerateTermAsync(hr, handoverId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ResponsibilityTermSignedArtifacts.Add(new ResponsibilityTermSignedArtifact
            {
                Id = Guid.NewGuid(), ResponsibilityTermId = term.Id, StorageKey = $"synthetic/{Guid.NewGuid():N}", FileName = "signed.pdf",
                ContentType = "application/pdf", SizeBytes = 1, Sha256 = new string('0', 64), UploadedAtUtc = DateTime.UtcNow,
                UploadedByUserId = Guid.NewGuid(), UploadedByDisplayNameSnapshot = "Synthetic", CreatedAtUtc = DateTime.UtcNow,
            });
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(hr, handoverId, Reason)).StatusCode);
        Assert.Equal(ResponsibilityTermStatus.Generated, Assert.Single(await ListTermsAsync(hr, handoverId)).Status);
        Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(hr, equipmentIds[0])).Status);
    }

    [Fact]
    public async Task AfterCancellation_AvailableOnlyWorkflowsFollowTheirOwnRulesAgain()
    {
        using var admin = await LoginAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(admin, kind: "Laptop", assetTagPrefix: "LP");
        var laptop = await Phase3TestData.CreateLaptopEquipmentAsync(admin, type.Id);
        var (handover, _) = await PrepareDraftAsync(admin, laptop.Id, "Carrier");
        (await Phase4TestData.RegisterDispatchAsync(admin, handover.Id)).EnsureSuccessStatusCode();
        Assert.False((await GetEquipmentAsync(admin, laptop.Id)).CurrentPhotosCorrectable);

        (await CancelAsync(admin, handover.Id, Reason)).EnsureSuccessStatusCode();

        Assert.True((await GetEquipmentAsync(admin, laptop.Id)).CurrentPhotosCorrectable);
        var csrf = await admin.GetAntiforgeryTokenAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync<object?>($"/api/equipment/{laptop.Id}/retire", null, csrf)).StatusCode);
    }

    private static async Task<HttpResponseMessage> CancelAsync(HttpClient client, Guid handoverId, string? reason)
    {
        var csrf = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync($"/api/equipment-handovers/{handoverId}/dispatch/cancel", new { Reason = reason }, csrf);
    }

    private static async Task<(Guid HandoverId, List<Guid> EquipmentIds)> CreateDispatchedCarrierAsync(HttpClient client, int itemCount = 1)
    {
        var (first, _) = await CreateGenericEquipmentAsync(client);
        var (handover, _) = await PrepareDraftAsync(client, first, "Carrier");
        var ids = new List<Guid> { first };
        for (var i = 1; i < itemCount; i++)
        {
            var (next, _) = await CreateGenericEquipmentAsync(client);
            var add = await Phase4TestData.AddItemAsync(client, handover.Id, next);
            add.EnsureSuccessStatusCode();
            var item = (await add.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!.Items.Single(x => x.EquipmentId == next);
            (await Phase4TestData.RecordInspectionAsync(client, handover.Id, item.Id)).EnsureSuccessStatusCode();
            ids.Add(next);
        }

        (await Phase4TestData.RegisterDispatchAsync(client, handover.Id)).EnsureSuccessStatusCode();
        return (handover.Id, ids);
    }

    private static async Task<(Guid EquipmentId, Guid TypeId)> CreateGenericEquipmentAsync(HttpClient client)
    {
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        return (equipment.Id, type.Id);
    }

    private static async Task<(EquipmentHandoverDetailResponse Handover, Guid ItemId)> PrepareDraftAsync(HttpClient client, Guid equipmentId, string deliveryMethod)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        return await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipmentId, photoCount: 0, deliveryMethod: deliveryMethod);
    }

    private static async Task<HttpResponseMessage> PostGenerateAsync(HttpClient client, Guid handoverId)
    {
        var csrf = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync<object?>($"/api/equipment-handovers/{handoverId}/responsibility-terms", null, csrf);
    }

    private static async Task<ResponsibilityTermResponse> GenerateTermAsync(HttpClient client, Guid handoverId)
    {
        var response = await PostGenerateAsync(client, handoverId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task<IReadOnlyList<ResponsibilityTermResponse>> ListTermsAsync(HttpClient client, Guid handoverId) =>
        (await client.GetFromJsonAsync<List<ResponsibilityTermResponse>>(
            $"/api/equipment-handovers/{handoverId}/responsibility-terms", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<byte[]> DownloadAsync(HttpClient client, Guid termId)
    {
        var response = await client.GetAsync($"/api/responsibility-terms/{termId}/content");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<Guid> ActiveDispatchIdAsync(Guid handoverId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.EquipmentDispatches.Where(d => d.EquipmentHandoverId == handoverId && d.CancelledAtUtc == null).Select(d => d.Id).SingleAsync();
    }

    private async Task<int> CountCancellationsAsync(Guid handoverId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await dbContext.EquipmentDispatches.CountAsync(d => d.EquipmentHandoverId == handoverId && d.CancelledAtUtc != null);
        var audits = await dbContext.AuditLogs.CountAsync(a => a.Action == AuditActions.EquipmentDispatch.Cancelled && a.EntityId == handoverId.ToString());
        Assert.Equal(rows, audits);
        return rows;
    }

    private async Task<int> CountActiveAssignmentsAsync(Guid equipmentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.EquipmentAssignments.CountAsync(a => a.EquipmentId == equipmentId && a.Status == AssignmentStatus.Active);
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
