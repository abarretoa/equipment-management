using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Equipments;

/// <summary>
/// EquipmentType reference-data lifecycle against a real PostgreSQL database
/// (docs/PROJECT_SPEC.md, section 7; Phase 3 scope).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class EquipmentTypeTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Administration_CanCreateEquipmentType()
    {
        using var client = await LoginAsNewAdministratorAsync();

        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, $"Monitor {Guid.NewGuid():N}", "Generic", "MN");

        Assert.Equal("Generic", type.Kind.ToString());
        Assert.True(type.IsActive);
    }

    [Fact]
    public async Task Hr_CanCreateAndUpdateEquipmentType()
    {
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR EQ1, Lisboa");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);

        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, $"Keyboard {Guid.NewGuid():N}", "Generic", "KB");

        var csrfToken = await hrClient.GetAntiforgeryTokenAsync();
        var updateResponse = await hrClient.PutWithCsrfAsync($"/api/equipment-types/{type.Id}", new { Name = $"{type.Name} Updated", AssetTagPrefix = "KB" }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Hr_CannotArchiveEquipmentType()
    {
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR EQ2, Lisboa");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient);

        var csrfToken = await hrClient.GetAntiforgeryTokenAsync();
        var response = await hrClient.PostWithCsrfAsync<object?>($"/api/equipment-types/{type.Id}/deactivate", null, csrfToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateNormalizedName_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var name = $"Duplicate Type {Guid.NewGuid():N}";
        await Phase3TestData.CreateEquipmentTypeAsync(client, name, "Generic", "DT");

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync("/api/equipment-types", new
        {
            Name = $"  {name.ToUpperInvariant()}  ",
            Kind = "Generic",
            AssetTagPrefix = "DT",
        }, csrfToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task InactiveEquipmentType_CannotRegisterNewEquipment()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client);

        var deactivateCsrf = await client.GetAntiforgeryTokenAsync();
        await client.PostWithCsrfAsync<object?>($"/api/equipment-types/{type.Id}/deactivate", null, deactivateCsrf);

        var createCsrf = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostMultipartWithCsrfAsync(
            "/api/equipment", "Photos", [],
            new Dictionary<string, string> { ["EquipmentTypeId"] = type.Id.ToString(), ["Manufacturer"] = "Logitech" }, createCsrf);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task EquipmentTypeEndpoints_HaveNoHardDeleteAction()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client);

        var response = await client.DeleteAsync($"/api/equipment-types/{type.Id}");

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Expected no hard-delete route, got {response.StatusCode}.");
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
