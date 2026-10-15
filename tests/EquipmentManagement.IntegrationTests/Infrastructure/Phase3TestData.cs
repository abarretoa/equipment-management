using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Equipments;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Creates EquipmentTypes/Equipment through the real HTTP API for integration
/// test setup — same rationale as <see cref="Phase2TestData"/>.
/// </summary>
public static class Phase3TestData
{
    public static async Task<EquipmentTypeResponse> CreateEquipmentTypeAsync(
        HttpClient client, string? name = null, string kind = "Generic", string? assetTagPrefix = null)
    {
        name ??= $"Test Type {Guid.NewGuid():N}";
        assetTagPrefix ??= "TT";
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync("/api/equipment-types", new
        {
            Name = name,
            Kind = kind,
            AssetTagPrefix = assetTagPrefix,
        }, csrfToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<EquipmentTypeResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    /// <summary>The smallest byte sequence <see cref="EquipmentManagement.Domain.Files.ImageSignatureDetector"/> recognizes as a genuine JPEG — same bytes as <see cref="Phase4TestData.MinimalJpegBytes"/>.</summary>
    public static readonly byte[] MinimalJpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    public static async Task<EquipmentDetailResponse> CreateGenericEquipmentAsync(
        HttpClient client, Guid equipmentTypeId, string? serialNumber = null)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostMultipartWithCsrfAsync(
            "/api/equipment", "Photos", [],
            new Dictionary<string, string>
            {
                ["EquipmentTypeId"] = equipmentTypeId.ToString(),
                ["Manufacturer"] = "Logitech",
                ["Model"] = "MX Master",
                ["SerialNumber"] = serialNumber ?? "",
            }, csrfToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    public static async Task<EquipmentDetailResponse> CreateLaptopEquipmentAsync(
        HttpClient client, Guid equipmentTypeId, string? serialNumber = null, int photoCount = 3)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var photos = Enumerable.Range(0, photoCount).Select(_ => (MinimalJpegBytes, "photo.jpg", "image/jpeg")).ToList();
        var response = await client.PostMultipartWithCsrfAsync(
            "/api/equipment", "Photos", photos,
            new Dictionary<string, string>
            {
                ["EquipmentTypeId"] = equipmentTypeId.ToString(),
                ["Manufacturer"] = "Dell",
                ["Model"] = "Latitude 5450",
                ["SerialNumber"] = serialNumber ?? $"SN{Guid.NewGuid():N}"[..12],
                ["Processor"] = "Intel i7-1365U",
                ["Memory"] = "16 GB",
                ["Storage"] = "512 GB SSD",
            }, csrfToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    /// <summary><paramref name="response"/> is returned raw (not <c>EnsureSuccessStatusCode</c>'d) so callers can assert on validation failures (wrong photo count, etc.) themselves.</summary>
    public static Task<HttpResponseMessage> TryCreateLaptopEquipmentRawAsync(
        HttpClient client, Guid equipmentTypeId, int photoCount, string csrfToken, string? serialNumber = null)
    {
        var photos = Enumerable.Range(0, photoCount).Select(_ => (MinimalJpegBytes, "photo.jpg", "image/jpeg")).ToList();
        return client.PostMultipartWithCsrfAsync(
            "/api/equipment", "Photos", photos,
            new Dictionary<string, string>
            {
                ["EquipmentTypeId"] = equipmentTypeId.ToString(),
                ["Manufacturer"] = "Dell",
                ["Model"] = "Latitude 5450",
                ["SerialNumber"] = serialNumber ?? $"SN{Guid.NewGuid():N}"[..12],
                ["Processor"] = "Intel i7-1365U",
                ["Memory"] = "16 GB",
                ["Storage"] = "512 GB SSD",
            }, csrfToken);
    }

    public static async Task<EquipmentDetailResponse> CreateMobilePhoneEquipmentAsync(
        HttpClient client, Guid equipmentTypeId, string? imei = null, int photoCount = 3)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var photos = Enumerable.Range(0, photoCount).Select(_ => (MinimalJpegBytes, "photo.jpg", "image/jpeg")).ToList();
        var response = await client.PostMultipartWithCsrfAsync(
            "/api/equipment", "Photos", photos,
            new Dictionary<string, string>
            {
                ["EquipmentTypeId"] = equipmentTypeId.ToString(),
                ["Manufacturer"] = "Samsung",
                ["Model"] = "Galaxy A05s",
                ["Imei"] = imei ?? $"{Random.Shared.NextInt64(100000000000000, 999999999999999)}",
                ["MobileNumber"] = "910000555",
                ["SimSerialNumber"] = "89351000000000000123",
            }, csrfToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
    }
}
