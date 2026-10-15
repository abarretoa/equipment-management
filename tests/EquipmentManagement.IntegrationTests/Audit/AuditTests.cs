using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.Api.Contracts.Users;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Audit;

/// <summary>
/// The audit trail actually gets written for critical Phase 1 operations, never
/// carries sensitive Identity fields, and is reachable only read-only
/// (docs/PROJECT_SPEC.md, section 15; Phase 1 scope).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class AuditTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string[] ForbiddenSubstrings = ["PasswordHash", "SecurityStamp", "ConcurrencyStamp"];

    [Fact]
    public async Task LoginSucceededAndFailed_BothRecordAuditEvents()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        using var client = factory.CreateClient();

        await client.LoginAsync(email, password);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        await client.PostWithCsrfAsync("/api/auth/login", new { email, password = "TotallyWrongPassword123!" }, csrfToken);

        var userId = await ApiTestUsers.GetIdAsync(factory, email);
        var auditResponse = await client.GetAsync($"/api/audit?entityId={userId}&entityType=User");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        Assert.Contains(page!.Items, e => e.Action == "Auth.LoginSucceeded");
        Assert.Contains(page.Items, e => e.Action == "Auth.LoginFailed");
    }

    [Fact]
    public async Task CreatingAUser_RecordsAnAuditEvent()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var email = $"hr.{Guid.NewGuid():N}@example.invalid";

        var createResponse = await client.PostWithCsrfAsync("/api/users", new
        {
            FirstName = "Auditada",
            LastName = "Pessoa",
            Email = email,
            Phone = "+351922222221",
            Role = "HR",
            Address = "Rua da Auditoria 1, Lisboa",
        }, csrfToken);
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedUserResponse>(HttpClientTestExtensions.JsonOptions);

        var auditResponse = await client.GetAsync($"/api/audit?entityType=User&entityId={created!.User.Id}&action=User.Created");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        Assert.NotNull(page);
        var entry = Assert.Single(page!.Items, e => e.Action == "User.Created" && e.EntityId == created.User.Id.ToString());

        // Phase 10 hardening: PhoneNumber/Address must never appear as values in
        // any audit property — only whether they were provided.
        foreach (var json in new[] { entry.OldValuesJson, entry.NewValuesJson, entry.MetadataJson })
        {
            Assert.DoesNotContain("922222221", json ?? string.Empty);
            Assert.DoesNotContain("Rua da Auditoria", json ?? string.Empty);
        }

        var newValues = JsonDocument.Parse(entry.NewValuesJson!).RootElement;
        Assert.True(newValues.GetProperty("hasPhoneNumber").GetBoolean());
        Assert.True(newValues.GetProperty("hasAddress").GetBoolean());
        Assert.DoesNotContain("922222221", entry.NewValuesJson);
        Assert.DoesNotContain("Rua da Auditoria", entry.NewValuesJson);
    }

    [Fact]
    public async Task UpdatingAUser_RecordsChangedFieldNamesOnly_NeverPersonalValues()
    {
        // Previously this event stored {field: {old, new}} — raw names, phone and
        // address. It must name the changed fields and carry no values at all.
        using var client = await LoginAsNewAdministratorAsync();
        var (targetEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Original 1, Lisboa");
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var updateResponse = await client.PutWithCsrfAsync($"/api/users/{targetId}", new
        {
            FirstName = "Leonor",
            LastName = "Teste",
            Phone = "+351933333331",
            Address = "Rua Nova 2, Lisboa",
        }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var auditResponse = await client.GetAsync($"/api/audit?entityType=User&entityId={targetId}&action=User.Updated");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        var entry = Assert.Single(page!.Items);
        Assert.Null(entry.OldValuesJson);
        Assert.Null(entry.NewValuesJson);

        var metadata = JsonDocument.Parse(entry.MetadataJson!).RootElement;
        Assert.Equal(["changedFields"], metadata.EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            ["firstName", "lastName", "phoneNumber", "address"],
            metadata.GetProperty("changedFields").EnumerateArray().Select(e => e.GetString()!).ToArray());

        string[] personalValues =
        [
            "Leonor", "Teste", "933333331", "Rua Nova", // new values
            "900000000", "Rua Original", targetEmail,       // old values (ApiTestUsers fixture) and email
        ];
        foreach (var value in personalValues)
        {
            Assert.DoesNotContain(value, entry.MetadataJson);
        }
    }

    [Fact]
    public async Task UpdatingAUser_WithOnlyAPhoneChange_ListsOnlyThatField()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (targetEmail, _) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        // ApiTestUsers creates "Test" / "Administration" / +351900000000 / no address.
        var updateResponse = await client.PutWithCsrfAsync($"/api/users/{targetId}", new
        {
            FirstName = "Test",
            LastName = "Administration",
            Phone = "",
            Address = (string?)null,
        }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var auditResponse = await client.GetAsync($"/api/audit?entityType=User&entityId={targetId}&action=User.Updated");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        var entry = Assert.Single(page!.Items);
        var changedFields = JsonDocument.Parse(entry.MetadataJson!).RootElement.GetProperty("changedFields")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["phoneNumber"], changedFields);
        Assert.DoesNotContain("900000000", entry.MetadataJson);
    }

    [Fact]
    public async Task ChangingRole_RecordsAnAuditEvent()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (targetEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Cargo 1, Lisboa");
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);
        // Give the future Administration account an address too — HR's address
        // requirement is checked on the CURRENT role, not the target one, but
        // this keeps the fixture realistic.
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var roleResponse = await client.PutWithCsrfAsync($"/api/users/{targetId}/role", new { Role = "Administration" }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, roleResponse.StatusCode);

        var auditResponse = await client.GetAsync($"/api/audit?entityType=User&entityId={targetId}&action=User.RoleChanged");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        var entry = Assert.Single(page!.Items);
        Assert.Contains("HR", entry.OldValuesJson);
        Assert.Contains("Administration", entry.NewValuesJson);
    }

    [Fact]
    public async Task AuditPayloads_NeverContainSensitiveIdentityFields()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        await client.PostWithCsrfAsync("/api/users", new
        {
            FirstName = "Seguranca",
            LastName = "Teste",
            Email = $"hr.{Guid.NewGuid():N}@example.invalid",
            Phone = "+351944444441",
            Role = "HR",
            Address = "Rua Segura 1, Lisboa",
        }, csrfToken);

        var auditResponse = await client.GetAsync("/api/audit?pageSize=200");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        foreach (var entry in page!.Items)
        {
            foreach (var forbidden in ForbiddenSubstrings)
            {
                Assert.DoesNotContain(forbidden, entry.OldValuesJson ?? string.Empty);
                Assert.DoesNotContain(forbidden, entry.NewValuesJson ?? string.Empty);
                Assert.DoesNotContain(forbidden, entry.MetadataJson ?? string.Empty);
            }
        }
    }

    [Fact]
    public async Task AuditEndpoint_HasNoWriteAction()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync("/api/audit", new { }, csrfToken);

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Expected no writable audit route, got {response.StatusCode}.");
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
