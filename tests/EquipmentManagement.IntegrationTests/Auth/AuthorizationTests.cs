using System.Net;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Auth;

/// <summary>
/// The Administration/HR access boundary, enforced server-side via permission
/// policies (docs/PROJECT_SPEC.md, section 4; ADR-005). No test here checks a
/// role name directly — each asserts the actual HTTP outcome an HR user gets when
/// calling an Administration-only endpoint.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class AuthorizationTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Administration_CanListUsers()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        using var client = factory.CreateClient();
        await client.LoginAsync(email, password);

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Hr_CannotListUsers()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Teste 4, Faro");
        using var client = factory.CreateClient();
        await client.LoginAsync(email, password);

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Administration_CanReadAudit()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        using var client = factory.CreateClient();
        await client.LoginAsync(email, password);

        var response = await client.GetAsync("/api/audit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Hr_CannotReadAudit()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Teste 5, Coimbra");
        using var client = factory.CreateClient();
        await client.LoginAsync(email, password);

        var response = await client.GetAsync("/api/audit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
