using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Companies;
using EquipmentManagement.Api.Contracts.Consultants;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Creates Companies/Consultants through the real HTTP API (using an already
/// logged-in Administration <see cref="HttpClient"/>) for integration test
/// setup — exercises the same path a real caller would, rather than writing
/// rows directly via EF Core.
/// </summary>
public static class Phase2TestData
{
    public static async Task<(Guid Id, string Name)> CreateCompanyAsync(HttpClient adminClient, string? name = null)
    {
        name ??= $"Test Company {Guid.NewGuid():N}";
        var csrfToken = await adminClient.GetAntiforgeryTokenAsync();

        var response = await adminClient.PostWithCsrfAsync("/api/companies", new { Name = name }, csrfToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<CompanyResponse>(HttpClientTestExtensions.JsonOptions);
        return (body!.Id, body.Name);
    }

    public static async Task<(Guid Id, string Nif)> CreateConsultantAsync(
        HttpClient adminClient, string? fullName = null, string? nif = null)
    {
        fullName ??= "Test Consultant";
        nif ??= Random.Shared.NextInt64(100000000, 999999999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var csrfToken = await adminClient.GetAntiforgeryTokenAsync();

        var response = await adminClient.PostWithCsrfAsync("/api/consultants", new
        {
            FullName = fullName,
            Nif = nif,
            Email = $"consultant.{Guid.NewGuid():N}@example.invalid",
            Address = "Rua de Teste 1, Lisboa",
            Phone = "+351900000000",
        }, csrfToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ConsultantDetailResponse>(HttpClientTestExtensions.JsonOptions);
        return (body!.Id, body.Nif);
    }
}
