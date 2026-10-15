using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentManagement.Api.Contracts.Auth;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Auth;

/// <summary>
/// Approved V1 policy (security fix round RT-02): logout revokes EVERY session
/// of that user — a copied cookie and every other browser's cookie are
/// rejected on their very next request — while other users are unaffected and
/// a rejected (CSRF-failing) logout revokes nothing. Cookies are handled raw so
/// a preserved pre-logout cookie can be replayed exactly.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class LogoutSessionRevocationTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string AuthCookieName = "EquipmentManagement.Auth";
    private const string CsrfCookieName = "EquipmentManagement.Csrf";

    [Fact]
    public async Task Logout_ReplayingThePreservedCookie_Is401()
    {
        var (email, _) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var session = await LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(session, "/api/auth/me"));

        Assert.Equal(HttpStatusCode.OK, await LogoutAsync(session));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(session, "/api/auth/me"));
    }

    [Fact]
    public async Task Logout_RevokesEverySessionOfThatUser()
    {
        var (email, _) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var sessionA = await LoginAsync(email);
        var sessionB = await LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(sessionA, "/api/auth/me"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(sessionB, "/api/auth/me"));

        Assert.Equal(HttpStatusCode.OK, await LogoutAsync(sessionA));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(sessionA, "/api/auth/me"));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(sessionB, "/api/auth/me"));
    }

    [Fact]
    public async Task FreshLogin_AfterRevocation_SucceedsWithCurrentPermissions()
    {
        var (email, _) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var old = await LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, await LogoutAsync(old));

        var fresh = await LoginAsync(email);

        using var client = CreateRawClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", fresh.AuthCookie);
        var me = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = (await me.Content.ReadFromJsonAsync<CurrentUserResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(RolePermissions.GetPermissions("Administration").Order(StringComparer.Ordinal), body.Permissions.Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(fresh, "/api/users"));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(old, "/api/auth/me"));
    }

    [Fact]
    public async Task Logout_LeavesOtherUsersSessionsValid()
    {
        var (emailA, _) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var (emailB, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Logout 1, Lisboa");
        var a1 = await LoginAsync(emailA);
        var a2 = await LoginAsync(emailA);
        var b = await LoginAsync(emailB);

        Assert.Equal(HttpStatusCode.OK, await LogoutAsync(a1));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(a1, "/api/auth/me"));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(a2, "/api/auth/me"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(b, "/api/auth/me"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-valid-token")]
    public async Task RejectedCsrfLogout_RevokesNothing(string? csrfToken)
    {
        var (email, _) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var sessionA = await LoginAsync(email);
        var sessionB = await LoginAsync(email);
        var stampBefore = await GetSecurityStampAsync(email);

        var status = await LogoutAsync(sessionA, csrfOverride: csrfToken ?? string.Empty, omitCsrf: csrfToken is null);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(stampBefore, await GetSecurityStampAsync(email));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(sessionA, "/api/auth/me"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(sessionB, "/api/auth/me"));
    }

    [Fact]
    public async Task Logout_StaysUsableDuringForcedPasswordChange_AndRevokesEverySession()
    {
        var (email, _) = await ApiTestUsers.CreateAsync(factory, "Administration");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await userManager.FindByEmailAsync(email))!;
            user.MustChangePassword = true;
            await userManager.UpdateAsync(user);
        }

        var sessionA = await LoginAsync(email);
        var sessionB = await LoginAsync(email);
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(sessionA, "/api/users"));

        Assert.Equal(HttpStatusCode.OK, await LogoutAsync(sessionA));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(sessionA, "/api/auth/me"));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(sessionB, "/api/auth/me"));
    }

    // --- helpers -----------------------------------------------------------

    private sealed record Session(string AuthCookie, string CsrfCookie, string CsrfToken);

    private HttpClient CreateRawClient() =>
        factory.CreateClient(new() { HandleCookies = false, BaseAddress = new Uri("https://localhost") });

    /// <summary>A real login, then the authenticated antiforgery token (ASP.NET binds it to the signed-in identity).</summary>
    private async Task<Session> LoginAsync(string email)
    {
        using var client = CreateRawClient();
        var (csrfCookie, token) = await GetCsrfAsync(client, authCookie: null, currentCsrfCookie: null);

        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = ApiTestUsers.DefaultPassword }),
        };
        login.Headers.Add("Cookie", csrfCookie);
        login.Headers.Add("X-CSRF-TOKEN", token);
        var response = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var authCookie = ReadSetCookie(response, AuthCookieName)!;

        var (authCsrfCookie, authToken) = await GetCsrfAsync(client, authCookie, csrfCookie);
        return new Session(authCookie, authCsrfCookie, authToken);
    }

    private static async Task<(string Cookie, string Token)> GetCsrfAsync(HttpClient client, string? authCookie, string? currentCsrfCookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/antiforgery/token");
        var cookies = string.Join("; ", new[] { authCookie, currentCsrfCookie }.Where(c => c is not null));
        if (cookies.Length > 0)
        {
            request.Headers.Add("Cookie", cookies);
        }

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var token = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;
        return (ReadSetCookie(response, CsrfCookieName) ?? currentCsrfCookie!, token);
    }

    private static string? ReadSetCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal))?.Split(';')[0]
            : null;

    private async Task<HttpStatusCode> LogoutAsync(Session session, string? csrfOverride = null, bool omitCsrf = false)
    {
        using var client = CreateRawClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout") { Content = JsonContent.Create<object?>(null) };
        request.Headers.Add("Cookie", $"{session.AuthCookie}; {session.CsrfCookie}");
        if (!omitCsrf)
        {
            request.Headers.Add("X-CSRF-TOKEN", csrfOverride ?? session.CsrfToken);
        }

        return (await client.SendAsync(request)).StatusCode;
    }

    private async Task<HttpStatusCode> GetAsync(Session session, string path)
    {
        using var client = CreateRawClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", session.AuthCookie);
        return (await client.SendAsync(request)).StatusCode;
    }

    private async Task<string?> GetSecurityStampAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.GetSecurityStampAsync((await userManager.FindByEmailAsync(email))!);
    }
}
