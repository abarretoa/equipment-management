using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentManagement.Api.Contracts.Auth;
using EquipmentManagement.Api.Controllers;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Auth;

/// <summary>
/// Login, logout, session introspection and CSRF enforcement against a real
/// PostgreSQL database (Phase 1 scope).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class AuthenticationTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Login_WithValidCredentials_Succeeds()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        using var client = factory.CreateClient();
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync("/api/auth/login", new { email, password }, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.NotNull(body);
        Assert.Equal("Administration", body!.Role);
        Assert.Contains("User.Read", body.Permissions);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsGenericFailure()
    {
        var (email, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Teste 1, Lisboa");
        using var client = factory.CreateClient();
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync("/api/auth/login", new { email, password = "TotallyWrongPassword123!" }, csrfToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(AuthController.GenericLoginFailureMessage, problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsSameGenericFailure()
    {
        using var client = factory.CreateClient();
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync(
            "/api/auth/login", new { email = $"nobody.{Guid.NewGuid():N}@example.invalid", password = "whatever123!" }, csrfToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(AuthController.GenericLoginFailureMessage, problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Login_ForInactiveUser_Fails()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Teste 2, Porto", isActive: false);
        using var client = factory.CreateClient();
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync("/api/auth/login", new { email, password }, csrfToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithoutCsrfToken_IsRejected()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Me_WhenAuthenticated_ReturnsProfile()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        using var client = factory.CreateClient();
        await client.LoginAsync(email, password);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Equal(email, body!.Email);
    }

    [Fact]
    public async Task Me_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_EndsSessionSoMeNoLongerWorks()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        using var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var logoutResponse = await client.PostWithCsrfAsync<object?>("/api/auth/logout", null, csrfToken);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    [Fact]
    public async Task DeactivatingAUser_InvalidatesTheirExistingSession()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Teste 3, Braga");
        using var client = factory.CreateClient();
        await client.LoginAsync(email, password);

        // Confirm the session works before deactivation.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        await ApiTestUsers.SetActiveAsync(factory, email, isActive: false);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
