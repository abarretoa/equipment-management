using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Auth;
using EquipmentManagement.Api.Contracts.Users;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Users;

/// <summary>
/// Administration-only user management against a real PostgreSQL database
/// (Phase 1 scope). Every mutating call
/// goes through an authenticated Administration session with a real CSRF token —
/// this exercises the exact same path the frontend will use, not a shortcut.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class UserManagementTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task CreateHrUser_WithAddress_Succeeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var email = $"hr.{Guid.NewGuid():N}@example.invalid";

        var response = await client.PostWithCsrfAsync("/api/users", new
        {
            FirstName = "Nova",
            LastName = "Colaboradora",
            Email = email,
            Phone = "+351911111111",
            Role = "HR",
            Address = "Avenida Exemplo 1, Lisboa",
        }, csrfToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreatedUserResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.NotNull(body);
        Assert.Equal("HR", body!.User.Role);
        Assert.True(body.User.MustChangePassword);
        Assert.NotEmpty(body.TemporaryPassword);
    }

    [Fact]
    public async Task CreateUser_ConcurrentSameEmail_OnlyOneSucceeds()
    {
        // Regression test: UserManagementService.CreateAsync only pre-checked
        // FindByEmailAsync (a TOCTOU check) before calling userManager.CreateAsync.
        // ASP.NET Core Identity's own UserStore does not catch a real unique-index
        // violation, so a genuine concurrent duplicate used to surface as an
        // unhandled 500 instead of the expected 409 (see PostgresConflictClassifier
        // and UserManagementService.CreateAsync's new try/catch).
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();
        var email = $"race.{Guid.NewGuid():N}@example.invalid";

        var csrfA = await clientA.GetAntiforgeryTokenAsync();
        var csrfB = await clientB.GetAntiforgeryTokenAsync();

        object NewUserPayload() => new
        {
            FirstName = "Race",
            LastName = "Condition",
            Email = email,
            Phone = "+351911111113",
            Role = "HR",
            Address = "Avenida Exemplo 2, Lisboa",
        };

        var results = await Task.WhenAll(
            clientA.PostWithCsrfAsync("/api/users", NewUserPayload(), csrfA),
            clientB.PostWithCsrfAsync("/api/users", NewUserPayload(), csrfB));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task CreateHrUser_WithoutAddress_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync("/api/users", new
        {
            FirstName = "Sem",
            LastName = "Endereco",
            Email = $"hr.{Guid.NewGuid():N}@example.invalid",
            Phone = "+351911111112",
            Role = "HR",
            Address = (string?)null,
        }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_DuplicateEmail_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var email = $"dup.{Guid.NewGuid():N}@example.invalid";
        var payload = new
        {
            FirstName = "Primeiro",
            LastName = "Utilizador",
            Email = email,
            Phone = "+351911111113",
            Role = "Administration",
            Address = (string?)null,
        };

        var first = await client.PostWithCsrfAsync("/api/users", payload, csrfToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostWithCsrfAsync("/api/users", payload, csrfToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        await AssertProblemTitleAsync(second, "Já existe um utilizador com este e-mail.");
    }

    [Fact]
    public async Task CreateUser_InvalidRole_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync("/api/users", new
        {
            FirstName = "Invalido",
            LastName = "Cargo",
            Email = $"invalid.{Guid.NewGuid():N}@example.invalid",
            Phone = "+351911111114",
            Role = "SuperAdmin",
            Address = (string?)null,
        }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemTitleAsync(response, "'SuperAdmin' não é uma função suportada.");
    }

    [Fact]
    public async Task DeactivateViaApi_PreventsFutureLogin()
    {
        using var adminClient = await LoginAsNewAdministratorAsync();
        var (targetEmail, targetPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Alvo 1, Setubal");
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);
        var csrfToken = await adminClient.GetAntiforgeryTokenAsync();

        var deactivateResponse = await adminClient.PostWithCsrfAsync<object?>($"/api/users/{targetId}/deactivate", null, csrfToken);
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

        using var targetClient = factory.CreateClient();
        var targetCsrf = await targetClient.GetAntiforgeryTokenAsync();
        var loginResponse = await targetClient.PostWithCsrfAsync(
            "/api/auth/login", new { email = targetEmail, password = targetPassword }, targetCsrf);

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    [Fact]
    public async Task AdminPasswordReset_IssuesNewTemporaryPasswordAndForcesChange()
    {
        using var adminClient = await LoginAsNewAdministratorAsync();
        var (targetEmail, oldPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Reset 1, Aveiro");
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);
        var csrfToken = await adminClient.GetAntiforgeryTokenAsync();

        var resetResponse = await adminClient.PostWithCsrfAsync<object?>($"/api/users/{targetId}/reset-password", null, csrfToken);

        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
        var body = await resetResponse.Content.ReadFromJsonAsync<ResetPasswordResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.NotNull(body);
        Assert.NotEmpty(body!.TemporaryPassword);

        using var targetClient = factory.CreateClient();

        // The old password no longer works.
        var oldCsrf = await targetClient.GetAntiforgeryTokenAsync();
        var oldLoginResponse = await targetClient.PostWithCsrfAsync(
            "/api/auth/login", new { email = targetEmail, password = oldPassword }, oldCsrf);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLoginResponse.StatusCode);

        // The new temporary password works and requires a password change.
        var newCsrf = await targetClient.GetAntiforgeryTokenAsync();
        var newLoginResponse = await targetClient.PostWithCsrfAsync(
            "/api/auth/login", new { email = targetEmail, password = body.TemporaryPassword }, newCsrf);
        Assert.Equal(HttpStatusCode.OK, newLoginResponse.StatusCode);

        var meAfterReset = await targetClient.GetFromJsonAsync<CurrentUserResponse>(
            "/api/auth/me", HttpClientTestExtensions.JsonOptions);
        Assert.True(meAfterReset!.MustChangePassword);

        var blockedResponse = await targetClient.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, blockedResponse.StatusCode);
    }

    [Fact]
    public async Task Hr_CannotResetAnotherUsersPassword()
    {
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Sem Permissao 1, Lisboa");
        var (targetEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Alvo 2, Lisboa");
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);

        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);
        var csrfToken = await hrClient.GetAntiforgeryTokenAsync();

        var response = await hrClient.PostWithCsrfAsync<object?>($"/api/users/{targetId}/reset-password", null, csrfToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_LastActiveAdministrator_IsRejected()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var userId = await ApiTestUsers.GetIdAsync(factory, email);
        var others = await ApiTestUsers.GetOtherActiveAdministratorEmailsAsync(factory, email);

        foreach (var other in others)
        {
            await ApiTestUsers.SetActiveAsync(factory, other, isActive: false);
        }

        try
        {
            using var client = factory.CreateClient();
            await client.LoginAsync(email, password);
            var csrfToken = await client.GetAntiforgeryTokenAsync();

            var response = await client.PostWithCsrfAsync<object?>($"/api/users/{userId}/deactivate", null, csrfToken);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            await AssertProblemTitleAsync(response,
                "Este é o último utilizador ativo da Administração; atribua a função de Administração a outro utilizador antes de o desativar.");
        }
        finally
        {
            foreach (var other in others)
            {
                await ApiTestUsers.SetActiveAsync(factory, other, isActive: true);
            }
        }
    }

    [Fact]
    public async Task ChangeRole_OfLastActiveAdministrator_AwayFromAdministration_IsRejected()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var userId = await ApiTestUsers.GetIdAsync(factory, email);
        var others = await ApiTestUsers.GetOtherActiveAdministratorEmailsAsync(factory, email);

        foreach (var other in others)
        {
            await ApiTestUsers.SetActiveAsync(factory, other, isActive: false);
        }

        try
        {
            using var client = factory.CreateClient();
            await client.LoginAsync(email, password);
            var csrfToken = await client.GetAntiforgeryTokenAsync();

            var response = await client.PutWithCsrfAsync($"/api/users/{userId}/role", new { Role = "HR" }, csrfToken);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            await AssertProblemTitleAsync(response,
                "Este é o último utilizador ativo da Administração; atribua primeiro a função de Administração a outro utilizador.");
        }
        finally
        {
            foreach (var other in others)
            {
                await ApiTestUsers.SetActiveAsync(factory, other, isActive: true);
            }
        }
    }

    [Fact]
    public async Task ConcurrentMutualDeactivation_OfTheOnlyTwoActiveAdministrators_NeverLeavesZeroActive()
    {
        // Regression test: the last-Administration safeguard's Serializable
        // transaction previously caught only a raw PostgresException. With
        // exactly two active Administrators, having each concurrently
        // deactivate the OTHER is a textbook write-skew scenario — both
        // requests read "1 other active admin" (each other) and proceed, so
        // only Postgres's Serializable isolation (not the application-level
        // count check alone) can prevent zero active Administrators from
        // resulting. The exact conflict this produces can be a raw
        // PostgresException or a DbUpdateException-wrapped one depending on
        // which of the two userManager.UpdateAsync calls detects it (see
        // PostgresConflictClassifier).
        var (emailA, passwordA) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var (emailB, passwordB) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var idA = await ApiTestUsers.GetIdAsync(factory, emailA);
        var idB = await ApiTestUsers.GetIdAsync(factory, emailB);

        var others = await ApiTestUsers.GetOtherActiveAdministratorEmailsAsync(factory, emailA);
        others.Remove(emailB);

        foreach (var other in others)
        {
            await ApiTestUsers.SetActiveAsync(factory, other, isActive: false);
        }

        try
        {
            using var clientA = factory.CreateClient();
            await clientA.LoginAsync(emailA, passwordA);
            using var clientB = factory.CreateClient();
            await clientB.LoginAsync(emailB, passwordB);

            var csrfA = await clientA.GetAntiforgeryTokenAsync();
            var csrfB = await clientB.GetAntiforgeryTokenAsync();

            var results = await Task.WhenAll(
                clientA.PostWithCsrfAsync<object?>($"/api/users/{idB}/deactivate", null, csrfA),
                clientB.PostWithCsrfAsync<object?>($"/api/users/{idA}/deactivate", null, csrfB));

            // Whichever way the race resolves, exactly one request must win
            // and the other must be refused safely — never a 500 and never
            // both succeeding (which would leave zero active Administrators).
            // The refusal is a 409 when both requests passed authentication
            // and the safeguard or a serialization conflict stopped the
            // second; it is a 401 when the winner committed first, because
            // the loser's own account is by then inactive and its session is
            // rejected at authentication (ActiveUserCookieValidator). Which
            // of the two happens is pure timing, so both are accepted.
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, results.Count(r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Unauthorized));

            var stillActive = await ApiTestUsers.GetOtherActiveAdministratorEmailsAsync(factory, "nonexistent@example.invalid");
            Assert.Contains(stillActive, e => e == emailA || e == emailB);
        }
        finally
        {
            await ApiTestUsers.SetActiveAsync(factory, emailA, isActive: true);
            await ApiTestUsers.SetActiveAsync(factory, emailB, isActive: true);
            foreach (var other in others)
            {
                await ApiTestUsers.SetActiveAsync(factory, other, isActive: true);
            }
        }
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }

    /// <summary>Application-defined user-management messages are pt-PT (shown verbatim by the UI).</summary>
    private static async Task AssertProblemTitleAsync(HttpResponseMessage response, string expected)
    {
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(expected, problem.GetProperty("title").GetString());
    }
}
