using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.ResponsibilityTerms;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Equipments;

/// <summary>
/// "Alterar fotografias": Administration's correction of an equipment's
/// CURRENT condition photos outside custody and outside a dispatched delivery.
/// The current set is never mutated — it becomes Historical and a complete new
/// set of 3 becomes Current (replaced slots uploaded, kept slots copied to new
/// private objects), keeping the corrected set's origin. Frozen references
/// (Handover items, Responsibility Terms) keep resolving the old set.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class EquipmentPhotoCorrectionTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    // Genuine JPEG signatures with distinct tails, so each slot's bytes are recognizable.
    private static byte[] Jpeg(byte marker) => [.. Phase3TestData.MinimalJpegBytes, marker, marker];

    [Fact]
    public async Task Administration_ReplacesOneSlot_TheResultIsACompleteNewCurrentSet_AndTheOldSetIsPreserved()
    {
        using var client = await LoginAsync("Administration");
        var laptop = await CreateLaptopAsync(client);
        var before = laptop.CurrentPhotoSet!;
        var oldBytes = await DownloadAllAsync(client, laptop.Id, before);

        var response = await ReplaceAsync(client, laptop.Id, before.Id, (2, Jpeg(0x22)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = (await GetAsync(client, laptop.Id)).CurrentPhotoSet!;
        Assert.NotEqual(before.Id, after.Id);
        Assert.Equal(3, after.Photos.Count);
        Assert.Equal([1, 2, 3], after.Photos.Select(p => p.SortOrder));
        Assert.Equal(PhotoSetSource.InitialRegistration, after.Source);

        var newBytes = await DownloadAllAsync(client, laptop.Id, after);
        Assert.Equal(oldBytes[0], newBytes[0]);
        Assert.Equal(Jpeg(0x22), newBytes[1]);
        Assert.Equal(oldBytes[2], newBytes[2]);

        // The old set is history, intact — rows and files.
        Assert.Equal(oldBytes, await DownloadAllAsync(client, laptop.Id, before));
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var oldSet = await dbContext.EquipmentPhotoSets.SingleAsync(s => s.Id == before.Id);
        Assert.Equal(PhotoSetStatus.Historical, oldSet.Status);
        Assert.Equal(3, await dbContext.EquipmentPhotoSetPhotos.CountAsync(p => p.EquipmentPhotoSetId == before.Id));
        Assert.Equal(1, await dbContext.EquipmentPhotoSets.CountAsync(s => s.EquipmentId == laptop.Id && s.Status == PhotoSetStatus.Current));

        // Kept slots are copies, never shared files.
        var oldKeys = await dbContext.EquipmentPhotoSetPhotos.Where(p => p.EquipmentPhotoSetId == before.Id).Select(p => p.StorageKey).ToListAsync();
        var newKeys = await dbContext.EquipmentPhotoSetPhotos.Where(p => p.EquipmentPhotoSetId == after.Id).Select(p => p.StorageKey).ToListAsync();
        Assert.Empty(oldKeys.Intersect(newKeys));
    }

    [Fact]
    public async Task Administration_CanReplaceAllThreeSlots()
    {
        using var client = await LoginAsync("Administration");
        var phone = await CreatePhoneAsync(client);

        var response = await ReplaceAsync(client, phone.Id, phone.CurrentPhotoSet!.Id, (1, Jpeg(0x31)), (2, Jpeg(0x32)), (3, Jpeg(0x33)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = (await GetAsync(client, phone.Id)).CurrentPhotoSet!;
        Assert.Equal([Jpeg(0x31), Jpeg(0x32), Jpeg(0x33)], await DownloadAllAsync(client, phone.Id, after));
    }

    [Fact]
    public async Task DetailFlag_IsTrueForEligibleEquipment_AndFalseInCustodyOrOnADispatchedDelivery()
    {
        using var client = await LoginAsync("Administration");
        var eligible = await CreateLaptopAsync(client);
        Assert.True((await GetAsync(client, eligible.Id)).CurrentPhotosCorrectable);

        var assigned = await CreateLaptopAsync(client);
        await FinalizeInPersonAsync(client, assigned.Id);
        Assert.False((await GetAsync(client, assigned.Id)).CurrentPhotosCorrectable);

        var dispatched = await CreateLaptopAsync(client);
        await DispatchCarrierAsync(client, dispatched.Id);
        Assert.False((await GetAsync(client, dispatched.Id)).CurrentPhotosCorrectable);
    }

    [Fact]
    public async Task InCustody_ReplacementIsRejected_AndTheCurrentSetIsUnchanged()
    {
        using var client = await LoginAsync("Administration");
        var laptop = await CreateLaptopAsync(client);
        await FinalizeInPersonAsync(client, laptop.Id);

        var response = await ReplaceAsync(client, laptop.Id, laptop.CurrentPhotoSet!.Id, (1, Jpeg(0x41)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("fora de custódia", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(laptop.CurrentPhotoSet.Id, (await GetAsync(client, laptop.Id)).CurrentPhotoSet!.Id);
    }

    [Fact]
    public async Task ReturnPending_StillInCustody_ReplacementIsRejected()
    {
        using var client = await LoginAsync("Administration");
        var laptop = await CreateLaptopAsync(client);
        await FinalizeInPersonAsync(client, laptop.Id);
        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, laptop.Id);
        await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);
        Assert.Equal(EquipmentStatus.ReturnPending, (await GetAsync(client, laptop.Id)).Status);

        var response = await ReplaceAsync(client, laptop.Id, laptop.CurrentPhotoSet!.Id, (1, Jpeg(0x42)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(laptop.CurrentPhotoSet.Id, (await GetAsync(client, laptop.Id)).CurrentPhotoSet!.Id);
    }

    [Fact]
    public async Task OnADispatchedCarrierHandover_ReplacementIsRejected_ButANonDispatchedDraftDoesNotLockIt()
    {
        using var client = await LoginAsync("Administration");
        var dispatched = await CreateLaptopAsync(client);
        await DispatchCarrierAsync(client, dispatched.Id);

        var rejected = await ReplaceAsync(client, dispatched.Id, dispatched.CurrentPhotoSet!.Id, (1, Jpeg(0x43)));

        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("envio registado", await rejected.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(dispatched.CurrentPhotoSet.Id, (await GetAsync(client, dispatched.Id)).CurrentPhotoSet!.Id);

        // A Draft not yet dispatched freezes nothing: the correction is
        // allowed, and its later confirmation will freeze the new set.
        var inDraft = await CreateLaptopAsync(client);
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, inDraft.Id, photoCount: 0, deliveryMethod: "Carrier");
        var allowed = await ReplaceAsync(client, inDraft.Id, inDraft.CurrentPhotoSet!.Id, (1, Jpeg(0x44)));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task StateChangedSinceThePageLoaded_IsAConflict_AndNothingChanges()
    {
        using var client = await LoginAsync("Administration");
        var laptop = await CreateLaptopAsync(client);
        var loadedSetId = laptop.CurrentPhotoSet!.Id;

        // Race A — custody arrives between loading the dialog and saving.
        await FinalizeInPersonAsync(client, laptop.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await ReplaceAsync(client, laptop.Id, loadedSetId, (1, Jpeg(0x51)))).StatusCode);

        // Race B — the dispatch is registered between loading and saving.
        var other = await CreateLaptopAsync(client);
        await DispatchCarrierAsync(client, other.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await ReplaceAsync(client, other.Id, other.CurrentPhotoSet!.Id, (1, Jpeg(0x52)))).StatusCode);

        // Another correction already replaced the set the user was looking at.
        var third = await CreateLaptopAsync(client);
        (await ReplaceAsync(client, third.Id, third.CurrentPhotoSet!.Id, (1, Jpeg(0x53)))).EnsureSuccessStatusCode();
        var stale = await ReplaceAsync(client, third.Id, third.CurrentPhotoSet.Id, (2, Jpeg(0x54)));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        foreach (var id in new[] { laptop.Id, other.Id })
        {
            Assert.Equal(1, await CountSetsAsync(id));
        }

        Assert.Equal(2, await CountSetsAsync(third.Id));
    }

    [Fact]
    public async Task Hr_Anonymous_AndMissingCsrf_AreRejected()
    {
        using var adminClient = await LoginAsync("Administration");
        var laptop = await CreateLaptopAsync(adminClient);
        using var hrClient = await LoginAsync("HR");

        Assert.Equal(HttpStatusCode.Forbidden, (await ReplaceAsync(hrClient, laptop.Id, laptop.CurrentPhotoSet!.Id, (1, Jpeg(0x61)))).StatusCode);
        Assert.False((await GetAsync(hrClient, laptop.Id)).CurrentPhotoSet is null);

        using var anonymous = factory.CreateClient();
        using (var content = new MultipartFormDataContent())
        {
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await anonymous.PostAsync($"/api/equipment/{laptop.Id}/current-photos/replacement", content)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await adminClient.PostAsync($"/api/equipment/{laptop.Id}/current-photos/replacement", content)).StatusCode);
        }

        Assert.Equal(laptop.CurrentPhotoSet.Id, (await GetAsync(adminClient, laptop.Id)).CurrentPhotoSet!.Id);
    }

    [Fact]
    public async Task AnInvalidReplacement_LeavesTheOldSetCurrent_WithNoOrphanRowsOrFiles()
    {
        using var client = await LoginAsync("Administration");
        var laptop = await CreateLaptopAsync(client);
        var filesBefore = CountStoredEquipmentPhotos();

        // Slot 1 is a valid upload and slot 2 a kept copy (both written to
        // storage before slot 3 fails its signature check): all must go.
        var response = await ReplaceAsync(client, laptop.Id, laptop.CurrentPhotoSet!.Id, (1, Jpeg(0x71)), (3, "not an image"u8.ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(laptop.CurrentPhotoSet.Id, (await GetAsync(client, laptop.Id)).CurrentPhotoSet!.Id);
        Assert.Equal(1, await CountSetsAsync(laptop.Id));
        Assert.Equal(filesBefore, CountStoredEquipmentPhotos());

        var noSlots = await ReplaceAsync(client, laptop.Id, laptop.CurrentPhotoSet.Id);
        Assert.Equal(HttpStatusCode.BadRequest, noSlots.StatusCode);
    }

    [Fact]
    public async Task ATermFrozenOnTheOldSet_StillUsesIt_AReturnSetKeepsItsOrigin_AndTheNextTermUsesTheCorrection()
    {
        using var client = await LoginAsync("Administration");
        var laptop = await CreateLaptopAsync(client);
        var initialSetId = laptop.CurrentPhotoSet!.Id;

        // T1 freezes the initial set.
        var (handoverId, itemId) = await FinalizeInPersonAsync(client, laptop.Id);
        var t1 = await GenerateTermAsync(client, handoverId);
        var t1Bytes = await DownloadTermAsync(client, t1.Id);

        // A normal Return receipt (unchanged): exactly 3 photos, origin Return.
        var returnId = await ReceiveReturnAsync(client, laptop.Id);
        var returnSet = (await GetAsync(client, laptop.Id)).CurrentPhotoSet!;
        Assert.Equal(PhotoSetSource.Return, returnSet.Source);
        Assert.Equal(3, returnSet.Photos.Count);
        Assert.NotEqual(initialSetId, returnSet.Id);

        // Administration corrects the Return set outside custody.
        (await ReplaceAsync(client, laptop.Id, returnSet.Id, (3, Jpeg(0x81)))).EnsureSuccessStatusCode();
        var corrected = (await GetAsync(client, laptop.Id)).CurrentPhotoSet!;
        Assert.Equal(PhotoSetSource.Return, corrected.Source);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(returnId, (await dbContext.EquipmentPhotoSets.SingleAsync(s => s.Id == corrected.Id)).EquipmentReturnId);
            Assert.Equal(PhotoSetStatus.Historical, (await dbContext.EquipmentPhotoSets.SingleAsync(s => s.Id == returnSet.Id)).Status);
            Assert.Equal(initialSetId, (await dbContext.EquipmentHandoverItems.SingleAsync(i => i.Id == itemId)).EquipmentPhotoSetId);
        }

        // T1 is untouched: same version, same bytes, still the frozen set.
        var terms = await client.GetFromJsonAsync<List<ResponsibilityTermResponse>>(
            $"/api/equipment-handovers/{handoverId}/responsibility-terms", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(t1.Id, Assert.Single(terms!).Id);
        Assert.Equal(t1Bytes, await DownloadTermAsync(client, t1.Id));

        // The next handover freezes the corrected set.
        var (_, nextItemId) = await FinalizeInPersonAsync(client, laptop.Id, ensureAvailable: true);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(corrected.Id, (await dbContext.EquipmentHandoverItems.SingleAsync(i => i.Id == nextItemId)).EquipmentPhotoSetId);
        }
    }

    [Fact]
    public async Task Replacement_IsAudited_WithIdsAndSlotsOnly()
    {
        using var client = await LoginAsync("Administration");
        var laptop = await CreateLaptopAsync(client);

        (await ReplaceAsync(client, laptop.Id, laptop.CurrentPhotoSet!.Id, (2, Jpeg(0x91)))).EnsureSuccessStatusCode();
        var newSetId = (await GetAsync(client, laptop.Id)).CurrentPhotoSet!.Id;

        var page = await client.GetFromJsonAsync<PagedResponse<AuditLogResponse>>(
            $"/api/audit?entityId={laptop.Id}&page=1&pageSize=100", HttpClientTestExtensions.JsonOptions);
        var entry = Assert.Single(page!.Items, e => e.Action == AuditActions.EquipmentPhotoSet.Replaced);
        Assert.Equal("Equipment", entry.EntityType);
        var json = (entry.OldValuesJson ?? string.Empty) + (entry.NewValuesJson ?? string.Empty);
        Assert.Contains(laptop.CurrentPhotoSet.Id.ToString(), json);
        Assert.Contains(newSetId.ToString(), json);
        Assert.Contains(laptop.AssetTag, json);
        Assert.Contains("InitialRegistration", json);
        Assert.DoesNotContain("replacement-2.jpg", json);
        Assert.DoesNotContain("photo.jpg", json);
    }

    // --- Setup ---------------------------------------------------------------

    private static async Task<HttpResponseMessage> ReplaceAsync(HttpClient client, Guid equipmentId, Guid expectedSetId, params (int Slot, byte[] Bytes)[] slots)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        using var content = new MultipartFormDataContent { { new StringContent(expectedSetId.ToString()), "ExpectedPhotoSetId" } };
        foreach (var (slot, bytes) in slots)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(file, $"Photo{slot}", $"replacement-{slot}.jpg");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/equipment/{equipmentId}/current-photos/replacement") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return await client.SendAsync(request);
    }

    private static async Task<EquipmentDetailResponse> CreateLaptopAsync(HttpClient client)
    {
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LP");
        return await Phase3TestData.CreateLaptopEquipmentAsync(client, type.Id);
    }

    private static async Task<EquipmentDetailResponse> CreatePhoneAsync(HttpClient client)
    {
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        return await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, type.Id);
    }

    private static async Task<EquipmentDetailResponse> GetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{id}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<List<byte[]>> DownloadAllAsync(HttpClient client, Guid equipmentId, EquipmentPhotoSetResponse set)
    {
        var all = new List<byte[]>();
        foreach (var photo in set.Photos.OrderBy(p => p.SortOrder))
        {
            var response = await client.GetAsync($"/api/equipment/{equipmentId}/photos/{photo.Id}/content");
            response.EnsureSuccessStatusCode();
            all.Add(await response.Content.ReadAsByteArrayAsync());
        }

        return all;
    }

    private static async Task<(Guid HandoverId, Guid ItemId)> FinalizeInPersonAsync(HttpClient client, Guid equipmentId, bool ensureAvailable = false)
    {
        if (ensureAvailable)
        {
            // After a Return receipt the equipment is UnderInspection; finish
            // the return inspection so it is Available again.
            Assert.Equal(EquipmentStatus.Available, (await GetAsync(client, equipmentId)).Status);
        }

        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var (handover, itemId) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipmentId, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();
        return (handover.Id, itemId);
    }

    private static async Task DispatchCarrierAsync(HttpClient client, Guid equipmentId)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipmentId, photoCount: 0, deliveryMethod: "Carrier");
        (await Phase4TestData.RegisterDispatchAsync(client, handover.Id)).EnsureSuccessStatusCode();
    }

    private async Task<Guid> ReceiveReturnAsync(HttpClient client, Guid equipmentId)
    {
        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, equipmentId);
        var equipmentReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);
        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: $"Rua Receiver {Guid.NewGuid():N}, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, equipmentReturn.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3))
            .EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();
        return equipmentReturn.Id;
    }

    private static async Task<ResponsibilityTermResponse> GenerateTermAsync(HttpClient client, Guid handoverId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync<object?>($"/api/equipment-handovers/{handoverId}/responsibility-terms", null, csrfToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task<string> DownloadTermAsync(HttpClient client, Guid termId)
    {
        var bytes = await (await client.GetAsync($"/api/responsibility-terms/{termId}/content")).Content.ReadAsByteArrayAsync();
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private async Task<int> CountSetsAsync(Guid equipmentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.EquipmentPhotoSets.CountAsync(s => s.EquipmentId == equipmentId);
    }

    /// <summary>Tests in this collection run sequentially, so the run's private equipment-photo folder is stable around a single call.</summary>
    private static int CountStoredEquipmentPhotos()
    {
        var folder = Path.Combine(TestStorage.RootPath, "equipment-photos");
        return Directory.Exists(folder) ? Directory.GetFiles(folder).Length : 0;
    }

    private async Task<HttpClient> LoginAsync(string role)
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, role);
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
