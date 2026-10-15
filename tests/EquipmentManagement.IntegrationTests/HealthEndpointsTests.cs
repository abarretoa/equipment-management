using System.Net;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests;

/// <summary>
/// Smoke test: the API host must start and serve traffic. Deliberately checks
/// only /health/live (which never touches PostgreSQL) so this test proves the DI
/// container and middleware pipeline are wired correctly without requiring a
/// database connection.
/// </summary>
public sealed class HealthEndpointsTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task LiveEndpoint_ReturnsHealthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EveryResponse_CarriesBaselineSecurityHeaders()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal("nosniff", GetHeaderValue(response, "X-Content-Type-Options"));
        Assert.Equal("strict-origin-when-cross-origin", GetHeaderValue(response, "Referrer-Policy"));
        Assert.Equal("DENY", GetHeaderValue(response, "X-Frame-Options"));
        Assert.Contains("frame-ancestors 'none'", GetHeaderValue(response, "Content-Security-Policy"));
        Assert.NotNull(GetHeaderValue(response, "Permissions-Policy"));
    }

    private static string? GetHeaderValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
