using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Allocations;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Allocations;

/// <summary>
/// The historical-allocation rules — Internal/External validation, the one-Active
/// invariant, and the never-overwrite-history guarantee — against a real
/// PostgreSQL database (docs/PROJECT_SPEC.md, sections 6, 21; Phase 2 scope).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class AllocationTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task InternalAllocation_WithoutCompany_Succeeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "Internal",
            EngagementType = "EmploymentContract",
            RoleOrFunction = "Backend Developer",
            StartDate = "2026-01-01",
        }, csrfToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Null(body!.CompanyId);
        Assert.Equal("Active", body.Status.ToString());
    }

    [Fact]
    public async Task ExternalAllocation_WithActiveCompany_Succeeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var (companyId, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "External",
            EngagementType = "ServiceProvider",
            RoleOrFunction = "Consultant",
            CompanyId = companyId,
            StartDate = "2026-01-01",
        }, csrfToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Equal(companyId, body!.CompanyId);
        Assert.Equal(companyName, body.CompanyName);
    }

    [Fact]
    public async Task ExternalAllocation_WithoutCompany_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "External",
            EngagementType = "ServiceProvider",
            RoleOrFunction = "Consultant",
            StartDate = "2026-01-01",
        }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InternalAllocation_WithACompany_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var (companyId, _) = await Phase2TestData.CreateCompanyAsync(client);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "Internal",
            EngagementType = "EmploymentContract",
            RoleOrFunction = "Developer",
            CompanyId = companyId,
            StartDate = "2026-01-01",
        }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidDateRange_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "Internal",
            EngagementType = "EmploymentContract",
            RoleOrFunction = "Developer",
            StartDate = "2026-06-01",
            EndDate = "2026-01-01",
        }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SecondActiveAllocation_ForSameConsultant_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);

        var firstCsrf = await client.GetAntiforgeryTokenAsync();
        var first = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "Internal",
            EngagementType = "EmploymentContract",
            RoleOrFunction = "Developer",
            StartDate = "2026-01-01",
        }, firstCsrf);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var secondCsrf = await client.GetAntiforgeryTokenAsync();
        var second = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "Internal",
            EngagementType = "ServiceProvider",
            RoleOrFunction = "Tech Lead",
            StartDate = "2026-03-01",
        }, secondCsrf);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task SecondActiveAllocation_ConcurrentCreate_OnlyOneSucceeds()
    {
        // Regression test: ConsultantAllocationService's Serializable-isolation
        // catch used to match only a raw PostgresException, missing the
        // DbUpdateException-wrapped shape the same conflict can take during
        // SaveChangesAsync (see PostgresConflictClassifier and ADR-036, which this mirrors). Two real, simultaneous requests for
        // the same consultant race the one-Active check itself, not a pre-check.
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(clientA);

        var csrfA = await clientA.GetAntiforgeryTokenAsync();
        var csrfB = await clientB.GetAntiforgeryTokenAsync();

        var results = await Task.WhenAll(
            clientA.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
            {
                WorkforceType = "Internal",
                EngagementType = "EmploymentContract",
                RoleOrFunction = "Developer",
                StartDate = "2026-01-01",
            }, csrfA),
            clientB.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
            {
                WorkforceType = "Internal",
                EngagementType = "ServiceProvider",
                RoleOrFunction = "Tech Lead",
                StartDate = "2026-01-01",
            }, csrfB));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task EndingAnAllocation_PreservesItAndAllowsANewOne()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var (companyId, _) = await Phase2TestData.CreateCompanyAsync(client);

        var createFirstCsrf = await client.GetAntiforgeryTokenAsync();
        var firstResponse = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "External",
            EngagementType = "ServiceProvider",
            RoleOrFunction = "Developer",
            CompanyId = companyId,
            StartDate = "2026-01-01",
        }, createFirstCsrf);
        var first = await firstResponse.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions);

        var endCsrf = await client.GetAntiforgeryTokenAsync();
        var endResponse = await client.PostWithCsrfAsync(
            $"/api/consultants/{consultantId}/allocations/{first!.Id}/end", new { EndDate = "2026-06-30", IsEndOfEngagement = false }, endCsrf);
        Assert.Equal(HttpStatusCode.OK, endResponse.StatusCode);
        var ended = await endResponse.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Equal("Ended", ended!.Status.ToString());
        Assert.Equal(new DateOnly(2026, 6, 30), ended.EndDate);

        // The ended allocation is preserved exactly, never overwritten.
        var getEndedResponse = await client.GetAsync($"/api/consultants/{consultantId}/allocations/{first.Id}");
        var getEnded = await getEndedResponse.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Equal("Developer", getEnded!.RoleOrFunction);
        Assert.Equal(companyId, getEnded.CompanyId);

        // A new, non-overlapping allocation can now be created.
        var secondCsrf = await client.GetAntiforgeryTokenAsync();
        var secondResponse = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "Internal",
            EngagementType = "EmploymentContract",
            RoleOrFunction = "Senior Developer",
            StartDate = "2026-07-01",
        }, secondCsrf);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
    }

    [Fact]
    public async Task EndingAnAlreadyEndedAllocation_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);

        var createCsrf = await client.GetAntiforgeryTokenAsync();
        var createResponse = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "Internal",
            EngagementType = "EmploymentContract",
            RoleOrFunction = "Developer",
            StartDate = "2026-01-01",
        }, createCsrf);
        var allocation = await createResponse.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions);

        var endCsrf = await client.GetAntiforgeryTokenAsync();
        await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations/{allocation!.Id}/end", new { EndDate = "2026-06-30", IsEndOfEngagement = false }, endCsrf);

        var secondEndCsrf = await client.GetAntiforgeryTokenAsync();
        var secondEndResponse = await client.PostWithCsrfAsync(
            $"/api/consultants/{consultantId}/allocations/{allocation.Id}/end", new { EndDate = "2026-07-01", IsEndOfEngagement = false }, secondEndCsrf);

        Assert.Equal(HttpStatusCode.Conflict, secondEndResponse.StatusCode);
    }

    [Fact]
    public async Task UpdatingAnEndedAllocation_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);

        var createCsrf = await client.GetAntiforgeryTokenAsync();
        var createResponse = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "Internal",
            EngagementType = "EmploymentContract",
            RoleOrFunction = "Developer",
            StartDate = "2026-01-01",
        }, createCsrf);
        var allocation = await createResponse.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions);

        var endCsrf = await client.GetAntiforgeryTokenAsync();
        await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations/{allocation!.Id}/end", new { EndDate = "2026-06-30", IsEndOfEngagement = false }, endCsrf);

        var updateCsrf = await client.GetAntiforgeryTokenAsync();
        var updateResponse = await client.PutWithCsrfAsync(
            $"/api/consultants/{consultantId}/allocations/{allocation.Id}", new { RoleOrFunction = "Rewritten Role" }, updateCsrf);

        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);
    }

    [Fact]
    public async Task EndingAnAllocation_RecordsAnAuditEvent()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);

        var createCsrf = await client.GetAntiforgeryTokenAsync();
        var createResponse = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "Internal",
            EngagementType = "EmploymentContract",
            RoleOrFunction = "Developer",
            StartDate = "2026-01-01",
        }, createCsrf);
        var allocation = await createResponse.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions);

        var endCsrf = await client.GetAntiforgeryTokenAsync();
        await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations/{allocation!.Id}/end", new { EndDate = "2026-06-30", IsEndOfEngagement = false }, endCsrf);

        var auditResponse = await client.GetAsync($"/api/audit?entityType=ConsultantAllocation&entityId={allocation.Id}&action=ConsultantAllocation.Ended");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        Assert.Contains(page!.Items, e => e.Action == "ConsultantAllocation.Ended");
    }

    [Fact]
    public async Task FirstActiveAllocation_Succeeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);

        var response = await PostInternalAllocationAsync(client, consultantId, "2026-01-01", endDate: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var allocation = (await response.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(AllocationStatus.Active, allocation.Status);
    }

    /// <summary>
    /// The confirmed client rule: dates never permit a second Active allocation.
    /// The old overlap rule accepted this exact case (current allocation with
    /// a known end, next one starting the day after); it is now rejected.
    /// </summary>
    [Fact]
    public async Task SecondActiveAllocation_WithNonOverlappingLaterDates_IsStillRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        (await PostInternalAllocationAsync(client, consultantId, "2026-01-01", endDate: "2026-09-30")).EnsureSuccessStatusCode();

        var second = await PostInternalAllocationAsync(client, consultantId, "2026-10-01", endDate: null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("Este consultor já tem uma alocação ativa", await ReadTitleAsync(second), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FutureStartActiveAllocation_CountsAsTheConsultantsOneActiveAllocation()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var futureStart = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        (await PostInternalAllocationAsync(client, consultantId, futureStart, endDate: null)).EnsureSuccessStatusCode();

        var startingNow = await PostInternalAllocationAsync(client, consultantId, "2026-01-01", endDate: "2026-02-28");

        Assert.Equal(HttpStatusCode.Conflict, startingNow.StatusCode);
    }

    [Fact]
    public async Task AfterEndingTheActiveAllocation_ANewActiveAllocationCanBeCreated()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var first = await Phase4TestData.CreateAllocationAsync(client, consultantId, new DateOnly(2026, 1, 1));
        (await Phase4TestData.EndAllocationAsync(client, consultantId, first.Id, new DateOnly(2026, 6, 30))).EnsureSuccessStatusCode();

        var second = await PostInternalAllocationAsync(client, consultantId, "2026-07-01", endDate: null);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    /// <summary>The partial unique index is the backstop even for writes that bypass the service (e.g. a future import or a manual fix).</summary>
    [Fact]
    public async Task Database_RejectsASecondActiveAllocationWrittenDirectly()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        await Phase4TestData.CreateAllocationAsync(client, consultantId, new DateOnly(2026, 1, 1));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ConsultantAllocations.Add(new ConsultantAllocation
        {
            Id = Guid.NewGuid(),
            ConsultantId = consultantId,
            WorkforceType = WorkforceType.Internal,
            EngagementType = EngagementType.EmploymentContract,
            RoleOrFunction = "Bypass",
            StartDate = new DateOnly(2027, 1, 1),
            Status = AllocationStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        Assert.True(PostgresConflictClassifier.IsUniqueViolationOn(exception, "IX_ConsultantAllocations_ConsultantId_Active"));
    }

    private static async Task<HttpResponseMessage> PostInternalAllocationAsync(HttpClient client, Guid consultantId, string startDate, string? endDate)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "Internal",
            EngagementType = "EmploymentContract",
            RoleOrFunction = "Developer",
            StartDate = startDate,
            EndDate = endDate,
        }, csrfToken);
    }

    private static async Task<string> ReadTitleAsync(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("title").GetString()!;
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
