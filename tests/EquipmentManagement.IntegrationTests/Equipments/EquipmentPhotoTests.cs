using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Equipments;

/// <summary>
/// Equipment condition photos: exactly-3-photos creation validation for
/// Laptop/MobilePhone, MobilePhone equipment creation, the legacy backfill
/// action, and the invariant that ordinary Equipment Edit can never create or
/// replace the current photo set.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class EquipmentPhotoTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task CreateLaptop_WithWrongPhotoCount_IsRejected(int photoCount)
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LP");

        var response = await Phase3TestData.TryCreateLaptopEquipmentRawAsync(client, type.Id, photoCount, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLaptop_WithExactlyThreePhotos_Succeeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LP");

        var equipment = await Phase3TestData.CreateLaptopEquipmentAsync(client, type.Id);

        Assert.NotNull(equipment.CurrentPhotoSet);
        Assert.Equal(3, equipment.CurrentPhotoSet!.Photos.Count);
        Assert.Equal("InitialRegistration", equipment.CurrentPhotoSet.Source.ToString());
    }

    [Fact]
    public async Task CreateGenericEquipment_RejectsAnyPhotos()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");

        var photos = new List<(byte[] Bytes, string FileName, string ContentType)> { (Phase3TestData.MinimalJpegBytes, "photo.jpg", "image/jpeg") };
        var response = await client.PostMultipartWithCsrfAsync(
            "/api/equipment", "Photos", photos,
            new Dictionary<string, string> { ["EquipmentTypeId"] = type.Id.ToString(), ["Manufacturer"] = "Logitech" }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMobilePhone_WithExactlyThreePhotos_Succeeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");

        var equipment = await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, type.Id);

        Assert.NotNull(equipment.MobilePhoneSpecification);
        Assert.NotNull(equipment.CurrentPhotoSet);
        Assert.Equal(3, equipment.CurrentPhotoSet!.Photos.Count);
    }

    [Fact]
    public async Task EstablishPhotos_ForLegacyEquipmentWithNoCurrentSet_Succeeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LP");
        var equipment = await Phase3TestData.CreateLaptopEquipmentAsync(client, type.Id);

        // Simulate a legacy row that predates this feature — same
        // DB-manipulation pattern already used elsewhere in this suite.
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EquipmentManagement.Infrastructure.Persistence.ApplicationDbContext>();
            var photoSets = await dbContext.EquipmentPhotoSets.Where(s => s.EquipmentId == equipment.Id).ToListAsync();
            dbContext.EquipmentPhotoSets.RemoveRange(photoSets);
            await dbContext.SaveChangesAsync();
        }

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var photos = Enumerable.Range(0, 3).Select(_ => (Phase3TestData.MinimalJpegBytes, "photo.jpg", "image/jpeg")).ToList();
        var response = await client.PostMultipartWithCsrfAsync($"/api/equipment/{equipment.Id}/photos", "Photos", photos, new Dictionary<string, string>(), csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.NotNull(updated!.CurrentPhotoSet);
    }

    [Fact]
    public async Task EstablishPhotos_WhenCurrentSetAlreadyExists_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LP");
        var equipment = await Phase3TestData.CreateLaptopEquipmentAsync(client, type.Id);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var photos = Enumerable.Range(0, 3).Select(_ => (Phase3TestData.MinimalJpegBytes, "photo.jpg", "image/jpeg")).ToList();
        var response = await client.PostMultipartWithCsrfAsync($"/api/equipment/{equipment.Id}/photos", "Photos", photos, new Dictionary<string, string>(), csrfToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateEquipment_NeverChangesTheCurrentPhotoSet()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LP");
        var equipment = await Phase3TestData.CreateLaptopEquipmentAsync(client, type.Id);
        var originalPhotoSetId = equipment.CurrentPhotoSet!.Id;

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var updateResponse = await client.PutWithCsrfAsync($"/api/equipment/{equipment.Id}", new
        {
            Manufacturer = "Dell",
            Model = "Latitude 5450 Updated",
            SerialNumber = equipment.SerialNumber,
            Notes = "Updated via edit form",
            LaptopSpecification = new { Processor = "Intel i7-1365U", Memory = "16 GB", Storage = "512 GB SSD" },
        }, csrfToken);
        updateResponse.EnsureSuccessStatusCode();

        var refreshed = await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipment.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.NotNull(refreshed!.CurrentPhotoSet);
        Assert.Equal(originalPhotoSetId, refreshed.CurrentPhotoSet!.Id);
        Assert.Equal(3, refreshed.CurrentPhotoSet.Photos.Count);
        Assert.Equal("Updated via edit form", refreshed.Notes);
    }

    [Fact]
    public async Task PhotoContent_IsDownloadableByAuthenticatedUser()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LP");
        var equipment = await Phase3TestData.CreateLaptopEquipmentAsync(client, type.Id);
        var photoId = equipment.CurrentPhotoSet!.Photos[0].Id;

        var response = await client.GetAsync($"/api/equipment/{equipment.Id}/photos/{photoId}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
    }

    /// <summary>
    /// <c>SortOrder</c>'s range (1-3) is backed by a real Postgres <c>CHECK</c>
    /// constraint (<c>CK_EquipmentPhotoSetPhotos_SortOrder_Range</c>), not just
    /// application code — a row outside that range must be physically
    /// unwritable, regardless of caller.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task PhotoSetPhoto_WithSortOrderOutOfRange_ViolatesTheDatabaseCheckConstraint(int invalidSortOrder)
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LP");
        var equipment = await Phase3TestData.CreateLaptopEquipmentAsync(client, type.Id);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquipmentManagement.Infrastructure.Persistence.ApplicationDbContext>();
        dbContext.EquipmentPhotoSetPhotos.Add(new EquipmentManagement.Domain.Equipments.EquipmentPhotoSetPhoto
        {
            Id = Guid.NewGuid(),
            EquipmentPhotoSetId = equipment.CurrentPhotoSet!.Id,
            StorageKey = $"equipment-photos/{equipment.Id:N}/{Guid.NewGuid():N}.jpg",
            OriginalFileName = "out-of-range.jpg",
            ContentType = "image/jpeg",
            SizeBytes = Phase3TestData.MinimalJpegBytes.Length,
            Sha256Hash = new string('a', 64),
            SortOrder = invalidSortOrder,
            UploadedAtUtc = DateTime.UtcNow,
            UploadedByUserId = Guid.NewGuid(),
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    /// <summary>
    /// <c>(EquipmentPhotoSetId, SortOrder)</c> is backed by a real Postgres
    /// unique index — two photos in the same set can never share a slot.
    /// </summary>
    [Fact]
    public async Task PhotoSetPhoto_WithDuplicateSortOrderInTheSameSet_ViolatesTheDatabaseUniqueIndex()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LP");
        var equipment = await Phase3TestData.CreateLaptopEquipmentAsync(client, type.Id);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquipmentManagement.Infrastructure.Persistence.ApplicationDbContext>();
        dbContext.EquipmentPhotoSetPhotos.Add(new EquipmentManagement.Domain.Equipments.EquipmentPhotoSetPhoto
        {
            Id = Guid.NewGuid(),
            EquipmentPhotoSetId = equipment.CurrentPhotoSet!.Id,
            StorageKey = $"equipment-photos/{equipment.Id:N}/{Guid.NewGuid():N}.jpg",
            OriginalFileName = "duplicate-slot.jpg",
            ContentType = "image/jpeg",
            SizeBytes = Phase3TestData.MinimalJpegBytes.Length,
            Sha256Hash = new string('b', 64),
            SortOrder = 1, // equipment.CurrentPhotoSet already has a photo at SortOrder 1
            UploadedAtUtc = DateTime.UtcNow,
            UploadedByUserId = Guid.NewGuid(),
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
