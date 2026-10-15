using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Companies;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Companies;

/// <summary>
/// Company CRUD-with-historical-safe-semantics against a real PostgreSQL
/// database (Phase 2 scope).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class CompanyTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Administration_CanCreateCompany()
    {
        using var client = await LoginAsNewAdministratorAsync();

        var (id, name) = await Phase2TestData.CreateCompanyAsync(client);

        var getResponse = await client.GetAsync($"/api/companies/{id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var body = await getResponse.Content.ReadFromJsonAsync<CompanyResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Equal(name, body!.Name);
        Assert.True(body.IsActive);
    }

    [Fact]
    public async Task DuplicateNormalizedCompanyName_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var name = $"Duplicate Co {Guid.NewGuid():N}";
        await Phase2TestData.CreateCompanyAsync(client, name);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        // Same name, different case and spacing — must still collide.
        var response = await client.PostWithCsrfAsync("/api/companies", new { Name = $"  {name.ToUpperInvariant()}  " }, csrfToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateNormalizedCompanyName_ConcurrentCreate_OnlyOneSucceeds()
    {
        // Regression test: CompanyService's duplicate-name catch used to match
        // only DbUpdateException with a directly-inner PostgresException. Two
        // real, simultaneous inserts race the actual unique index rather than
        // the earlier existence pre-check, which is the scenario that can
        // surface a different wrapper shape (see PostgresConflictClassifier).
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();
        var name = $"Race Co {Guid.NewGuid():N}";

        var csrfA = await clientA.GetAntiforgeryTokenAsync();
        var csrfB = await clientB.GetAntiforgeryTokenAsync();

        var results = await Task.WhenAll(
            clientA.PostWithCsrfAsync("/api/companies", new { Name = name }, csrfA),
            clientB.PostWithCsrfAsync("/api/companies", new { Name = name }, csrfB));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Hr_CanReadCreateAndUpdateCompaniesButNotArchive()
    {
        // Approved Phase 2 permission adjustment (superseding the earlier
        // read-only-for-HR default): HR can create/update Companies as
        // day-to-day reference-data entry, but cannot archive one.
        using var adminClient = await LoginAsNewAdministratorAsync();
        var (id, _) = await Phase2TestData.CreateCompanyAsync(adminClient);

        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR 1, Lisboa");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);

        var readResponse = await hrClient.GetAsync($"/api/companies/{id}");
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);

        var (createdId, createdName) = await Phase2TestData.CreateCompanyAsync(hrClient, $"HR Created {Guid.NewGuid():N}");

        var updateCsrf = await hrClient.GetAntiforgeryTokenAsync();
        var updateResponse = await hrClient.PutWithCsrfAsync($"/api/companies/{createdId}", new { Name = $"{createdName} Updated" }, updateCsrf);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var archiveCsrf = await hrClient.GetAntiforgeryTokenAsync();
        var archiveResponse = await hrClient.PostWithCsrfAsync<object?>($"/api/companies/{createdId}/deactivate", null, archiveCsrf);
        Assert.Equal(HttpStatusCode.Forbidden, archiveResponse.StatusCode);
    }

    [Fact]
    public async Task Hr_StillCannotAccessUserManagementOrAudit()
    {
        // The Phase 2 Company adjustment is scoped to Company only — it must
        // not have broadened anything else.
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR 4, Lisboa");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await hrClient.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hrClient.GetAsync("/api/audit")).StatusCode);
    }

    [Fact]
    public async Task InactiveCompany_CannotBeUsedForNewExternalAllocation()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (companyId, _) = await Phase2TestData.CreateCompanyAsync(client);
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);

        var deactivateCsrf = await client.GetAntiforgeryTokenAsync();
        await client.PostWithCsrfAsync<object?>($"/api/companies/{companyId}/deactivate", null, deactivateCsrf);

        var allocationCsrf = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "External",
            EngagementType = "ServiceProvider",
            RoleOrFunction = "Developer",
            CompanyId = companyId,
            StartDate = "2026-01-01",
        }, allocationCsrf);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CompanyEndpoints_HaveNoHardDeleteAction()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (id, _) = await Phase2TestData.CreateCompanyAsync(client);

        var response = await client.DeleteAsync($"/api/companies/{id}");

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Expected no hard-delete route, got {response.StatusCode}.");

        // Still readable and unaffected.
        var getResponse = await client.GetAsync($"/api/companies/{id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
