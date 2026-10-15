using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.Returns;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Infrastructure.Files;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Security;

/// <summary>
/// Every nested destructive route must prove the complete ownership chain
/// named by its route identifiers (security fix round RT-01): an editable
/// parent A must never delete a resource that belongs to parent B. A
/// mismatch is a 404 that leaves B's row, private file and audit trail
/// untouched.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class NestedResourceOwnershipTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task HandoverPhotoDelete_ValidHierarchy_DeletesOnlyThatPhoto()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handoverId, itemId) = await CreateDraftWithInspectedItemAsync(client);
        var photoA = await UploadHandoverPhotoAsync(client, handoverId, itemId);
        var sibling = await UploadHandoverPhotoAsync(client, handoverId, itemId);
        var keyA = await GetHandoverPhotoKeyAsync(photoA);

        var response = await DeleteAsync(client, $"/api/equipment-handovers/{handoverId}/items/{itemId}/inspection/photos/{photoA}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await GetHandoverPhotoKeyAsync(photoA, required: false));
        Assert.Null(await OpenStorageAsync(keyA!));
        Assert.NotNull(await GetHandoverPhotoKeyAsync(sibling));

        var audit = await SingleAuditAsync(AuditActions.EquipmentHandover.PhotoDeleted, handoverId);
        Assert.Contains(photoA.ToString(), audit.NewValuesJson);
    }

    [Fact]
    public async Task HandoverPhotoDelete_FakeHandoverParent_Returns404AndKeepsOtherDraftsPhoto()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (draftA, _) = await CreateDraftWithInspectedItemAsync(client);
        var (draftB, itemB) = await CreateDraftWithInspectedItemAsync(client);
        var photoB = await UploadHandoverPhotoAsync(client, draftB, itemB);

        var response = await DeleteAsync(client, $"/api/equipment-handovers/{draftA}/items/{itemB}/inspection/photos/{photoB}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertHandoverPhotoIntactAsync(client, draftB, itemB, photoB);
        Assert.Equal(0, await CountAuditAsync(AuditActions.EquipmentHandover.PhotoDeleted, draftA));
        Assert.Equal(0, await CountAuditAsync(AuditActions.EquipmentHandover.PhotoDeleted, draftB));
    }

    [Fact]
    public async Task HandoverPhotoDelete_CorrectItemButPhotoOfAnotherItem_Returns404()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (draftA, itemA) = await CreateDraftWithInspectedItemAsync(client);
        var (draftB, itemB) = await CreateDraftWithInspectedItemAsync(client);
        var photoB = await UploadHandoverPhotoAsync(client, draftB, itemB);

        var response = await DeleteAsync(client, $"/api/equipment-handovers/{draftA}/items/{itemA}/inspection/photos/{photoB}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertHandoverPhotoIntactAsync(client, draftB, itemB, photoB);
        Assert.Equal(0, await CountAuditAsync(AuditActions.EquipmentHandover.PhotoDeleted, draftA));
    }

    [Fact]
    public async Task HandoverPhotoDelete_UnknownPhoto_Returns404()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (draftA, itemA) = await CreateDraftWithInspectedItemAsync(client);

        var response = await DeleteAsync(client, $"/api/equipment-handovers/{draftA}/items/{itemA}/inspection/photos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HandoverPhotoDelete_ConfirmedHandoverOwnPhoto_IsRejectedAsImmutable()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (confirmedB, itemB, photoB) = await CreateConfirmedHandoverWithPhotoAsync(client);

        var response = await DeleteAsync(client, $"/api/equipment-handovers/{confirmedB}/items/{itemB}/inspection/photos/{photoB}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertHandoverPhotoIntactAsync(client, confirmedB, itemB, photoB);
    }

    [Fact]
    public async Task HandoverPhotoDelete_EditableDraftParentWithConfirmedHandoversPhoto_Returns404AndKeepsHistoricalEvidence()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (draftA, _) = await CreateDraftWithInspectedItemAsync(client);
        var (confirmedB, itemB, photoB) = await CreateConfirmedHandoverWithPhotoAsync(client);

        var response = await DeleteAsync(client, $"/api/equipment-handovers/{draftA}/items/{itemB}/inspection/photos/{photoB}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertHandoverPhotoIntactAsync(client, confirmedB, itemB, photoB);
        Assert.Equal(0, await CountAuditAsync(AuditActions.EquipmentHandover.PhotoDeleted, draftA));
        Assert.Equal(0, await CountAuditAsync(AuditActions.EquipmentHandover.PhotoDeleted, confirmedB));
    }

    [Fact]
    public async Task ReturnInspectionPhotoDelete_FakeReturnParent_Returns404AndKeepsOtherReturnsPhoto()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var returnA = await CreateReceivedReturnWithInspectionAsync(client);
        var returnB = await CreateReceivedReturnWithInspectionAsync(client);
        var photoResponse = await Phase5TestData.UploadReturnPhotoAsync(client, returnB);
        photoResponse.EnsureSuccessStatusCode();
        var photoB = (await photoResponse.Content.ReadFromJsonAsync<ReturnInspectionPhotoResponse>(HttpClientTestExtensions.JsonOptions))!.Id;

        var response = await DeleteAsync(client, $"/api/equipment-returns/{returnA}/inspection/photos/{photoB}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        string key;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            key = (await db.ReturnInspectionPhotos.AsNoTracking().SingleAsync(p => p.Id == photoB)).StorageKey;
        }

        Assert.NotNull(await OpenStorageAsync(key));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/equipment-returns/{returnB}/inspection/photos/{photoB}/content")).StatusCode);
        Assert.Equal(0, await CountAuditAsync(AuditActions.ReturnInspection.PhotoRemoved, returnA));
    }

    [Fact]
    public async Task DamagePhotoDelete_FakeReturnParent_Returns404AndKeepsOtherReturnsEvidence()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var returnA = await CreateReceivedReturnWithInspectionAsync(client);
        var returnB = await CreateReceivedReturnWithInspectionAsync(client);
        var photoResponse = await Phase5TestData.UploadDamagePhotoAsync(client, returnB);
        photoResponse.EnsureSuccessStatusCode();
        var photoB = (await photoResponse.Content.ReadFromJsonAsync<DamagePhotoResponse>(HttpClientTestExtensions.JsonOptions))!.Id;

        var response = await DeleteAsync(client, $"/api/equipment-returns/{returnA}/damage/photos/{photoB}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        string key;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            key = (await db.DamagePhotos.AsNoTracking().SingleAsync(p => p.Id == photoB)).StorageKey;
        }

        Assert.NotNull(await OpenStorageAsync(key));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/equipment-returns/{returnB}/damage/photos/{photoB}/content")).StatusCode);
        Assert.Equal(0, await CountAuditAsync(AuditActions.Damage.PhotoRemoved, returnA));
    }

    private async Task AssertHandoverPhotoIntactAsync(HttpClient client, Guid handoverId, Guid itemId, Guid photoId)
    {
        var key = await GetHandoverPhotoKeyAsync(photoId);
        Assert.NotNull(await OpenStorageAsync(key!));
        var content = await client.GetAsync($"/api/equipment-handovers/{handoverId}/items/{itemId}/inspection/photos/{photoId}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
    }

    private static async Task<(Guid HandoverId, Guid ItemId)> CreateDraftWithInspectedItemAsync(HttpClient client)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var handover = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId);
        var addResponse = await Phase4TestData.AddItemAsync(client, handover.Id, equipment.Id);
        addResponse.EnsureSuccessStatusCode();
        var withItem = (await addResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        var itemId = withItem.Items.Single().Id;
        (await Phase4TestData.RecordInspectionAsync(client, handover.Id, itemId)).EnsureSuccessStatusCode();
        return (handover.Id, itemId);
    }

    private static async Task<(Guid HandoverId, Guid ItemId, Guid PhotoId)> CreateConfirmedHandoverWithPhotoAsync(HttpClient client)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);
        var (handover, itemId) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 1);

        var finalizeResponse = await Phase4TestData.FinalizeAsync(client, handover.Id);
        finalizeResponse.EnsureSuccessStatusCode();
        var finalized = (await finalizeResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        return (handover.Id, itemId, finalized.Items.Single().Inspection!.Photos.Single().Id);
    }

    private async Task<Guid> CreateReceivedReturnWithInspectionAsync(HttpClient client)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, equipment.Id);
        var equipmentReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);
        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver Ownership, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, equipmentReturn.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, equipmentReturn.Id, hasDamage: true, damageDescription: "Risco na tampa")).EnsureSuccessStatusCode();
        return equipmentReturn.Id;
    }

    private static async Task<Guid> UploadHandoverPhotoAsync(HttpClient client, Guid handoverId, Guid itemId)
    {
        var response = await Phase4TestData.UploadPhotoAsync(client, handoverId, itemId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeliveryInspectionPhotoResponse>(HttpClientTestExtensions.JsonOptions))!.Id;
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string url)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.DeleteWithCsrfAsync(url, csrfToken);
    }

    private async Task<string?> GetHandoverPhotoKeyAsync(Guid photoId, bool required = true)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var photo = await db.DeliveryInspectionPhotos.AsNoTracking().SingleOrDefaultAsync(p => p.Id == photoId);
        if (required)
        {
            Assert.NotNull(photo);
        }

        return photo?.StorageKey;
    }

    private async Task<Stream?> OpenStorageAsync(string key)
    {
        var storage = factory.Services.GetRequiredService<IPrivateFileStorage>();
        var stream = await storage.OpenReadAsync(key);
        if (stream is not null)
        {
            await stream.DisposeAsync();
        }

        return stream;
    }

    private async Task<AuditLog> SingleAuditAsync(string action, Guid entityId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == action && a.EntityId == entityId.ToString());
    }

    private async Task<int> CountAuditAsync(string action, Guid entityId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuditLogs.AsNoTracking().CountAsync(a => a.Action == action && a.EntityId == entityId.ToString());
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
