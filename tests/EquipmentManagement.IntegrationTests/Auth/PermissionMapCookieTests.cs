using System.Net;
using System.Security.Claims;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Authorization;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EquipmentManagement.IntegrationTests.Auth;

/// <summary>
/// Cookies issued under an older <see cref="RolePermissions"/> map (ADR-078).
/// A deployment can't be simulated by editing source mid-run, so these tests
/// build principals with the app's own claims factory, alter only the
/// permission-map claim, and protect them with the app's own cookie ticket
/// format. The real <c>ActiveUserCookieValidator</c> then judges them on a real
/// request.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class PermissionMapCookieTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string AuthCookieName = "EquipmentManagement.Auth";
    private const string Address = "Rua do Mapa 3, Lisboa";

    [Fact]
    public async Task CurrentMapFingerprint_AndCurrentStamp_IsAuthenticated()
    {
        var email = await CreateUserAsync("Administration");

        var status = await GetMeWithForgedCookieAsync(email, principal => principal);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task MissingMapFingerprint_IsRejected_PreExistingCookiesFailClosed()
    {
        var email = await CreateUserAsync("Administration");

        var status = await GetMeWithForgedCookieAsync(email, principal => WithMapClaim(principal, value: null));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task StaleMapFingerprint_IsRejected_OnTheFirstRequest()
    {
        var email = await CreateUserAsync("Administration");
        // A cookie issued while a different permission map was deployed.
        var oldMap = PermissionMapFingerprint.Compute(new Dictionary<string, IEnumerable<string>>
        {
            [Roles.Administration] = [.. RolePermissions.GetPermissions(Roles.Administration), "Legacy.Permission"],
            [Roles.HR] = RolePermissions.GetPermissions(Roles.HR),
        });
        Assert.NotEqual(RolePermissions.Fingerprint, oldMap);

        var status = await GetMeWithForgedCookieAsync(email, principal => WithMapClaim(principal, oldMap));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task CurrentMapFingerprint_ButBadSecurityStamp_IsRejected()
    {
        var email = await CreateUserAsync("Administration");

        var status = await GetMeWithForgedCookieAsync(email, principal =>
        {
            var identity = (ClaimsIdentity)principal.Identity!;
            var stampType = factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity.SecurityStampClaimType;
            identity.RemoveClaim(identity.FindFirst(stampType)!);
            identity.AddClaim(new Claim(stampType, "NOT-THE-CURRENT-STAMP"));
            return principal;
        });

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task CurrentMapFingerprint_ButInactiveUser_IsRejected()
    {
        var email = await CreateUserAsync("HR");
        var cookie = await ForgeCookieAsync(email, principal => principal);
        await ApiTestUsers.SetActiveAsync(factory, email, isActive: false);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetMeAsync(cookie));
    }

    [Theory]
    [InlineData("Administration", HttpStatusCode.OK)]
    [InlineData("HR", HttpStatusCode.Forbidden)]
    public async Task FreshLogin_CarriesTheCurrentFingerprint_AndExactlyTheRolesCurrentPermissions(string role, HttpStatusCode usersList)
    {
        var email = await CreateUserAsync(role);
        using var client = factory.CreateClient(new() { HandleCookies = false, BaseAddress = new Uri("https://localhost") });
        var csrf = await client.GetAsync("/api/antiforgery/token");
        var csrfCookie = csrf.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("EquipmentManagement.Csrf=", StringComparison.Ordinal)).Split(';')[0];
        var token = System.Text.Json.JsonDocument.Parse(await csrf.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;

        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { email, password = ApiTestUsers.DefaultPassword }),
        };
        login.Headers.Add("Cookie", csrfCookie);
        login.Headers.Add("X-CSRF-TOKEN", token);
        var response = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var authCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthCookieName + "=", StringComparison.Ordinal)).Split(';')[0];
        var ticket = TicketFormat().Unprotect(authCookie[(AuthCookieName.Length + 1)..])!;
        Assert.Equal(RolePermissions.Fingerprint, ticket.Principal.FindFirstValue(ApplicationClaimsPrincipalFactory.PermissionMapClaimType));
        Assert.Equal(
            RolePermissions.GetPermissions(role).Order(StringComparer.Ordinal),
            ticket.Principal.FindAll(PermissionAuthorizationHandler.PermissionClaimType).Select(c => c.Value).Order(StringComparer.Ordinal));

        Assert.Equal(HttpStatusCode.OK, await GetAsync(authCookie, "/api/auth/me"));
        Assert.Equal(usersList, await GetAsync(authCookie, "/api/users"));
        Assert.Equal(usersList, await GetAsync(authCookie, "/api/audit"));
    }

    // --- helpers -----------------------------------------------------------

    private async Task<string> CreateUserAsync(string role)
    {
        var (email, _) = await ApiTestUsers.CreateAsync(factory, role, address: Address);
        return email;
    }

    private async Task<HttpStatusCode> GetMeWithForgedCookieAsync(string email, Func<ClaimsPrincipal, ClaimsPrincipal> alter) =>
        await GetMeAsync(await ForgeCookieAsync(email, alter));

    /// <summary>Exactly what a login would issue for this user now, after <paramref name="alter"/>, as a protected auth cookie.</summary>
    private async Task<string> ForgeCookieAsync(string email, Func<ClaimsPrincipal, ClaimsPrincipal> alter)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var claimsFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email) ?? throw new InvalidOperationException("Test user not found.");

        var principal = alter(await claimsFactory.CreateAsync(user));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties(), IdentityConstants.ApplicationScheme);
        return $"{AuthCookieName}={TicketFormat().Protect(ticket)}";
    }

    private ISecureDataFormat<AuthenticationTicket> TicketFormat() =>
        factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme).TicketDataFormat;

    private static ClaimsPrincipal WithMapClaim(ClaimsPrincipal principal, string? value)
    {
        var identity = (ClaimsIdentity)principal.Identity!;
        foreach (var claim in identity.FindAll(ApplicationClaimsPrincipalFactory.PermissionMapClaimType).ToList())
        {
            identity.RemoveClaim(claim);
        }

        if (value is not null)
        {
            identity.AddClaim(new Claim(ApplicationClaimsPrincipalFactory.PermissionMapClaimType, value));
        }

        return principal;
    }

    private Task<HttpStatusCode> GetMeAsync(string cookie) => GetAsync(cookie, "/api/auth/me");

    private async Task<HttpStatusCode> GetAsync(string cookie, string path)
    {
        using var client = factory.CreateClient(new() { HandleCookies = false, BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return (await client.SendAsync(request)).StatusCode;
    }
}
