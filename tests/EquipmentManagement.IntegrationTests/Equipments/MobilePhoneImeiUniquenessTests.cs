using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Equipments;

/// <summary>
/// IMEI is unique product-wide: normal mobile phone creation and every update
/// path reject an IMEI already used by another phone (whatever separators it
/// is written with), and PostgreSQL enforces the same rule underneath.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class MobilePhoneImeiUniquenessTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Create_WithImeiAlreadyInUse_IsRejectedWithConflict()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var imei = NewImei();
        await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, type.Id, imei);

        // Same IMEI, written with separators: still the same phone identity.
        var response = await TryCreatePhoneAsync(client, type.Id, $"{imei[..2]}-{imei[2..8]} {imei[8..]}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("IMEI", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, await CountByNormalizedImeiAsync(imei));
    }

    [Theory]
    [InlineData("-")]
    [InlineData(" - - ")]
    public async Task Create_WithSeparatorOnlyImei_IsAValidationError_NotAServerError(string imei)
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");

        var response = await TryCreatePhoneAsync(client, type.Id, imei);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("O IMEI é obrigatório para telemóveis.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" - - ")]
    [InlineData(null)]
    public async Task Update_WithoutImei_IsThePtPtRequiredError(string? imei)
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var phone = await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, type.Id);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PutWithCsrfAsync($"/api/equipment/{phone.Id}", new
        {
            Manufacturer = "Samsung",
            Model = "Galaxy A05s",
            MobilePhoneSpecification = imei is null ? null : new { Imei = imei },
        }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("O IMEI é obrigatório para telemóveis.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("required", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("35693803564380")]
    [InlineData("3569380356438099")]
    [InlineData("35693803564380A")]
    [InlineData("ABCDEFGHIJKLMNO")]
    [InlineData("35-693803564380")]
    [InlineData("")]
    public async Task Database_RejectsANormalizedImeiThatIsNot15Digits(string normalizedImei)
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var phone = await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, type.Id);

        // Bypass every application rule: only the CHECK constraint can object.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var spec = await db.MobilePhoneSpecifications.FirstAsync(s => s.EquipmentId == phone.Id);
        spec.NormalizedImei = normalizedImei;

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<Npgsql.PostgresException>(ex.InnerException);
        Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, postgres.SqlState);
        Assert.Equal("CK_MobilePhoneSpecifications_NormalizedImei_Format", postgres.ConstraintName);
    }

    [Fact]
    public async Task Database_AcceptsAValid15DigitNormalizedImei()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var phone = await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, type.Id);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var spec = await db.MobilePhoneSpecifications.FirstAsync(s => s.EquipmentId == phone.Id);
        var replacement = NewImei();
        spec.Imei = replacement;
        spec.NormalizedImei = replacement;

        await db.SaveChangesAsync();
        Assert.Equal(1, await CountByNormalizedImeiAsync(replacement));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("ABC123")]
    [InlineData("35.693803/564380-9")]
    [InlineData("35693803564380")]
    [InlineData("3569380356438099")]
    public async Task Create_WithMalformedImei_IsAPtPtValidationError(string imei)
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");

        var response = await TryCreatePhoneAsync(client, type.Id, imei);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("O IMEI deve conter exatamente 15 dígitos.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("{1} {2} {3} {4} {5}")]
    [InlineData("{1}-{2}{3}-{4}{5}")]
    public async Task Create_WithValidImei_PersistsTheCanonical15Digits(string layout)
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var imei = NewImei();
        var written = string.Format(System.Globalization.CultureInfo.InvariantCulture, layout, imei, imei[..3], imei[3..6], imei[6..9], imei[9..12], imei[12..]);

        var response = await TryCreatePhoneAsync(client, type.Id, written);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await CountByNormalizedImeiAsync(imei));
    }

    [Fact]
    public async Task Update_ToMalformedImei_IsRejected_AndTheStoredImeiIsKept()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var phone = await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, type.Id);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PutWithCsrfAsync($"/api/equipment/{phone.Id}", new
        {
            Manufacturer = "Samsung",
            Model = "Galaxy A05s",
            MobilePhoneSpecification = new { Imei = "35693803564380" },
        }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("O IMEI deve conter exatamente 15 dígitos.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var refreshed = await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{phone.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(phone.MobilePhoneSpecification!.Imei, refreshed!.MobilePhoneSpecification!.Imei);
    }

    [Fact]
    public async Task Update_ToImeiOfAnotherPhone_IsRejectedWithConflict()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var firstImei = NewImei();
        await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, type.Id, firstImei);
        var second = await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, type.Id);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PutWithCsrfAsync($"/api/equipment/{second.Id}", new
        {
            Manufacturer = "Samsung",
            Model = "Galaxy A05s",
            MobilePhoneSpecification = new { Imei = firstImei },
        }, csrfToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var refreshed = await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{second.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(second.MobilePhoneSpecification!.Imei, refreshed!.MobilePhoneSpecification!.Imei);
    }

    [Fact]
    public async Task Update_KeepingItsOwnImei_Succeeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var imei = NewImei();
        var phone = await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, type.Id, imei);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PutWithCsrfAsync($"/api/equipment/{phone.Id}", new
        {
            Manufacturer = "Samsung",
            Model = "Galaxy A05s (edit)",
            MobilePhoneSpecification = new { Imei = imei, MobileNumber = "960000000" },
        }, csrfToken);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Database_RejectsDuplicateNormalizedImei()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var imei = NewImei();
        await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, type.Id, imei);
        var other = await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, type.Id);

        // Bypass every application check: the unique index alone must refuse it.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var spec = await db.MobilePhoneSpecifications.FirstAsync(s => s.EquipmentId == other.Id);
        spec.Imei = imei;
        spec.NormalizedImei = MobilePhoneSpecificationRules.NormalizeImei(imei)!;
        Assert.True(MobilePhoneSpecificationRules.IsValidImei(spec.NormalizedImei));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<Npgsql.PostgresException>(ex.InnerException);
        Assert.Equal(Npgsql.PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("IX_MobilePhoneSpecifications_NormalizedImei", postgres.ConstraintName);
    }

    private static string NewImei() => $"{Random.Shared.NextInt64(100000000000000, 999999999999999)}";

    private static async Task<HttpResponseMessage> TryCreatePhoneAsync(HttpClient client, Guid typeId, string imei)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var photos = Enumerable.Range(0, 3).Select(_ => (Phase3TestData.MinimalJpegBytes, "photo.jpg", "image/jpeg")).ToList();
        return await client.PostMultipartWithCsrfAsync(
            "/api/equipment", "Photos", photos,
            new Dictionary<string, string>
            {
                ["EquipmentTypeId"] = typeId.ToString(),
                ["Manufacturer"] = "Samsung",
                ["Model"] = "Galaxy A05s",
                ["Imei"] = imei,
            }, csrfToken);
    }

    private async Task<int> CountByNormalizedImeiAsync(string imei)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var normalized = MobilePhoneSpecificationRules.NormalizeImei(imei);
        return await db.MobilePhoneSpecifications.CountAsync(s => s.NormalizedImei == normalized);
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
