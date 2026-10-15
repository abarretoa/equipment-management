using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.IntegrationTests.Audit;

/// <summary>
/// fromUtc/toUtc accept only instants with an explicit timezone (the UI sends
/// "…Z"). Anything else — a bare date, a zone-less date-time, garbage — is a
/// 400 validation problem, never a 500 from the database driver.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class AuditDateFilterTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Theory]
    [InlineData("fromUtc", "2026-10-10T23:00:00.000Z")]
    [InlineData("toUtc", "2026-10-11T22:59:59.999Z")]
    [InlineData("fromUtc", "2026-10-11T00:00:00+01:00")]
    [InlineData("fromUtc", "2026-10-10T23:00:00Z")]
    public async Task TimezoneAwareValues_AreAccepted(string field, string value)
    {
        using var client = await LoginAsNewAdministratorAsync();

        var response = await client.GetAsync($"/api/audit?{field}={Uri.EscapeDataString(value)}&pageSize=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("fromUtc", "2026-10-11")]
    [InlineData("toUtc", "2026-10-11")]
    [InlineData("fromUtc", "2026-10-11T00:00:00")]
    [InlineData("fromUtc", "abc")]
    [InlineData("toUtc", "2026-99-99T00:00:00Z")]
    public async Task BareMalformedOrImpossibleValues_AreA400ValidationProblem_NeverA500(string field, string value)
    {
        using var client = await LoginAsNewAdministratorAsync();

        var response = await client.GetAsync($"/api/audit?{field}={Uri.EscapeDataString(value)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(HttpClientTestExtensions.JsonOptions);
        Assert.Contains(problem!.Errors.Keys, key => key.Equals(field, StringComparison.OrdinalIgnoreCase));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Npgsql", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AValidRange_ReturnsTheEventsInside_AndNoFilters_IsUnchanged()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me", HttpClientTestExtensions.JsonOptions);
        var userId = me.GetProperty("id").GetString();
        var now = DateTime.UtcNow;
        var from = Uri.EscapeDataString(now.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
        var to = Uri.EscapeDataString(now.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));

        var inRange = await client.GetFromJsonAsync<PagedResponse<AuditLogResponse>>(
            $"/api/audit?entityId={userId}&fromUtc={from}&toUtc={to}", HttpClientTestExtensions.JsonOptions);
        var outside = await client.GetFromJsonAsync<PagedResponse<AuditLogResponse>>(
            $"/api/audit?entityId={userId}&toUtc={Uri.EscapeDataString(now.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture))}", HttpClientTestExtensions.JsonOptions);
        var unfiltered = await client.GetAsync("/api/audit?pageSize=1");

        Assert.Contains(inRange!.Items, e => e.Action == "Auth.LoginSucceeded");
        Assert.DoesNotContain(outside!.Items, e => e.Action == "Auth.LoginSucceeded");
        Assert.Equal(HttpStatusCode.OK, unfiltered.StatusCode);
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
