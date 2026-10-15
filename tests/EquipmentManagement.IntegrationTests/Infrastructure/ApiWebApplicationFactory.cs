using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API host for integration tests, in the "Testing" environment
/// (so Program.cs's Development-only auto-migrate-on-startup does not run —
/// tests that need the schema apply migrations explicitly, see
/// IdentitySeedingTests). Every factory in the process points at this run's
/// isolated <see cref="TestDatabase"/> — never at the database named by
/// ConnectionStrings__Default, which only supplies the server (ADR-074) — and
/// at this run's temporary <see cref="TestStorage"/> root, never the API's
/// development private-storage/ (ADR-075).
/// </summary>
public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    public static string ConnectionString => TestDatabase.ConnectionString;

    public static string PrivateStorageRootPath => TestStorage.RootPath;

    public ApiWebApplicationFactory()
    {
        // The auth and antiforgery cookies are configured Secure-only
        // (CookieSecurePolicy.Always — correct for every real deployment, which
        // is always HTTPS). TestServer's in-memory requests derive
        // Request.IsHttps from the client's BaseAddress scheme, not real TLS, so
        // without this every antiforgery/login call in tests would fail with
        // "the current request is not an SSL request."
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    static ApiWebApplicationFactory()
    {
        // Program.cs reads ConnectionStrings:Default eagerly from
        // builder.Configuration while wiring up AddInfrastructure, before
        // builder.Build() runs. WebApplicationFactory's ConfigureAppConfiguration
        // override below is only merged in during that later Build() call, so an
        // in-memory override alone arrives too late for this eager read. Setting
        // the process environment variable here lands it in the configuration
        // ASP.NET Core's default host building already loads synchronously.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", ConnectionString);

        // PrivateStorage options bind lazily, so the in-memory override below
        // would suffice; set here too so no read-timing change in Program.cs can
        // ever send test files back to the API's development private-storage/.
        Environment.SetEnvironmentVariable("PrivateStorage__RootPath", PrivateStorageRootPath);

        // Same eager-read timing applies to the "login" rate limiter's
        // PermitLimit (also read from builder.Configuration before Build()).
        // Every Fact in a test class shares this factory's single host, and
        // therefore its single rate limiter's in-memory state, so the
        // production-sensible default (10/min, see Program.cs) would start
        // rejecting logins partway through a normal test run. This does not
        // weaken anything real: it only changes what the *test* host enforces.
        Environment.SetEnvironmentVariable("RateLimiting__Login__PermitLimit", "10000");

        // Same reasoning for the *global* rate limiter (100/min/IP by
        // default) — a single test class can easily exceed that across all
        // its Facts (each doing several HTTP calls), and a dedicated
        // concurrency test deliberately fires many requests at once.
        Environment.SetEnvironmentVariable("RateLimiting__Global__PermitLimit", "100000");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["PrivateStorage:RootPath"] = PrivateStorageRootPath,
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
                // Obviously-fake fixture values — Responsibility Term generation
                // fails safely (an exception, not a fabricated document) when this
                // is unconfigured, so the fixture must configure it for every test
                // that exercises Phase 6 generation (Phase 6 scope).
                ["Documents:Issuer:LegalName"] = "Example Consulting, Lda. (TEST FIXTURE - NOT REAL)",
                ["Documents:Issuer:Address"] = "Rua Exemplo 123, 1000-001 Lisboa (test fixture)",
                ["Documents:Issuer:TaxId"] = "500000000",
            });
        });
    }
}
