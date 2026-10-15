using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Consultants;

/// <summary>
/// Consultant lifecycle against a real PostgreSQL database
/// (docs/PROJECT_SPEC.md, sections 5, 9-11; Phase 2 scope).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class ConsultantTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Administration_CanCreateConsultant()
    {
        using var client = await LoginAsNewAdministratorAsync();

        var (id, nif) = await Phase2TestData.CreateConsultantAsync(client, fullName: "Ana Teste");

        var response = await client.GetAsync($"/api/consultants/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ConsultantDetailResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Equal("Ana Teste", body!.FullName);
        Assert.Equal(nif, body.Nif);
        Assert.True(body.IsActive);
        Assert.Null(body.CurrentAllocation);
        Assert.Empty(body.AllocationHistory);
    }

    [Fact]
    public async Task Hr_CanCreateConsultant()
    {
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR 2, Porto");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);

        var (id, _) = await Phase2TestData.CreateConsultantAsync(hrClient, fullName: "Bruno Demo");

        var response = await hrClient.GetAsync($"/api/consultants/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateNif_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var nif = Random.Shared.NextInt64(100000000, 999999999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Phase2TestData.CreateConsultantAsync(client, nif: nif);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        // Same NIF with formatting noise — must still collide after normalization.
        var response = await client.PostWithCsrfAsync("/api/consultants", new
        {
            FullName = "Segunda Pessoa",
            Nif = $" {nif[..3]}-{nif[3..6]}-{nif[6..]} ",
            Email = $"dup.{Guid.NewGuid():N}@example.invalid",
            Address = "Rua Duplicada 1, Lisboa",
            Phone = "+351900000001",
        }, csrfToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateNif_ConcurrentCreate_OnlyOneSucceeds()
    {
        // Regression test: ConsultantService's duplicate-NIF catch used to
        // match only DbUpdateException with a directly-inner PostgresException.
        // Two real, simultaneous inserts race the actual unique index rather
        // than the earlier existence pre-check (see PostgresConflictClassifier).
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();
        var nif = Random.Shared.NextInt64(100000000, 999999999).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var csrfA = await clientA.GetAntiforgeryTokenAsync();
        var csrfB = await clientB.GetAntiforgeryTokenAsync();

        object NewConsultantPayload() => new
        {
            FullName = "Racing Consultant",
            Nif = nif,
            Email = $"race.{Guid.NewGuid():N}@example.invalid",
            Address = "Rua da Corrida 1, Lisboa",
            Phone = "+351900000002",
        };

        var results = await Task.WhenAll(
            clientA.PostWithCsrfAsync("/api/consultants", NewConsultantPayload(), csrfA),
            clientB.PostWithCsrfAsync("/api/consultants", NewConsultantPayload(), csrfB));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Hr_CanUpdateConsultant()
    {
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR 3, Braga");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);
        var (id, nif) = await Phase2TestData.CreateConsultantAsync(hrClient);

        var csrfToken = await hrClient.GetAntiforgeryTokenAsync();
        var response = await hrClient.PutWithCsrfAsync($"/api/consultants/{id}", new
        {
            FullName = "Nome Atualizado",
            Nif = nif,
            Email = $"updated.{Guid.NewGuid():N}@example.invalid",
            Address = "Rua Atualizada 1, Lisboa",
            Phone = "+351900000002",
        }, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ConsultantDetailResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Equal("Nome Atualizado", body!.FullName);
    }

    [Fact]
    public async Task Deactivate_PreservesTheRecord()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (id, _) = await Phase2TestData.CreateConsultantAsync(client);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var deactivateResponse = await client.PostWithCsrfAsync<object?>($"/api/consultants/{id}/deactivate", null, csrfToken);
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/consultants/{id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var body = await getResponse.Content.ReadFromJsonAsync<ConsultantDetailResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.False(body!.IsActive);
    }

    [Fact]
    public async Task InactiveConsultant_CannotReceiveNewAllocation()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (id, _) = await Phase2TestData.CreateConsultantAsync(client);

        var deactivateCsrf = await client.GetAntiforgeryTokenAsync();
        await client.PostWithCsrfAsync<object?>($"/api/consultants/{id}/deactivate", null, deactivateCsrf);

        var allocationCsrf = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync($"/api/consultants/{id}/allocations", new
        {
            WorkforceType = "Internal",
            EngagementType = "EmploymentContract",
            RoleOrFunction = "Developer",
            StartDate = "2026-01-01",
        }, allocationCsrf);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ConsultantListSummary_NeverExposesNifAddressPhoneOrEmail()
    {
        using var client = await LoginAsNewAdministratorAsync();
        await Phase2TestData.CreateConsultantAsync(client, fullName: $"Privacidade {Guid.NewGuid():N}");

        var response = await client.GetAsync("/api/consultants?pageSize=200");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("\"nif\"", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"address\"", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"phone\"", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"email\"", raw, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
