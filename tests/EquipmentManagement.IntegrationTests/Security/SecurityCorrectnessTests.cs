using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Observability;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Security;

/// <summary>
/// Security-correctness regressions (ADR-076): an untrusted X-Correlation-ID can
/// never reach the audit trail or break a committed operation; a security
/// mutation and its audit row commit together or not at all; and an
/// already-issued auth cookie never keeps permissions its user has lost.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class SecurityCorrectnessTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string CorrelationHeader = "X-Correlation-ID";
    private const string HrAddress = "Rua da Sessão 7, Lisboa";

    // --- A: X-Correlation-ID ----------------------------------------------

    [Fact]
    public async Task NoCorrelationHeader_GeneratesASafeServerId()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/antiforgery/token");

        AssertSafeCorrelationId(SingleCorrelationHeader(response));
    }

    [Fact]
    public async Task ValidClientCorrelationId_IsPreserved_OnResponseAndAudit()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var supplied = $"gw-{Guid.NewGuid():N}:req_1.2";

        var response = await PostCompanyAsync(client, csrfToken, supplied);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(supplied, SingleCorrelationHeader(response));
        var companyId = await ReadIdAsync(response);
        Assert.Equal(supplied, await SingleAuditCorrelationIdAsync(AuditActions.Company.Created, companyId));
    }

    public static TheoryData<string> UnsafeCorrelationIds => new()
    {
        "",
        "   ",
        new string('a', 65),
        new string('a', 101),
        new string('x', 4000),
        "abc def",
        "abc\tdef",
        "id;drop",
        "<script>",
        "id/with/slashes",
        "%0d%0aInjected: 1",
    };

    [Theory]
    [MemberData(nameof(UnsafeCorrelationIds))]
    public async Task UnsafeClientCorrelationId_IsReplaced_AndTheOperationAndAuditBothCommit(string supplied)
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await PostCompanyAsync(client, csrfToken, supplied);

        // The historical failure: the company committed, then the audit insert
        // failed on the 100-character column and the request became a 500.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var echoed = SingleCorrelationHeader(response);
        AssertSafeCorrelationId(echoed);
        Assert.NotEqual(supplied, echoed);

        var companyId = await ReadIdAsync(response);
        var audited = await SingleAuditCorrelationIdAsync(AuditActions.Company.Created, companyId);
        Assert.Equal(echoed, audited);
    }

    [Fact]
    public async Task OversizedCorrelationId_NeverLeavesACommittedOperationWithoutItsAuditRow()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var name = $"Oversized Co {Guid.NewGuid():N}";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/companies") { Content = JsonContent.Create(new { Name = name }) };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        request.Headers.TryAddWithoutValidation(CorrelationHeader, new string('z', 5000));

        var response = await client.SendAsync(request);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Name == name);
        var auditRows = company is null
            ? 0
            : await db.AuditLogs.AsNoTracking().CountAsync(a => a.Action == AuditActions.Company.Created && a.EntityId == company.Id.ToString());
        Assert.True(company is null || auditRows == 1, $"Committed company with {auditRows} audit rows (HTTP {(int)response.StatusCode}).");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public void CorrelationIdPolicy_FitsTheAuditLogColumn()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var column = db.Model.FindEntityType(typeof(AuditLog))!.FindProperty(nameof(AuditLog.CorrelationId))!.GetMaxLength();

        Assert.NotNull(column);
        Assert.True(CorrelationId.MaxLength <= column, $"Policy max {CorrelationId.MaxLength} exceeds column max {column}.");
        Assert.True(CorrelationId.Generate().Length <= CorrelationId.MaxLength);
    }

    [Fact]
    public async Task NonAsciiCorrelationId_IsReplaced()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/antiforgery/token");
        // Latin-1 bytes that are not in the permitted ASCII identifier set.
        request.Headers.TryAddWithoutValidation(CorrelationHeader, "café-é");

        var response = await client.SendAsync(request);

        var echoed = SingleCorrelationHeader(response);
        AssertSafeCorrelationId(echoed);
        Assert.NotEqual("café-é", echoed);
    }

    // --- B: audit atomicity ----------------------------------------------

    [Fact]
    public async Task AdministrationUserUpdate_WhenTheAuditWriteFails_LeavesTheUserUnchanged()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (targetEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: HrAddress);
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        HttpResponseMessage response;
        await using (await FailAuditInsertsAsync(AuditActions.User.Updated, targetId))
        {
            response = await client.PutWithCsrfAsync($"/api/users/{targetId}", new
            {
                FirstName = "Alterado",
                LastName = "Nunca",
                Phone = "+351900000001",
                Address = HrAddress,
            }, csrfToken);
        }

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var user = await FindUserAsync(targetEmail);
        Assert.Equal("Test", user.FirstName);
        Assert.Equal("HR", user.LastName);
        Assert.Equal(0, await CountAuditAsync(AuditActions.User.Updated, targetId));
    }

    [Fact]
    public async Task AdministrationUserUpdate_WhenSuccessful_WritesExactlyOneAuditRow()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (targetEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: HrAddress);
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync($"/api/users/{targetId}", new
        {
            FirstName = "Alterado",
            LastName = "Sempre",
            Phone = "+351900000001",
            Address = HrAddress,
        }, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Alterado", (await FindUserAsync(targetEmail)).FirstName);
        Assert.Equal(1, await CountAuditAsync(AuditActions.User.Updated, targetId));
    }

    [Fact]
    public async Task RoleChange_WhenTheAuditWriteFails_KeepsTheOldRoleAndTheSession()
    {
        using var adminClient = await LoginAsNewAdministratorAsync();
        var (targetEmail, targetPassword) = await ApiTestUsers.CreateAsync(factory, "Administration", address: HrAddress);
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);
        using var targetClient = factory.CreateClient();
        await targetClient.LoginAsync(targetEmail, targetPassword);
        var stampBefore = (await FindUserAsync(targetEmail)).SecurityStamp;
        var csrfToken = await adminClient.GetAntiforgeryTokenAsync();

        HttpResponseMessage response;
        await using (await FailAuditInsertsAsync(AuditActions.User.RoleChanged, targetId))
        {
            response = await adminClient.PutWithCsrfAsync($"/api/users/{targetId}/role", new { Role = "HR" }, csrfToken);
        }

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Administration", await RoleOfAsync(targetEmail));
        Assert.Equal(stampBefore, (await FindUserAsync(targetEmail)).SecurityStamp);
        Assert.Equal(HttpStatusCode.OK, (await targetClient.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Deactivation_WhenTheAuditWriteFails_LeavesTheUserActive()
    {
        using var adminClient = await LoginAsNewAdministratorAsync();
        var (targetEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: HrAddress);
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);
        var csrfToken = await adminClient.GetAntiforgeryTokenAsync();

        HttpResponseMessage response;
        await using (await FailAuditInsertsAsync(AuditActions.User.Deactivated, targetId))
        {
            response = await adminClient.PostWithCsrfAsync($"/api/users/{targetId}/deactivate", new { }, csrfToken);
        }

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True((await FindUserAsync(targetEmail)).IsActive);
    }

    [Fact]
    public async Task UserCreation_WhenTheAuditWriteFails_LeavesNoUserBehind()
    {
        using var adminClient = await LoginAsNewAdministratorAsync();
        var csrfToken = await adminClient.GetAntiforgeryTokenAsync();
        var email = $"atomic.{Guid.NewGuid():N}@example.invalid";

        HttpResponseMessage response;
        await using (await FailAuditInsertsAsync(AuditActions.User.Created, entityId: null))
        {
            response = await adminClient.PostWithCsrfAsync("/api/users", new
            {
                FirstName = "Nunca",
                LastName = "Criado",
                Email = email,
                Role = "Administration",
            }, csrfToken);
        }

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Null(await TryFindUserAsync(email));
    }

    [Fact]
    public async Task CompanyDeactivation_WhenTheAuditWriteFails_LeavesTheCompanyActive()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var companyId = await ReadIdAsync(await PostCompanyAsync(client, csrfToken, correlationId: null));

        HttpResponseMessage response;
        await using (await FailAuditInsertsAsync(AuditActions.Company.Deactivated, companyId))
        {
            response = await client.PostWithCsrfAsync($"/api/companies/{companyId}/deactivate", new { }, csrfToken);
        }

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True((await db.Companies.AsNoTracking().SingleAsync(c => c.Id == companyId)).IsActive);
    }

    [Fact]
    public async Task EquipmentRetirement_WhenTheAuditWriteFails_LeavesTheEquipmentAvailable()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        HttpResponseMessage response;
        await using (await FailAuditInsertsAsync(AuditActions.Equipment.Retired, equipment.Id))
        {
            response = await client.PostWithCsrfAsync<object?>($"/api/equipment/{equipment.Id}/retire", null, csrfToken);
        }

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(EquipmentStatus.Available, (await db.Equipment.AsNoTracking().SingleAsync(e => e.Id == equipment.Id)).Status);
    }

    [Fact]
    public async Task HandoverPhotoUpload_WhenTheAuditWriteFails_LeavesNeitherARowNorAStoredFile()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        var handover = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId);
        var added = await Phase4TestData.AddItemAsync(client, handover.Id, equipment.Id);
        added.EnsureSuccessStatusCode();
        var itemId = (await added.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!.Items.Single().Id;
        (await Phase4TestData.RecordInspectionAsync(client, handover.Id, itemId)).EnsureSuccessStatusCode();

        // Unique bytes, so this exact file can be looked for in storage without
        // depending on what any concurrently running test writes.
        byte[] bytes = [.. Phase4TestData.MinimalJpegBytes, .. Guid.NewGuid().ToByteArray()];
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));

        HttpResponseMessage response;
        await using (await FailAuditInsertsAsync(AuditActions.EquipmentHandover.PhotoUploaded, handover.Id))
        {
            response = await Phase4TestData.UploadPhotoAsync(client, handover.Id, itemId, bytes);
        }

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.DeliveryInspectionPhotos.AnyAsync(p => p.Sha256Hash == sha256));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(ApiWebApplicationFactory.PrivateStorageRootPath, "*", SearchOption.AllDirectories),
            path => new FileInfo(path).Length == bytes.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes));

        // The same upload succeeds once the audit can be written: exactly one row and one audit entry.
        var retry = await Phase4TestData.UploadPhotoAsync(client, handover.Id, itemId, bytes);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(1, await CountAuditAsync(AuditActions.EquipmentHandover.PhotoUploaded, handover.Id));
    }

    // --- C: stale role/permission claims in an issued cookie --------------

    [Fact]
    public async Task RoleDowngrade_RejectsTheOriginalCookie_OnItsVeryNextRequest()
    {
        using var adminClient = await LoginAsNewAdministratorAsync();
        var (targetEmail, targetPassword) = await ApiTestUsers.CreateAsync(factory, "Administration", address: HrAddress);
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);
        using var targetClient = factory.CreateClient();
        await targetClient.LoginAsync(targetEmail, targetPassword);
        Assert.Equal(HttpStatusCode.OK, (await targetClient.GetAsync("/api/users")).StatusCode);

        var csrfToken = await adminClient.GetAntiforgeryTokenAsync();
        var change = await adminClient.PutWithCsrfAsync($"/api/users/{targetId}/role", new { Role = "HR" }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        // Same client, same cookie, no re-login: Users.Read must be gone.
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/auth/me")).StatusCode);

        // A fresh login gets exactly the new role's permissions.
        using var relogged = factory.CreateClient();
        await relogged.LoginAsync(targetEmail, targetPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await relogged.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await relogged.GetAsync("/api/audit")).StatusCode);

        // The acting Administrator's own session is untouched.
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task RoleUpgrade_DoesNotSilentlyWidenTheOriginalCookie_ANewLoginIsRequired()
    {
        using var adminClient = await LoginAsNewAdministratorAsync();
        var (targetEmail, targetPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: HrAddress);
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);
        using var targetClient = factory.CreateClient();
        await targetClient.LoginAsync(targetEmail, targetPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await targetClient.GetAsync("/api/users")).StatusCode);

        var csrfToken = await adminClient.GetAntiforgeryTokenAsync();
        var change = await adminClient.PutWithCsrfAsync($"/api/users/{targetId}/role", new { Role = "Administration" }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/users")).StatusCode);

        using var relogged = factory.CreateClient();
        await relogged.LoginAsync(targetEmail, targetPassword);
        Assert.Equal(HttpStatusCode.OK, (await relogged.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Deactivation_RejectsTheOriginalCookie_OnItsVeryNextRequest()
    {
        using var adminClient = await LoginAsNewAdministratorAsync();
        var (targetEmail, targetPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: HrAddress);
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);
        using var targetClient = factory.CreateClient();
        await targetClient.LoginAsync(targetEmail, targetPassword);
        Assert.Equal(HttpStatusCode.OK, (await targetClient.GetAsync("/api/auth/me")).StatusCode);

        var csrfToken = await adminClient.GetAntiforgeryTokenAsync();
        var deactivate = await adminClient.PostWithCsrfAsync($"/api/users/{targetId}/deactivate", new { }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task NonAuthorizationEdit_KeepsTheTargetsSessionValid()
    {
        using var adminClient = await LoginAsNewAdministratorAsync();
        var (targetEmail, targetPassword) = await ApiTestUsers.CreateAsync(factory, "Administration", address: HrAddress);
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);
        using var targetClient = factory.CreateClient();
        await targetClient.LoginAsync(targetEmail, targetPassword);

        var csrfToken = await adminClient.GetAntiforgeryTokenAsync();
        var update = await adminClient.PutWithCsrfAsync($"/api/users/{targetId}", new
        {
            FirstName = "Só",
            LastName = "Nome",
            Phone = "+351900000002",
            Address = HrAddress,
        }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await targetClient.GetAsync("/api/users")).StatusCode);
    }

    // --- helpers -----------------------------------------------------------

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }

    private static Task<HttpResponseMessage> PostCompanyAsync(HttpClient client, string csrfToken, string? correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/companies")
        {
            Content = JsonContent.Create(new { Name = $"Correlation Co {Guid.NewGuid():N}" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation(CorrelationHeader, correlationId);
        }

        return client.SendAsync(request);
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static string SingleCorrelationHeader(HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues(CorrelationHeader));

    private static void AssertSafeCorrelationId(string? value)
    {
        Assert.False(string.IsNullOrEmpty(value));
        Assert.InRange(value!.Length, 1, 64);
        Assert.All(value, c => Assert.True(
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':',
            $"Unexpected character U+{(int)c:X4} in correlation id."));
    }

    private async Task<string?> SingleAuditCorrelationIdAsync(string action, Guid entityId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = entityId.ToString();
        var row = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == action && a.EntityId == id);
        return row.CorrelationId;
    }

    private async Task<int> CountAuditAsync(string action, Guid entityId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = entityId.ToString();
        return await db.AuditLogs.AsNoTracking().CountAsync(a => a.Action == action && a.EntityId == id);
    }

    private async Task<ApplicationUser> FindUserAsync(string email) =>
        await TryFindUserAsync(email) ?? throw new InvalidOperationException("Test user not found.");

    private async Task<ApplicationUser?> TryFindUserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.FindByEmailAsync(email);
    }

    private async Task<string?> RoleOfAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        return (await userManager.GetRolesAsync(user!)).SingleOrDefault();
    }

    /// <summary>
    /// Makes every AuditLogs insert for <paramref name="action"/> (and, when
    /// given, <paramref name="entityId"/>) fail inside PostgreSQL — the same
    /// trigger technique as CurrentInventoryImportTests, scoped narrowly enough
    /// that no other test's audit rows are affected. Disposing drops it.
    /// </summary>
    private async Task<IAsyncDisposable> FailAuditInsertsAsync(string action, Guid? entityId)
    {
        var name = $"audit_fail_{Guid.NewGuid():N}"[..24];
        var entityCondition = entityId is { } id ? $"""AND NEW."EntityId" = '{id}'""" : string.Empty;
        await ExecuteSqlAsync($"""
            CREATE FUNCTION {name}() RETURNS trigger AS $$
            BEGIN
                IF NEW."Action" = '{action}' {entityCondition} THEN
                    RAISE EXCEPTION 'injected audit failure';
                END IF;
                RETURN NEW;
            END $$ LANGUAGE plpgsql;
            CREATE TRIGGER {name} BEFORE INSERT ON "AuditLogs" FOR EACH ROW EXECUTE FUNCTION {name}();
            """);
        return new SqlCleanup(this, $"""DROP TRIGGER {name} ON "AuditLogs"; DROP FUNCTION {name}();""");
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        TestDatabase.EnsureIsThisRunsDatabase(db.Database.GetConnectionString()!);
#pragma warning disable EF1002 // Test-only DDL built from constants and generated identifiers.
        await db.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
    }

    private sealed class SqlCleanup(SecurityCorrectnessTests owner, string sql) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await owner.ExecuteSqlAsync(sql);
    }
}
