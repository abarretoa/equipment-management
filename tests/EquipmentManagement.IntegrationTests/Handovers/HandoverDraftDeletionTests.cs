using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Files;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Handovers;

/// <summary>
/// "Eliminar rascunho": an abandoned Draft may be deleted only before its
/// operational lock (<c>HandoverRules.CanDeleteDraft</c>) — never after
/// "Registar envio" or "Confirmar entrega", never with custody or a
/// Responsibility Term attached — and deleting it never touches Equipment.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class HandoverDraftDeletionTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Delete_EmptyDraft_Succeeds_AndTheDraftIsGone()
    {
        using var client = await LoginAsync("Administration");
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var handover = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId);

        var response = await DeleteAsync(client, handover.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/equipment-handovers/{handover.Id}")).StatusCode);
        var list = await client.GetFromJsonAsync<PagedResponse<EquipmentHandoverSummaryResponse>>(
            $"/api/equipment-handovers?consultantId={consultantId}&pageSize=100", HttpClientTestExtensions.JsonOptions);
        Assert.DoesNotContain(list!.Items, h => h.Id == handover.Id);
    }

    [Fact]
    public async Task Delete_DraftWithEquipment_Succeeds_EquipmentStaysAvailable_AndNoAssignmentExists()
    {
        using var client = await LoginAsync("HR");
        var (handoverId, equipmentId, _) = await CreateDraftWithInspectedItemAsync(client, photoCount: 0, recordInspection: false);

        var response = await DeleteAsync(client, handoverId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var equipment = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal(EquipmentStatus.Available, equipment.Status);
        Assert.Null(equipment.CurrentCustody);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await dbContext.EquipmentAssignments.AnyAsync(a => a.EquipmentId == equipmentId));
        Assert.False(await dbContext.EquipmentHandoverItems.AnyAsync(i => i.EquipmentHandoverId == handoverId));
    }

    [Fact]
    public async Task Delete_DraftWithSavedInspectionAndPhotos_RemovesDraftOnlyRowsAndFiles_AndLeavesEquipmentUntouched()
    {
        using var client = await LoginAsync("Administration");
        var (handoverId, equipmentId, itemId) = await CreateDraftWithInspectedItemAsync(client, photoCount: 2, recordInspection: true);
        var before = await GetEquipmentAsync(client, equipmentId);

        List<string> photoKeys;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            photoKeys = await (
                from p in dbContext.DeliveryInspectionPhotos
                join i in dbContext.DeliveryInspections on p.DeliveryInspectionId equals i.Id
                where i.EquipmentHandoverItemId == itemId
                select p.StorageKey).ToListAsync();
        }

        Assert.Equal(2, photoKeys.Count);

        var response = await DeleteAsync(client, handoverId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await dbContext.EquipmentHandovers.AnyAsync(h => h.Id == handoverId));
            Assert.False(await dbContext.DeliveryInspections.AnyAsync(i => i.EquipmentHandoverItemId == itemId));
            Assert.False(await dbContext.EquipmentAssignments.AnyAsync(a => a.EquipmentId == equipmentId));
            Assert.False(await dbContext.EquipmentReturns.AnyAsync(r => r.EquipmentId == equipmentId));
        }

        var storage = factory.Services.GetRequiredService<IPrivateFileStorage>();
        foreach (var key in photoKeys)
        {
            Assert.Null(await storage.OpenReadAsync(key));
        }

        var after = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal(EquipmentStatus.Available, after.Status);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
    }

    [Fact]
    public async Task Delete_ThenTheSameEquipmentCanJoinANewDraft_AndAnotherDraftWithItIsUnaffected()
    {
        using var client = await LoginAsync("Administration");
        var (handoverId, equipmentId, _) = await CreateDraftWithInspectedItemAsync(client, photoCount: 0, recordInspection: true);
        var (otherConsultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var otherDraft = await Phase4TestData.CreateDraftHandoverAsync(client, otherConsultantId);
        (await Phase4TestData.AddItemAsync(client, otherDraft.Id, equipmentId)).EnsureSuccessStatusCode();

        (await DeleteAsync(client, handoverId)).EnsureSuccessStatusCode();

        var other = await client.GetFromJsonAsync<EquipmentHandoverDetailResponse>(
            $"/api/equipment-handovers/{otherDraft.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(equipmentId, Assert.Single(other!.Items).EquipmentId);

        var (thirdConsultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var newDraft = await Phase4TestData.CreateDraftHandoverAsync(client, thirdConsultantId);
        Assert.Equal(HttpStatusCode.OK, (await Phase4TestData.AddItemAsync(client, newDraft.Id, equipmentId)).StatusCode);
    }

    [Fact]
    public async Task Delete_CarrierDraftAfterDispatch_IsRejected_AndNothingChanges()
    {
        using var client = await LoginAsync("Administration");
        var (handoverId, equipmentId, _) = await CreateDraftWithInspectedItemAsync(client, photoCount: 0, recordInspection: true, deliveryMethod: "Carrier");
        (await Phase4TestData.RegisterDispatchAsync(client, handoverId)).EnsureSuccessStatusCode();

        var response = await DeleteAsync(client, handoverId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var detail = await client.GetFromJsonAsync<EquipmentHandoverDetailResponse>(
            $"/api/equipment-handovers/{handoverId}", HttpClientTestExtensions.JsonOptions);
        Assert.NotNull(detail!.Dispatch);
        Assert.Single(detail.Items);
        Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(client, equipmentId)).Status);
    }

    [Fact]
    public async Task Delete_CarrierDraftBeforeDispatch_Succeeds()
    {
        using var client = await LoginAsync("Administration");
        var (handoverId, _, _) = await CreateDraftWithInspectedItemAsync(client, photoCount: 0, recordInspection: true, deliveryMethod: "Carrier");

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(client, handoverId)).StatusCode);
    }

    [Fact]
    public async Task Delete_PhysicallyFinalizedHandover_IsRejected_AndCustodyIsKept()
    {
        using var client = await LoginAsync("Administration");
        var (handoverId, equipmentId, _) = await CreateDraftWithInspectedItemAsync(client, photoCount: 0, recordInspection: true);
        (await Phase4TestData.FinalizeAsync(client, handoverId)).EnsureSuccessStatusCode();

        var response = await DeleteAsync(client, handoverId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var equipment = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal(EquipmentStatus.Assigned, equipment.Status);
        Assert.NotNull(equipment.CurrentCustody);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/equipment-handovers/{handoverId}")).StatusCode);
    }

    [Fact]
    public async Task Delete_UnknownHandover_Returns404_AndASecondDeleteToo()
    {
        using var client = await LoginAsync("Administration");
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteAsync(client, Guid.NewGuid())).StatusCode);

        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var handover = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId);
        (await DeleteAsync(client, handover.Id)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteAsync(client, handover.Id)).StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutAuthentication_WithoutCsrf_OrWithoutPermission_IsRejected()
    {
        using var adminClient = await LoginAsync("Administration");
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(adminClient);
        var handover = await Phase4TestData.CreateDraftHandoverAsync(adminClient, consultantId);

        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/equipment-handovers/{handover.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.DeleteAsync($"/api/equipment-handovers/{handover.Id}")).StatusCode);

        // A signed-in user holding no role has no EquipmentHandover.Update.
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "HR");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.True((await userManager.RemoveFromRoleAsync(user!, "HR")).Succeeded);
        }

        using var noRoleClient = factory.CreateClient();
        await noRoleClient.LoginAsync(email, password);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync(noRoleClient, handover.Id)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"/api/equipment-handovers/{handover.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_IsAudited_WithoutConsultantPii()
    {
        using var client = await LoginAsync("Administration");
        var (handoverId, equipmentId, _) = await CreateDraftWithInspectedItemAsync(client, photoCount: 0, recordInspection: true);
        var detail = await client.GetFromJsonAsync<EquipmentHandoverDetailResponse>(
            $"/api/equipment-handovers/{handoverId}", HttpClientTestExtensions.JsonOptions);
        var consultant = await client.GetFromJsonAsync<ConsultantDetailResponse>(
            $"/api/consultants/{detail!.ConsultantId}", HttpClientTestExtensions.JsonOptions);
        var assetTag = (await GetEquipmentAsync(client, equipmentId)).AssetTag;

        (await DeleteAsync(client, handoverId)).EnsureSuccessStatusCode();

        var page = await client.GetFromJsonAsync<PagedResponse<AuditLogResponse>>(
            $"/api/audit?entityId={handoverId}&page=1&pageSize=100", HttpClientTestExtensions.JsonOptions);
        var entry = Assert.Single(page!.Items, e => e.Action == AuditActions.EquipmentHandover.DraftDeleted);
        Assert.Equal("EquipmentHandover", entry.EntityType);
        Assert.Equal(handoverId.ToString(), entry.EntityId);

        var json = (entry.OldValuesJson ?? string.Empty) + (entry.NewValuesJson ?? string.Empty) + (entry.MetadataJson ?? string.Empty);
        Assert.Contains(assetTag, json);
        Assert.Contains(detail.ConsultantId.ToString(), json);
        Assert.DoesNotContain(consultant!.FullName, json);
        Assert.DoesNotContain(consultant.Email, json);
        Assert.DoesNotContain(consultant.Nif, json);
        Assert.DoesNotContain(consultant.Phone, json);
    }

    /// <summary>The UI showed the Draft as deletable, but a dispatch landed in between (simulated by writing the row directly): the server re-checks and refuses.</summary>
    [Fact]
    public async Task Delete_FailsClosed_WhenADispatchAppearedAfterThePageLoaded()
    {
        using var client = await LoginAsync("Administration");
        var (handoverId, _, _) = await CreateDraftWithInspectedItemAsync(client, photoCount: 0, recordInspection: true, deliveryMethod: "Carrier");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.EquipmentDispatches.Add(new EquipmentDispatch
            {
                Id = Guid.NewGuid(),
                EquipmentHandoverId = handoverId,
                ExpectedDeliveryFromDate = new DateOnly(2026, 10, 20),
                DispatchedAtUtc = DateTime.UtcNow,
                DispatchedByUserId = Guid.NewGuid(),
                DispatchedByDisplayNameSnapshot = "Concurrent",
            });
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await DeleteAsync(client, handoverId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/equipment-handovers/{handoverId}")).StatusCode);
    }

    /// <summary>Legacy/unexpected data: a nominal Draft carrying a Responsibility Term. Its immutable artifact is never deleted with the Draft.</summary>
    [Fact]
    public async Task Delete_FailsClosed_WhenANominalDraftCarriesAResponsibilityTerm()
    {
        using var client = await LoginAsync("Administration");
        var (handoverId, _, _) = await CreateDraftWithInspectedItemAsync(client, photoCount: 0, recordInspection: true);
        var termId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var actorId = await dbContext.Users.Select(u => u.Id).FirstAsync();
            dbContext.ResponsibilityTerms.Add(new ResponsibilityTerm
            {
                Id = termId,
                EquipmentHandoverId = handoverId,
                DocumentNumber = $"TRL-{Guid.NewGuid():N}"[..20],
                Version = 1,
                Status = ResponsibilityTermStatus.Generated,
                TemplateVersion = 2,
                GeneratedAtUtc = DateTime.UtcNow,
                GeneratedByUserId = actorId,
                GeneratedByDisplayNameSnapshot = "Legacy",
                SnapshotUnavailable = true,
                StorageKey = $"responsibility-terms/legacy/{Guid.NewGuid():N}.pdf",
                FileName = "legacy.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1,
                Sha256 = new string('0', 64),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            });
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await DeleteAsync(client, handoverId)).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.True(await dbContext.ResponsibilityTerms.AnyAsync(t => t.Id == termId));
            Assert.True(await dbContext.EquipmentHandovers.AnyAsync(h => h.Id == handoverId));
        }
    }

    /// <summary>
    /// Real concurrency: "Eliminar rascunho" and "Registar envio" race on the
    /// same carrier Draft. Whatever the interleaving, both can never succeed,
    /// and the surviving state is consistent with the one that did.
    /// </summary>
    [Fact]
    public async Task Delete_RacingDispatch_NeverBothSucceed()
    {
        using var client = await LoginAsync("Administration");
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var (handoverId, _, _) = await CreateDraftWithInspectedItemAsync(client, photoCount: 0, recordInspection: true, deliveryMethod: "Carrier");
            using var otherClient = await LoginAsync("Administration");

            var deleteTask = DeleteAsync(client, handoverId);
            var dispatchTask = Phase4TestData.RegisterDispatchAsync(otherClient, handoverId);
            await Task.WhenAll(deleteTask, dispatchTask);
            var deleted = (await deleteTask).IsSuccessStatusCode;
            var dispatched = (await dispatchTask).IsSuccessStatusCode;

            Assert.False(deleted && dispatched, $"attempt {attempt}: both the delete and the dispatch succeeded");
            // Losing the race is a clean conflict (or, for a dispatch that
            // arrives after the delete committed, a plain 404) — never an
            // unhandled 500, on either side.
            Assert.Contains((await deleteTask).StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict });
            Assert.Contains((await dispatchTask).StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.NotFound });

            await using var scope = factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var handoverExists = await dbContext.EquipmentHandovers.AnyAsync(h => h.Id == handoverId);
            var dispatchExists = await dbContext.EquipmentDispatches.AnyAsync(d => d.EquipmentHandoverId == handoverId);
            Assert.Equal(!deleted, handoverExists);
            Assert.Equal(dispatched, dispatchExists);
            Assert.Equal(handoverExists, await dbContext.EquipmentHandoverItems.AnyAsync(i => i.EquipmentHandoverId == handoverId));
        }
    }

    /// <summary>
    /// Ordering A, deterministically: "Registar envio" commits first, so a
    /// delete issued afterwards is a conflict and changes nothing.
    /// </summary>
    [Fact]
    public async Task Race_DispatchWins_ThenTheDeleteIsAConflict()
    {
        using var client = await LoginAsync("HR");
        var (handoverId, equipmentId, _) = await CreateDraftWithInspectedItemAsync(client, photoCount: 0, recordInspection: true, deliveryMethod: "Carrier");

        Assert.Equal(HttpStatusCode.OK, (await Phase4TestData.RegisterDispatchAsync(client, handoverId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await DeleteAsync(client, handoverId)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await dbContext.EquipmentHandovers.AnyAsync(h => h.Id == handoverId));
        Assert.Equal(1, await dbContext.EquipmentDispatches.CountAsync(d => d.EquipmentHandoverId == handoverId));
        Assert.True(await dbContext.EquipmentHandoverItems.AnyAsync(i => i.EquipmentHandoverId == handoverId));
        Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(client, equipmentId)).Status);
    }

    /// <summary>
    /// Ordering B, deterministically: the Draft's deletion is in flight (rows
    /// deleted, transaction still open) while "Registar envio" passes its
    /// checks and reaches its insert, which blocks on the deleted parent row.
    /// Once the deletion commits, the insert fails on the dispatch → handover
    /// foreign key: the dispatch must answer a clean 409, not a 500, and
    /// nothing may be left behind.
    /// </summary>
    [Fact]
    public async Task Race_DeleteWins_ThenTheBlockedDispatchIsAConflict_NotA500()
    {
        using var client = await LoginAsync("HR");
        var (handoverId, equipmentId, _) = await CreateDraftWithInspectedItemAsync(client, photoCount: 0, recordInspection: true, deliveryMethod: "Carrier");

        await using var deletingScope = factory.Services.CreateAsyncScope();
        var deletingContext = deletingScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var deletion = await deletingContext.Database.BeginTransactionAsync();
        // The rows "Eliminar rascunho" removes (inspections cascade with their item).
        await deletingContext.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM ""EquipmentHandoverItems"" WHERE ""EquipmentHandoverId"" = {handoverId}");
        await deletingContext.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM ""EquipmentHandovers"" WHERE ""Id"" = {handoverId}");

        var dispatchTask = Phase4TestData.RegisterDispatchAsync(client, handoverId);
        await WaitUntilADispatchInsertIsBlockedAsync(dispatchTask);
        await deletion.CommitAsync();

        var dispatch = await dispatchTask;

        Assert.Equal(HttpStatusCode.Conflict, dispatch.StatusCode);
        Assert.Contains("já não está disponível para registar o envio", await dispatch.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await dbContext.EquipmentHandovers.AnyAsync(h => h.Id == handoverId));
        Assert.False(await dbContext.EquipmentDispatches.AnyAsync(d => d.EquipmentHandoverId == handoverId));
        Assert.False(await dbContext.EquipmentHandoverItems.AnyAsync(i => i.EquipmentHandoverId == handoverId));
        Assert.False(await dbContext.EquipmentAssignments.AnyAsync(a => a.EquipmentId == equipmentId));
        Assert.Equal(EquipmentStatus.Available, (await GetEquipmentAsync(client, equipmentId)).Status);
    }

    /// <summary>Polls pg_stat_activity until a session is waiting on a lock while inserting into EquipmentDispatches.</summary>
    private async Task WaitUntilADispatchInsertIsBlockedAsync(Task dispatchTask)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            Assert.False(dispatchTask.IsCompleted, "The dispatch finished before reaching its insert; the race was not reproduced.");
            var blocked = await dbContext.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%INSERT INTO "EquipmentDispatches"%'
                """).SingleAsync();
            if (blocked > 0)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("The dispatch insert never blocked on the in-flight deletion.");
    }

    private static async Task<(Guid HandoverId, Guid EquipmentId, Guid ItemId)> CreateDraftWithInspectedItemAsync(
        HttpClient client, int photoCount, bool recordInspection, string deliveryMethod = "InPerson")
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        if (recordInspection)
        {
            var (handover, itemId) = await Phase4TestData.PrepareReadyToFinalizeAsync(
                client, consultantId, equipment.Id, photoCount, deliveryMethod: deliveryMethod);
            return (handover.Id, equipment.Id, itemId);
        }

        var draft = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId, deliveryMethod: deliveryMethod);
        var addResponse = await Phase4TestData.AddItemAsync(client, draft.Id, equipment.Id);
        addResponse.EnsureSuccessStatusCode();
        var withItem = (await addResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        return (draft.Id, equipment.Id, withItem.Items.Single().Id);
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, Guid handoverId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.DeleteWithCsrfAsync($"/api/equipment-handovers/{handoverId}", csrfToken);
    }

    private static async Task<EquipmentDetailResponse> GetEquipmentAsync(HttpClient client, Guid equipmentId) =>
        (await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipmentId}", HttpClientTestExtensions.JsonOptions))!;

    private async Task<HttpClient> LoginAsync(string role)
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, role);
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
