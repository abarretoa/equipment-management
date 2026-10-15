using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentManagement.Api.Contracts.Auth;
using EquipmentManagement.Api.Contracts.Profile;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Profile;

/// <summary>
/// "Meu perfil" — <c>GET/PUT /api/profile</c> (ADR-073) against a real PostgreSQL
/// database: current-user-only by construction, no mass assignment of
/// email/role/flags, the existing HR address rule, CSRF, session validity and a
/// field-names-only audit entry.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class ProfileTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string HrAddress = "Rua do Perfil 10, Porto";

    // --- Authentication ---------------------------------------------------

    [Fact]
    public async Task Get_WhenAnonymous_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_WhenAnonymous_Returns401()
    {
        using var client = factory.CreateClient();
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync("/api/profile", ValidUpdate(), csrfToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_AfterDeactivation_ExistingSessionIsRejected()
    {
        var (client, email, _) = await LoginAsNewUserAsync(Roles.HR, HrAddress);
        using var _client = client;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/profile")).StatusCode);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        await ApiTestUsers.SetActiveAsync(factory, email, isActive: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutWithCsrfAsync("/api/profile", ValidUpdate(HrAddress), csrfToken)).StatusCode);
    }

    [Fact]
    public async Task Get_WhileMustChangePassword_IsBlockedLikeEveryOtherEndpoint()
    {
        var (client, _, userId) = await LoginAsNewUserAsync(Roles.Administration);
        using var _client = client;
        await MutateUserAsync(userId, u => u.MustChangePassword = true);

        var response = await client.GetAsync("/api/profile");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- GET ----------------------------------------------------------------

    [Fact]
    public async Task Get_AsAdministration_ReturnsOwnProfileWithOnlyTheScreenFields()
    {
        var (client, email, _) = await LoginAsNewUserAsync(Roles.Administration);
        using var _client = client;

        var response = await client.GetAsync("/api/profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var propertyNames = json.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(["address", "email", "firstName", "lastName", "phoneNumber", "role"], propertyNames);

        var body = json.Deserialize<ProfileResponse>(HttpClientTestExtensions.JsonOptions)!;
        Assert.Equal(email, body.Email);
        Assert.Equal("Test", body.FirstName);
        Assert.Equal(Roles.Administration, body.LastName);
        Assert.Equal("+351900000000", body.PhoneNumber);
        Assert.Null(body.Address);
        Assert.Equal(Roles.Administration, body.Role);
    }

    [Fact]
    public async Task Get_AsHr_ReturnsOwnProfile()
    {
        var (client, email, _) = await LoginAsNewUserAsync(Roles.HR, HrAddress);
        using var _client = client;

        var body = await client.GetFromJsonAsync<ProfileResponse>("/api/profile", HttpClientTestExtensions.JsonOptions);

        Assert.Equal(email, body!.Email);
        Assert.Equal(Roles.HR, body.Role);
        Assert.Equal(HrAddress, body.Address);
    }

    [Fact]
    public async Task Get_WithAUserIdInTheRoute_IsNotAnEndpoint()
    {
        var (client, _, _) = await LoginAsNewUserAsync(Roles.Administration);
        using var _client = client;
        var otherId = await CreateUserIdAsync(Roles.HR, HrAddress);

        var response = await client.GetAsync($"/api/profile/{otherId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- PUT: allowed edits -------------------------------------------------

    [Fact]
    public async Task Put_AsAdministration_UpdatesOwnEditableFields()
    {
        var (client, email, userId) = await LoginAsNewUserAsync(Roles.Administration);
        using var _client = client;
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync("/api/profile", new
        {
            FirstName = "Ana",
            LastName = "Administradora",
            Phone = "+351911222333",
            Address = "Avenida Nova 5, Lisboa",
        }, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProfileResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Equal(new ProfileResponse("Ana", "Administradora", email, "+351911222333", "Avenida Nova 5, Lisboa", Roles.Administration), body);

        var stored = await LoadUserAsync(userId);
        Assert.Equal("Ana", stored.User.FirstName);
        Assert.Equal("Administradora", stored.User.LastName);
        Assert.Equal("+351911222333", stored.User.PhoneNumber);
        Assert.Equal("Avenida Nova 5, Lisboa", stored.User.Address);
    }

    [Fact]
    public async Task Put_AsHr_UpdatesOwnEditableFields()
    {
        var (client, _, userId) = await LoginAsNewUserAsync(Roles.HR, HrAddress);
        using var _client = client;
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync("/api/profile", new
        {
            FirstName = "Ana",
            LastName = "Recursos",
            Phone = "+351922333444",
            Address = "Rua Alterada 7, Porto",
        }, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await LoadUserAsync(userId);
        Assert.Equal("Ana", stored.User.FirstName);
        Assert.Equal("Recursos", stored.User.LastName);
        Assert.Equal("+351922333444", stored.User.PhoneNumber);
        Assert.Equal("Rua Alterada 7, Porto", stored.User.Address);
        Assert.Equal(Roles.HR, stored.Role);
    }

    [Fact]
    public async Task Put_AsAdministration_MayClearAddress_LikeTheAdministrativeEdit()
    {
        var (client, _, userId) = await LoginAsNewUserAsync(Roles.Administration, address: "Rua Antiga 1, Lisboa");
        using var _client = client;
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync("/api/profile", ValidUpdate(address: null), csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await LoadUserAsync(userId)).User.Address);
    }

    [Fact]
    public async Task Put_ChangesOnlyTheCallersAccount()
    {
        var (client, _, userId) = await LoginAsNewUserAsync(Roles.HR, HrAddress);
        using var _client = client;
        var otherId = await CreateUserIdAsync(Roles.HR, "Rua da Outra Pessoa 2, Faro");
        var otherBefore = await LoadUserAsync(otherId);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync("/api/profile", ValidUpdate(HrAddress), csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Alterado", (await LoadUserAsync(userId)).User.FirstName);
        AssertSameAccount(otherBefore, await LoadUserAsync(otherId));
    }

    [Fact]
    public async Task Put_IgnoresIdEmailRoleAndAccountFlagsSmuggledIntoTheBody()
    {
        // HR caller trying to make itself an active, password-reset-free
        // Administration under another email — and to redirect the edit to
        // another user by id. None of these properties exist on
        // UpdateProfileRequest; this asserts the actual API outcome.
        var (client, email, userId) = await LoginAsNewUserAsync(Roles.HR, HrAddress);
        using var _client = client;
        var otherId = await CreateUserIdAsync(Roles.Administration);
        var otherBefore = await LoadUserAsync(otherId);
        var before = await LoadUserAsync(userId);
        var meBefore = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me", HttpClientTestExtensions.JsonOptions);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync("/api/profile", new
        {
            Id = otherId,
            UserId = otherId,
            FirstName = "Escalada",
            LastName = "Tentativa",
            Phone = "+351933444555",
            Address = HrAddress,
            Email = "attacker@example.invalid",
            UserName = "attacker@example.invalid",
            Role = Roles.Administration,
            Roles = new[] { Roles.Administration },
            Permissions = Permissions.All,
            IsActive = false,
            MustChangePassword = true,
            EmailConfirmed = false,
            SecurityStamp = "forged",
            ConcurrencyStamp = "forged",
            PasswordHash = "forged",
        }, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProfileResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Equal(email, body!.Email);
        Assert.Equal(Roles.HR, body.Role);

        var after = await LoadUserAsync(userId);
        Assert.Equal("Escalada", after.User.FirstName);
        Assert.Equal(email, after.User.Email);
        Assert.Equal(email, after.User.UserName);
        Assert.Equal(Roles.HR, after.Role);
        Assert.True(after.User.IsActive);
        Assert.False(after.User.MustChangePassword);
        Assert.True(after.User.EmailConfirmed);
        Assert.Equal(before.User.SecurityStamp, after.User.SecurityStamp);
        Assert.Equal(before.User.PasswordHash, after.User.PasswordHash);

        AssertSameAccount(otherBefore, await LoadUserAsync(otherId));

        // Same session, same permissions — the cookie is still valid and the
        // permission set is still HR's.
        var meAfter = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(Roles.HR, meAfter!.Role);
        Assert.Equal(meBefore!.Permissions.Order(), meAfter.Permissions.Order());
        Assert.Equal(RolePermissions.GetPermissions(Roles.HR).Order(), meAfter.Permissions.Order());
    }

    [Fact]
    public async Task Put_WithAUserIdInTheRoute_IsNotAnEndpoint()
    {
        var (client, _, _) = await LoginAsNewUserAsync(Roles.Administration);
        using var _client = client;
        var otherId = await CreateUserIdAsync(Roles.HR, HrAddress);
        var otherBefore = await LoadUserAsync(otherId);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync($"/api/profile/{otherId}", ValidUpdate(HrAddress), csrfToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertSameAccount(otherBefore, await LoadUserAsync(otherId));
    }

    [Fact]
    public async Task Put_KeepsTheSessionValidAndMeReflectsTheNewName()
    {
        var (client, _, userId) = await LoginAsNewUserAsync(Roles.Administration);
        using var _client = client;
        var stampBefore = (await LoadUserAsync(userId)).User.SecurityStamp;
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync("/api/profile", ValidUpdate(), csrfToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var meBody = await me.Content.ReadFromJsonAsync<CurrentUserResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Equal("Alterado", meBody!.FirstName);
        Assert.Equal(stampBefore, (await LoadUserAsync(userId)).User.SecurityStamp);

        // A second mutation in the same session with the same CSRF token still works.
        var second = await client.PutWithCsrfAsync("/api/profile", ValidUpdate() with { FirstName = "Outra" }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    // --- PUT: rejected ------------------------------------------------------

    [Fact]
    public async Task Put_WithoutCsrfToken_IsRejectedAndChangesNothing()
    {
        var (client, _, userId) = await LoginAsNewUserAsync(Roles.Administration);
        using var _client = client;

        var response = await client.PutAsJsonAsync("/api/profile", ValidUpdate());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("CSRF", problem.GetProperty("title").GetString());
        Assert.Equal("Test", (await LoadUserAsync(userId)).User.FirstName);
    }

    public static TheoryData<string, string, string, string?, string?> InvalidUpdates => new()
    {
        { "FirstName", "", "Apelido", null, null },
        { "FirstName", "   ", "Apelido", null, null },
        { "LastName", "Nome", "", null, null },
        { "FirstName", new string('a', 201), "Apelido", null, null },
        { "LastName", "Nome", new string('a', 201), null, null },
        { "Phone", "Nome", "Apelido", "not-a-phone", null },
        { "Phone", "Nome", "Apelido", "abc", null },
        { "Address", "Nome", "Apelido", null, new string('a', 501) },
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Put_WithoutAPhone_ClearsTheStoredPhone_AndAuditsOnlyThatField(string? phone)
    {
        // An empty or blank phone means "no phone" (it used to be a 400).
        var (client, email, userId) = await LoginAsNewUserAsync(Roles.HR, HrAddress);
        using var _client = client;
        var before = await LoadUserAsync(userId);
        Assert.NotNull(before.User.PhoneNumber);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync("/api/profile",
            new { FirstName = "Test", LastName = Roles.HR, Phone = phone, Address = HrAddress }, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProfileResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Null(body!.PhoneNumber);

        var after = await LoadUserAsync(userId);
        Assert.Null(after.User.PhoneNumber);
        Assert.Equal(email, after.User.Email);
        Assert.Equal(HrAddress, after.User.Address);
        Assert.Equal(Roles.HR, after.Role);
        Assert.Equal(before.User.SecurityStamp, after.User.SecurityStamp);

        var entry = Assert.Single(await LoadProfileAuditsAsync(userId));
        var changedFields = JsonDocument.Parse(entry.MetadataJson!).RootElement.GetProperty("changedFields")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["phoneNumber"], changedFields);
    }

    [Fact]
    public async Task Put_WithA500CharacterAddress_Succeeds()
    {
        var (client, _, userId) = await LoginAsNewUserAsync(Roles.HR, HrAddress);
        using var _client = client;
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var address = new string('a', 500);

        var response = await client.PutWithCsrfAsync("/api/profile", ValidUpdate(address), csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(address, (await LoadUserAsync(userId)).User.Address);
    }

    [Theory]
    [MemberData(nameof(InvalidUpdates))]
    public async Task Put_WithInvalidValues_Returns400ValidationProblem_AndNoAudit(
        string expectedErrorKey, string firstName, string lastName, string? phone, string? address)
    {
        var (client, _, userId) = await LoginAsNewUserAsync(Roles.Administration);
        using var _client = client;
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync("/api/profile", new { firstName, lastName, phone, address }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(expectedErrorKey, out _));
        Assert.Equal("Test", (await LoadUserAsync(userId)).User.FirstName);
        Assert.Empty(await LoadProfileAuditsAsync(userId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Put_AsHr_WithoutAddress_IsRejectedOnTheAddressField(string? address)
    {
        var (client, _, userId) = await LoginAsNewUserAsync(Roles.HR, HrAddress);
        using var _client = client;
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync("/api/profile", ValidUpdate(address), csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("Address", out _));
        var stored = await LoadUserAsync(userId);
        Assert.Equal(HrAddress, stored.User.Address);
        Assert.Equal("Test", stored.User.FirstName);
        Assert.Empty(await LoadProfileAuditsAsync(userId));
    }

    // --- Audit --------------------------------------------------------------

    [Fact]
    public async Task Put_RecordsOneAuditEntryWithChangedFieldNamesOnly()
    {
        var (client, email, userId) = await LoginAsNewUserAsync(Roles.HR, HrAddress);
        using var _client = client;
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync("/api/profile", new
        {
            FirstName = "Test",
            LastName = "Novoapelido",
            Phone = "+351966777888",
            Address = "Travessa Sintética 99, Coimbra",
        }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entry = Assert.Single(await LoadProfileAuditsAsync(userId));
        Assert.Equal("User", entry.EntityType);
        Assert.Equal(userId, entry.ActorUserId);
        Assert.Equal("Test HR", entry.ActorDisplayNameSnapshot);
        Assert.Null(entry.OldValuesJson);
        Assert.Null(entry.NewValuesJson);

        var metadata = JsonDocument.Parse(entry.MetadataJson!).RootElement;
        var changedFields = metadata.GetProperty("changedFields").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["lastName", "phoneNumber", "address"], changedFields);

        foreach (var value in new[] { "966777888", "Travessa Sintética", "Coimbra", "Novoapelido", "900000000", "Rua do Perfil", email })
        {
            Assert.DoesNotContain(value, entry.MetadataJson);
        }

        // Re-saving identical values is not an edit: no second entry.
        var unchanged = await client.PutWithCsrfAsync("/api/profile", new
        {
            FirstName = "Test",
            LastName = "Novoapelido",
            Phone = "+351966777888",
            Address = "Travessa Sintética 99, Coimbra",
        }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.Single(await LoadProfileAuditsAsync(userId));
        Assert.Empty(await LoadAuditsAsync(userId, AuditActions.User.Updated));
    }

    // --- helpers ------------------------------------------------------------

    private sealed record ProfileUpdate(string FirstName, string LastName, string? Phone, string? Address);

    private static ProfileUpdate ValidUpdate(string? address = null) =>
        new("Alterado", "Utilizador", "+351912345678", address);

    private async Task<(HttpClient Client, string Email, Guid UserId)> LoginAsNewUserAsync(string role, string? address = null)
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, role, address);
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return (client, email, await ApiTestUsers.GetIdAsync(factory, email));
    }

    private async Task<Guid> CreateUserIdAsync(string role, string? address = null)
    {
        var (email, _) = await ApiTestUsers.CreateAsync(factory, role, address);
        return await ApiTestUsers.GetIdAsync(factory, email);
    }

    private async Task<(ApplicationUser User, string Role)> LoadUserAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("Test user not found.");
        return (user, (await userManager.GetRolesAsync(user)).Single());
    }

    private async Task MutateUserAsync(Guid userId, Action<ApplicationUser> mutate)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("Test user not found.");
        mutate(user);
        await userManager.UpdateAsync(user);
    }

    private Task<List<AuditLog>> LoadProfileAuditsAsync(Guid userId) => LoadAuditsAsync(userId, AuditActions.User.ProfileUpdated);

    private async Task<List<AuditLog>> LoadAuditsAsync(Guid userId, string action)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entityId = userId.ToString();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityId == entityId && a.Action == action)
            .ToListAsync();
    }

    private static void AssertSameAccount((ApplicationUser User, string Role) expected, (ApplicationUser User, string Role) actual)
    {
        Assert.Equal(expected.User.FirstName, actual.User.FirstName);
        Assert.Equal(expected.User.LastName, actual.User.LastName);
        Assert.Equal(expected.User.Email, actual.User.Email);
        Assert.Equal(expected.User.PhoneNumber, actual.User.PhoneNumber);
        Assert.Equal(expected.User.Address, actual.User.Address);
        Assert.Equal(expected.User.IsActive, actual.User.IsActive);
        Assert.Equal(expected.User.MustChangePassword, actual.User.MustChangePassword);
        Assert.Equal(expected.User.SecurityStamp, actual.User.SecurityStamp);
        Assert.Equal(expected.User.ConcurrencyStamp, actual.User.ConcurrencyStamp);
        Assert.Equal(expected.Role, actual.Role);
    }
}
