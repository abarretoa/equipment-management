using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.ResponsibilityTerms;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.Infrastructure.Persistence.Migrations;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.ResponsibilityTerms;

/// <summary>
/// Every Responsibility Term version owns the exact frozen snapshot it was
/// rendered from: Consultant, allocation, one equipment snapshot per item and
/// the exact photo set per item. A later version, a cancellation, a
/// confirmation or live edits never change it. Legacy versions are only
/// given a snapshot where the old shared model provably still holds it.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class TermSnapshotPerVersionTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task FirstGeneration_OwnsConsultantAllocationItemsAndExactPhotoSets_ForAMixedTerm()
    {
        using var admin = await LoginAsync("Administration");
        var draft = await CreateDispatchedCarrierAsync(admin, laptops: 1, phones: 1, generics: 1);
        var consultant = await GetConsultantAsync(admin, draft.ConsultantId);

        var v1 = await GenerateAsync(admin, draft.HandoverId);
        var (term, items) = await TermSnapshotTestData.GetAsync(factory, v1.Id);

        Assert.False(term.SnapshotUnavailable);
        Assert.Equal(consultant.FullName, term.ConsultantSnapshot!.FullName);
        Assert.Equal(consultant.Nif, term.ConsultantSnapshot.Nif);
        Assert.Equal("Backend Developer", term.AllocationSnapshot!.RoleOrFunction);
        Assert.Equal(3, items.Count);
        Assert.Equal([0, 1, 2], items.Select(i => i.SortOrder));
        foreach (var id in draft.LaptopIds.Concat(draft.PhoneIds))
        {
            Assert.Equal(await CurrentPhotoSetIdAsync(id), items.Single(i => i.EquipmentId == id).EquipmentPhotoSetId);
        }

        Assert.Null(items.Single(i => i.EquipmentId == draft.GenericIds[0]).EquipmentPhotoSetId);
        Assert.Equal("Laptop", items.Single(i => i.EquipmentId == draft.LaptopIds[0]).EquipmentSnapshot.Kind);
        Assert.NotNull(items.Single(i => i.EquipmentId == draft.PhoneIds[0]).EquipmentSnapshot.Imei);
    }

    [Fact]
    public async Task Regeneration_GivesV2ItsOwnSnapshot_AndNeverTouchesV1_EvenWhenLiveDataAndPhotosChanged()
    {
        using var admin = await LoginAsync("Administration");
        var draft = await CreateDispatchedCarrierAsync(admin, laptops: 1, phones: 1, generics: 0);
        var laptopId = draft.LaptopIds[0];
        var v1 = await GenerateAsync(admin, draft.HandoverId);
        var (v1Before, v1ItemsBefore) = await TermSnapshotTestData.GetAsync(factory, v1.Id);
        var photoSetA = v1ItemsBefore.Single(i => i.EquipmentId == laptopId).EquipmentPhotoSetId;

        // Cancel, so the item is Available again and its photos may be corrected.
        (await CancelDispatchAsync(admin, draft.HandoverId)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await ReplaceFirstPhotoAsync(admin, laptopId, photoSetA!.Value)).StatusCode);
        var photoSetB = await CurrentPhotoSetIdAsync(laptopId);
        Assert.NotEqual(photoSetA, photoSetB);

        // Legitimate live edits: Consultant, allocation, equipment.
        await RenameConsultantAsync(admin, draft.ConsultantId, "Consultor Renomeado Sintético");
        await SetAllocationRoleAsync(draft.ConsultantId, "Arquiteto Sintético");
        await SetLaptopModelAsync(admin, laptopId, "Modelo Alterado Sintético");

        (await Phase4TestData.RegisterDispatchAsync(admin, draft.HandoverId)).EnsureSuccessStatusCode();
        var v2 = await GenerateAsync(admin, draft.HandoverId);
        Assert.Equal(2, v2.Version);

        var (v1After, v1ItemsAfter) = await TermSnapshotTestData.GetAsync(factory, v1.Id);
        var (v2Term, v2Items) = await TermSnapshotTestData.GetAsync(factory, v2.Id);

        // v1: exactly as before, including PhotoSet A.
        Assert.Equal(ResponsibilityTermStatus.Superseded, v1After.Status);
        Assert.Equal(v1Before.ConsultantSnapshot!.FullName, v1After.ConsultantSnapshot!.FullName);
        Assert.Equal("Backend Developer", v1After.AllocationSnapshot!.RoleOrFunction);
        Assert.Equal(Snapshot(v1ItemsBefore), Snapshot(v1ItemsAfter));
        Assert.Equal(photoSetA, v1ItemsAfter.Single(i => i.EquipmentId == laptopId).EquipmentPhotoSetId);
        Assert.Equal(v1.Sha256, v1After.Sha256);

        // v2: the then-current data, including PhotoSet B; separate rows.
        Assert.Equal("Consultor Renomeado Sintético", v2Term.ConsultantSnapshot!.FullName);
        Assert.Equal("Arquiteto Sintético", v2Term.AllocationSnapshot!.RoleOrFunction);
        Assert.Equal("Modelo Alterado Sintético", v2Items.Single(i => i.EquipmentId == laptopId).EquipmentSnapshot.Model);
        Assert.Equal(photoSetB, v2Items.Single(i => i.EquipmentId == laptopId).EquipmentPhotoSetId);
        Assert.Empty(v2Items.Select(i => i.Id).Intersect(v1ItemsAfter.Select(i => i.Id)));

        // PhotoSet A itself is kept (Historical), with its photos.
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await dbContext.EquipmentPhotoSets.AnyAsync(s => s.Id == photoSetA));
        Assert.Equal(3, await dbContext.EquipmentPhotoSetPhotos.CountAsync(p => p.EquipmentPhotoSetId == photoSetA));
    }

    [Fact]
    public async Task PreDeliveryRegenerationWithoutCancellation_KeepsV1Intact()
    {
        using var admin = await LoginAsync("Administration");
        var draft = await CreateDispatchedCarrierAsync(admin, laptops: 1, phones: 0, generics: 1);
        var v1 = await GenerateAsync(admin, draft.HandoverId);
        var (v1Before, v1ItemsBefore) = await TermSnapshotTestData.GetAsync(factory, v1.Id);

        await RenameConsultantAsync(admin, draft.ConsultantId, "Outro Nome Sintético");
        var v2 = await GenerateAsync(admin, draft.HandoverId);

        var (v1After, v1ItemsAfter) = await TermSnapshotTestData.GetAsync(factory, v1.Id);
        Assert.Equal(v1Before.ConsultantSnapshot!.FullName, v1After.ConsultantSnapshot!.FullName);
        Assert.Equal(Snapshot(v1ItemsBefore), Snapshot(v1ItemsAfter));
        Assert.Equal("Outro Nome Sintético", (await TermSnapshotTestData.GetAsync(factory, v2.Id)).Term.ConsultantSnapshot!.FullName);
    }

    [Fact]
    public async Task ConfirmarEntrega_LeavesTheCurrentVersionSnapshotUnchanged_AndCopiesItIntoTheDeliveryRecord()
    {
        using var admin = await LoginAsync("Administration");
        var draft = await CreateDispatchedCarrierAsync(admin, laptops: 1, phones: 1, generics: 0);
        var v1 = await GenerateAsync(admin, draft.HandoverId);
        var (before, itemsBefore) = await TermSnapshotTestData.GetAsync(factory, v1.Id);
        Assert.Null((await GetHandoverAsync(admin, draft.HandoverId)).ConsultantSnapshot);

        await RenameConsultantAsync(admin, draft.ConsultantId, "Renomeado Antes Da Receção");
        (await Phase4TestData.FinalizeAsync(admin, draft.HandoverId)).EnsureSuccessStatusCode();

        var (after, itemsAfter) = await TermSnapshotTestData.GetAsync(factory, v1.Id);
        Assert.Equal(ResponsibilityTermStatus.Generated, after.Status);
        Assert.Equal(before.ConsultantSnapshot!.FullName, after.ConsultantSnapshot!.FullName);
        Assert.Equal(Snapshot(itemsBefore), Snapshot(itemsAfter));
        Assert.Equal(v1.Sha256, after.Sha256);
        Assert.Single(await ListTermsAsync(admin, draft.HandoverId));

        var delivered = await GetHandoverAsync(admin, draft.HandoverId);
        Assert.Equal(before.ConsultantSnapshot.FullName, delivered.ConsultantSnapshot!.FullName);
        Assert.Equal(
            itemsBefore.Select(i => (i.EquipmentId, i.EquipmentSnapshot.AssetTag)).OrderBy(x => x.EquipmentId),
            delivered.Items.Select(i => (i.EquipmentId, i.EquipmentSnapshot!.AssetTag)).OrderBy(x => x.EquipmentId));
    }

    [Fact]
    public async Task ALargeTerm_HasOneSnapshotRowPerItem()
    {
        using var admin = await LoginAsync("Administration");
        var draft = await CreateDispatchedCarrierAsync(admin, laptops: 2, phones: 2, generics: 2);

        var v1 = await GenerateAsync(admin, draft.HandoverId);
        var (_, items) = await TermSnapshotTestData.GetAsync(factory, v1.Id);

        Assert.Equal(6, items.Count);
        Assert.Equal(Enumerable.Range(0, 6), items.Select(i => i.SortOrder));
        Assert.Equal(6, items.Select(i => i.EquipmentId).Distinct().Count());
        Assert.Equal(4, items.Count(i => i.EquipmentPhotoSetId is not null));
    }

    [Fact]
    public async Task InPerson_PostDeliveryVersions_CopyTheDeliveryRecord_AndSendSignCompleteKeepEveryVersion()
    {
        using var admin = await LoginAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(admin, kind: "Laptop", assetTagPrefix: "LP");
        var laptop = await Phase3TestData.CreateLaptopEquipmentAsync(admin, type.Id);
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(admin);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(admin, consultantId, laptop.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(admin, handover.Id)).EnsureSuccessStatusCode();
        var delivered = await GetHandoverAsync(admin, handover.Id);

        var v1 = await GenerateAsync(admin, handover.Id);
        var v2 = await GenerateAsync(admin, handover.Id);
        var (t1, i1) = await TermSnapshotTestData.GetAsync(factory, v1.Id);
        var (t2, i2) = await TermSnapshotTestData.GetAsync(factory, v2.Id);

        Assert.Equal(delivered.ConsultantSnapshot!.FullName, t1.ConsultantSnapshot!.FullName);
        Assert.Equal(delivered.ConsultantSnapshot.FullName, t2.ConsultantSnapshot!.FullName);
        Assert.Equal(Snapshot(i1), Snapshot(i2));
        Assert.NotEqual(i1.Single().Id, i2.Single().Id);

        // A superseded version can be neither sent nor signed; the current one completes.
        Assert.Equal(HttpStatusCode.Conflict, (await Phase7TestData.SendAsync(admin, v1.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Phase7TestData.SendAsync(admin, v2.Id)).StatusCode);
        Assert.Contains(GetEmailSender().SentMessages, m => m.ToAddress == t2.ConsultantSnapshot.Email && m.Attachment is not null);
        (await Phase7TestData.UploadSignedDocumentAsync(admin, v2.Id)).EnsureSuccessStatusCode();
        Assert.Equal(HandoverOperationalStatus.Completed, (await GetHandoverAsync(admin, handover.Id)).OperationalStatus);

        Assert.NotNull((await TermSnapshotTestData.GetAsync(factory, v1.Id)).Term.ConsultantSnapshot);
        Assert.Single((await TermSnapshotTestData.GetAsync(factory, v2.Id)).Items);
    }

    // --- Legacy backfill (the migration's exact SQL) -------------------------

    [Fact]
    public async Task Backfill_RecoversEveryPostDeliveryVersion_FromTheImmutableDeliveryRecord()
    {
        using var admin = await LoginAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(admin, kind: "Laptop", assetTagPrefix: "LP");
        var laptop = await Phase3TestData.CreateLaptopEquipmentAsync(admin, type.Id);
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(admin);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(admin, consultantId, laptop.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(admin, handover.Id)).EnsureSuccessStatusCode();
        var v1 = await GenerateAsync(admin, handover.Id);
        var v2 = await GenerateAsync(admin, handover.Id);
        var deliveredPhotoSet = await DeliveryRecordPhotoSetIdAsync(handover.Id, laptop.Id);

        await MakeLegacyAsync(v1.Id, v2.Id);
        await RunBackfillAsync();

        foreach (var id in new[] { v1.Id, v2.Id })
        {
            var (term, items) = await TermSnapshotTestData.GetAsync(factory, id);
            Assert.False(term.SnapshotUnavailable);
            Assert.NotNull(term.ConsultantSnapshot);
            Assert.Equal(deliveredPhotoSet, Assert.Single(items).EquipmentPhotoSetId);
        }
    }

    [Fact]
    public async Task Backfill_RecoversOnlyTheLatestPreparedVersion_NeverFabricatesTheOlderOne()
    {
        using var admin = await LoginAsync("Administration");
        var draft = await CreateDispatchedCarrierAsync(admin, laptops: 1, phones: 0, generics: 0);
        var v1 = await GenerateAsync(admin, draft.HandoverId);
        await RenameConsultantAsync(admin, draft.ConsultantId, "Nome Da Segunda Versão");
        var v2 = await GenerateAsync(admin, draft.HandoverId);
        var (v2Term, v2Items) = await TermSnapshotTestData.GetAsync(factory, v2.Id);

        // Old model: the Draft's shared fields held the LATEST preparation only.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var h = await dbContext.EquipmentHandovers.SingleAsync(x => x.Id == draft.HandoverId);
            h.ConsultantSnapshot = new ConsultantSnapshot
            {
                FullName = v2Term.ConsultantSnapshot!.FullName, Nif = v2Term.ConsultantSnapshot.Nif, Address = v2Term.ConsultantSnapshot.Address,
                Phone = v2Term.ConsultantSnapshot.Phone, Email = v2Term.ConsultantSnapshot.Email,
            };
            var item = await dbContext.EquipmentHandoverItems.SingleAsync(x => x.EquipmentHandoverId == draft.HandoverId);
            item.EquipmentSnapshot = new EquipmentSnapshot { AssetTag = v2Items[0].EquipmentSnapshot.AssetTag, EquipmentTypeName = v2Items[0].EquipmentSnapshot.EquipmentTypeName, Kind = "Laptop" };
            item.EquipmentPhotoSetId = v2Items[0].EquipmentPhotoSetId;
            await dbContext.SaveChangesAsync();
        }

        await MakeLegacyAsync(v1.Id, v2.Id);
        await RunBackfillAsync();

        var (v1Term, v1Items) = await TermSnapshotTestData.GetAsync(factory, v1.Id);
        Assert.True(v1Term.SnapshotUnavailable);
        Assert.Null(v1Term.ConsultantSnapshot);
        Assert.Empty(v1Items);

        var (v2After, v2ItemsAfter) = await TermSnapshotTestData.GetAsync(factory, v2.Id);
        Assert.False(v2After.SnapshotUnavailable);
        Assert.Equal("Nome Da Segunda Versão", v2After.ConsultantSnapshot!.FullName);
        Assert.Equal(v2Items[0].EquipmentPhotoSetId, Assert.Single(v2ItemsAfter).EquipmentPhotoSetId);

        // The Draft's stale preparation copy is gone; the version owns it now.
        Assert.Null((await GetHandoverAsync(admin, draft.HandoverId)).ConsultantSnapshot);

        // An unrecoverable version stays fully usable as a document.
        var download = await admin.GetAsync($"/api/responsibility-terms/{v1.Id}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(2, (await ListTermsAsync(admin, draft.HandoverId)).Count);
    }

    [Fact]
    public async Task Backfill_LeavesAVersionCancelledByAnularEnvio_Unrecovered()
    {
        using var admin = await LoginAsync("Administration");
        var draft = await CreateDispatchedCarrierAsync(admin, laptops: 0, phones: 0, generics: 1);
        var v1 = await GenerateAsync(admin, draft.HandoverId);
        (await CancelDispatchAsync(admin, draft.HandoverId)).EnsureSuccessStatusCode();

        await MakeLegacyAsync(v1.Id);
        await RunBackfillAsync();

        var (term, items) = await TermSnapshotTestData.GetAsync(factory, v1.Id);
        Assert.True(term.SnapshotUnavailable);
        Assert.Empty(items);
    }

    // --- Setup ---------------------------------------------------------------

    private sealed record CarrierDraft(Guid HandoverId, Guid ConsultantId, List<Guid> LaptopIds, List<Guid> PhoneIds, List<Guid> GenericIds);

    private static async Task<CarrierDraft> CreateDispatchedCarrierAsync(HttpClient client, int laptops, int phones, int generics)
    {
        var ids = new List<(Guid Id, string Kind)>();
        var laptopType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LP");
        var phoneType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var genericType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        for (var i = 0; i < laptops; i++) ids.Add(((await Phase3TestData.CreateLaptopEquipmentAsync(client, laptopType.Id)).Id, "Laptop"));
        for (var i = 0; i < phones; i++) ids.Add(((await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, phoneType.Id)).Id, "Phone"));
        for (var i = 0; i < generics; i++) ids.Add(((await Phase3TestData.CreateGenericEquipmentAsync(client, genericType.Id)).Id, "Generic"));

        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, ids[0].Id, photoCount: 0, deliveryMethod: "Carrier");
        foreach (var (id, _) in ids.Skip(1))
        {
            var add = await Phase4TestData.AddItemAsync(client, handover.Id, id);
            add.EnsureSuccessStatusCode();
            var item = (await add.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!.Items.Single(x => x.EquipmentId == id);
            (await Phase4TestData.RecordInspectionAsync(client, handover.Id, item.Id)).EnsureSuccessStatusCode();
        }

        (await Phase4TestData.RegisterDispatchAsync(client, handover.Id)).EnsureSuccessStatusCode();
        return new CarrierDraft(handover.Id, consultantId,
            ids.Where(x => x.Kind == "Laptop").Select(x => x.Id).ToList(),
            ids.Where(x => x.Kind == "Phone").Select(x => x.Id).ToList(),
            ids.Where(x => x.Kind == "Generic").Select(x => x.Id).ToList());
    }

    /// <summary>Puts versions back into the pre-migration shape: no own snapshot, no item rows, marked unavailable.</summary>
    private async Task MakeLegacyAsync(params Guid[] termIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.ResponsibilityTermItems.Where(i => termIds.Contains(i.ResponsibilityTermId)).ExecuteDeleteAsync();
        foreach (var id in termIds)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "ResponsibilityTerms" SET "SnapshotUnavailable" = true,
                    "Snapshot_ConsultantFullName" = NULL, "Snapshot_ConsultantNif" = NULL, "Snapshot_ConsultantAddress" = NULL,
                    "Snapshot_ConsultantPhone" = NULL, "Snapshot_ConsultantEmail" = NULL, "Snapshot_WorkforceType" = NULL,
                    "Snapshot_EngagementType" = NULL, "Snapshot_RoleOrFunction" = NULL, "Snapshot_CompanyName" = NULL
                WHERE "Id" = {id}
                """);
        }
    }

    private async Task RunBackfillAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await dbContext.Database.ExecuteSqlRawAsync(PersistResponsibilityTermSnapshotsPerVersion.BackfillSql);
        await transaction.CommitAsync();
    }

    private static List<(Guid, string, string?, string?, Guid?)> Snapshot(IEnumerable<ResponsibilityTermItem> items) =>
        items.OrderBy(i => i.SortOrder).Select(i => (i.EquipmentId, i.EquipmentSnapshot.AssetTag, i.EquipmentSnapshot.Model, i.EquipmentSnapshot.Processor, i.EquipmentPhotoSetId)).ToList();

    private async Task<Guid?> CurrentPhotoSetIdAsync(Guid equipmentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.EquipmentPhotoSets.Where(s => s.EquipmentId == equipmentId && s.Status == Domain.Equipments.PhotoSetStatus.Current).Select(s => (Guid?)s.Id).SingleOrDefaultAsync();
    }

    private async Task<Guid?> DeliveryRecordPhotoSetIdAsync(Guid handoverId, Guid equipmentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.EquipmentHandoverItems.Where(i => i.EquipmentHandoverId == handoverId && i.EquipmentId == equipmentId).Select(i => i.EquipmentPhotoSetId).SingleAsync();
    }

    private static async Task<ResponsibilityTermResponse> GenerateAsync(HttpClient client, Guid handoverId)
    {
        var csrf = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync<object?>($"/api/equipment-handovers/{handoverId}/responsibility-terms", null, csrf);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task<HttpResponseMessage> CancelDispatchAsync(HttpClient client, Guid handoverId)
    {
        var csrf = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync($"/api/equipment-handovers/{handoverId}/dispatch/cancel", new { Reason = "Ensaio sintético" }, csrf);
    }

    private static async Task<HttpResponseMessage> ReplaceFirstPhotoAsync(HttpClient client, Guid equipmentId, Guid expectedSetId)
    {
        var csrf = await client.GetAntiforgeryTokenAsync();
        using var content = new MultipartFormDataContent { { new StringContent(expectedSetId.ToString()), "ExpectedPhotoSetId" } };
        var file = new ByteArrayContent([.. Phase3TestData.MinimalJpegBytes, 7, 7]);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "Photo1", "replacement-1.jpg");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/equipment/{equipmentId}/current-photos/replacement") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static async Task RenameConsultantAsync(HttpClient client, Guid consultantId, string fullName)
    {
        var current = await GetConsultantAsync(client, consultantId);
        var csrf = await client.GetAntiforgeryTokenAsync();
        var response = await client.PutWithCsrfAsync($"/api/consultants/{consultantId}", new { FullName = fullName, current.Nif, current.Email, current.Address, current.Phone }, csrf);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task SetAllocationRoleAsync(Guid consultantId, string role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var allocation = await dbContext.ConsultantAllocations.FirstAsync(a => a.ConsultantId == consultantId);
        allocation.RoleOrFunction = role;
        await dbContext.SaveChangesAsync();
    }

    private static async Task SetLaptopModelAsync(HttpClient client, Guid equipmentId, string model)
    {
        var current = (await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipmentId}", HttpClientTestExtensions.JsonOptions))!;
        var csrf = await client.GetAntiforgeryTokenAsync();
        var response = await client.PutWithCsrfAsync($"/api/equipment/{equipmentId}", new
        {
            current.Manufacturer, Model = model, current.SerialNumber, Notes = (string?)null,
            LaptopSpecification = new { Processor = "Intel i9", Memory = "64 GB", Storage = "2 TB SSD" },
        }, csrf);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<ConsultantDetailResponse> GetConsultantAsync(HttpClient client, Guid consultantId) =>
        (await client.GetFromJsonAsync<ConsultantDetailResponse>($"/api/consultants/{consultantId}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<EquipmentHandoverDetailResponse> GetHandoverAsync(HttpClient client, Guid handoverId) =>
        (await client.GetFromJsonAsync<EquipmentHandoverDetailResponse>($"/api/equipment-handovers/{handoverId}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<IReadOnlyList<ResponsibilityTermResponse>> ListTermsAsync(HttpClient client, Guid handoverId) =>
        (await client.GetFromJsonAsync<List<ResponsibilityTermResponse>>(
            $"/api/equipment-handovers/{handoverId}/responsibility-terms", HttpClientTestExtensions.JsonOptions))!;

    private EquipmentManagement.Infrastructure.Email.RecordingEmailSender GetEmailSender() =>
        (EquipmentManagement.Infrastructure.Email.RecordingEmailSender)factory.Services.GetRequiredService<EquipmentManagement.Infrastructure.Email.IEmailSender>();

    private async Task<HttpClient> LoginAsync(string role)
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, role);
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
