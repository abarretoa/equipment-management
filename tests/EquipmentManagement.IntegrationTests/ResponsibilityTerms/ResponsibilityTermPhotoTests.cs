using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.ResponsibilityTerms;

/// <summary>
/// The single most important correction in this feature: a Handover's
/// Responsibility Term must reference the equipment's condition photo set
/// exactly as it stood at that Handover's finalization
/// (<c>EquipmentHandoverItem.EquipmentPhotoSetId</c>), frozen forever — never
/// a live "whatever is Current now" read. A later Return that establishes a
/// brand-new current photo set for the same equipment must never be picked up
/// by an older, already-finalized Handover's Term, even on regeneration.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class ResponsibilityTermPhotoTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Generate_ForNewlyFinalizedHandover_FreezesTheEquipmentCurrentPhotoSetAtThatMoment()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, equipmentId, itemId) = await CreateFinalizedLaptopHandoverAsync(client);
        var photoSetAAtFinalize = await GetEquipmentCurrentPhotoSetIdAsync(equipmentId);

        (await GenerateTermAsync(client, handover.Id)).EnsureSuccessStatusCode();

        var frozenPhotoSetId = await GetHandoverItemFrozenPhotoSetIdAsync(itemId);
        Assert.Equal(photoSetAAtFinalize, frozenPhotoSetId);
    }

    [Fact]
    public async Task AfterAReturnEstablishesNewPhotos_TheOldHandoversFrozenReference_IsUnchanged()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, equipmentId, itemId) = await CreateFinalizedLaptopHandoverAsync(client);
        var photoSetA = await GetHandoverItemFrozenPhotoSetIdAsync(itemId);
        Assert.NotNull(photoSetA);

        await CreateAndReceiveReturnWithNewPhotosAsync(client, equipmentId);

        // The equipment's Current set is now a brand-new one (B), and A has
        // been superseded to Historical — but the OLD handover's item still
        // points at A, byte-for-byte unchanged.
        var photoSetBId = await GetEquipmentCurrentPhotoSetIdAsync(equipmentId);
        Assert.NotEqual(photoSetA, photoSetBId);

        var stillFrozenPhotoSetId = await GetHandoverItemFrozenPhotoSetIdAsync(itemId);
        Assert.Equal(photoSetA, stillFrozenPhotoSetId);

        await AssertPhotoSetStatusAsync(photoSetA!.Value, "Historical");
        await AssertPhotoSetStatusAsync(photoSetBId!.Value, "Current");

        // Regenerating the Term for this same, already-finalized Handover
        // must still succeed (A's row still exists, only its Status flipped)
        // and must still resolve through the frozen reference, not B.
        var regenerateResponse = await GenerateTermAsync(client, handover.Id);
        regenerateResponse.EnsureSuccessStatusCode();
        var stillFrozenAfterRegenerate = await GetHandoverItemFrozenPhotoSetIdAsync(itemId);
        Assert.Equal(photoSetA, stillFrozenAfterRegenerate);
    }

    [Fact]
    public async Task ANewHandoverFinalizedAfterTheReturn_FreezesTheNewCurrentPhotoSet()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (firstHandover, equipmentId, firstItemId) = await CreateFinalizedLaptopHandoverAsync(client);
        var photoSetA = await GetHandoverItemFrozenPhotoSetIdAsync(firstItemId);

        await CreateAndReceiveReturnWithNewPhotosAsync(client, equipmentId);
        var photoSetB = await GetEquipmentCurrentPhotoSetIdAsync(equipmentId);
        Assert.NotEqual(photoSetA, photoSetB);

        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var (secondHandover, secondItemId) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipmentId, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, secondHandover.Id)).EnsureSuccessStatusCode();

        var secondHandoverFrozenPhotoSetId = await GetHandoverItemFrozenPhotoSetIdAsync(secondItemId);
        Assert.Equal(photoSetB, secondHandoverFrozenPhotoSetId);
    }

    [Fact]
    public async Task Generate_ForALegacyHandoverWithNoFrozenPhotoSet_FailsWithADistinctMessage()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, _, itemId) = await CreateFinalizedLaptopHandoverAsync(client);

        // Simulate a Handover finalized before this feature shipped — its
        // item's EquipmentPhotoSetId column starts null for every historical
        // row, and must never be retroactively backfilled from whatever is
        // Current today.
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var item = await dbContext.Set<EquipmentManagement.Domain.Handovers.EquipmentHandoverItem>()
                .FirstAsync(i => i.Id == itemId);
            item.EquipmentPhotoSetId = null;
            await dbContext.SaveChangesAsync();
        }

        var response = await GenerateTermAsync(client, handover.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private async Task<Guid> CreateAndReceiveReturnWithNewPhotosAsync(HttpClient client, Guid equipmentId)
    {
        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, equipmentId);
        var equipmentReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: $"Rua Receiver {Guid.NewGuid():N}, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);

        var receiveResponse = await Phase5TestData.ReceiveReturnAsync(
            client, equipmentReturn.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3);
        receiveResponse.EnsureSuccessStatusCode();

        (await Phase5TestData.RecordReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();

        return equipmentReturn.Id;
    }

    private async Task<Guid?> GetEquipmentCurrentPhotoSetIdAsync(Guid equipmentId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.EquipmentPhotoSets
            .Where(s => s.EquipmentId == equipmentId && s.Status == EquipmentManagement.Domain.Equipments.PhotoSetStatus.Current)
            .Select(s => (Guid?)s.Id)
            .SingleOrDefaultAsync();
    }

    private async Task<Guid?> GetHandoverItemFrozenPhotoSetIdAsync(Guid itemId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.Set<EquipmentManagement.Domain.Handovers.EquipmentHandoverItem>()
            .Where(i => i.Id == itemId)
            .Select(i => i.EquipmentPhotoSetId)
            .SingleAsync();
    }

    private async Task AssertPhotoSetStatusAsync(Guid photoSetId, string expectedStatus)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var status = await dbContext.EquipmentPhotoSets.Where(s => s.Id == photoSetId).Select(s => s.Status.ToString()).SingleAsync();
        Assert.Equal(expectedStatus, status);
    }

    private static async Task<HttpResponseMessage> GenerateTermAsync(HttpClient client, Guid handoverId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync<object?>($"/api/equipment-handovers/{handoverId}/responsibility-terms", null, csrfToken);
    }

    private static async Task<(EquipmentHandoverDetailResponse Handover, Guid EquipmentId, Guid ItemId)> CreateFinalizedLaptopHandoverAsync(HttpClient client)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LP");
        var equipment = await Phase3TestData.CreateLaptopEquipmentAsync(client, equipmentType.Id);

        var (handover, itemId) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        var finalizeResponse = await Phase4TestData.FinalizeAsync(client, handover.Id);
        finalizeResponse.EnsureSuccessStatusCode();
        var finalized = (await finalizeResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;

        return (finalized, equipment.Id, itemId);
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
