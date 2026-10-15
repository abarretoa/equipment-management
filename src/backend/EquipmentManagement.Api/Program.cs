using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using EquipmentManagement.Api.ErrorHandling;
using EquipmentManagement.Api.Middleware;
using EquipmentManagement.Application;
using EquipmentManagement.Infrastructure;
using EquipmentManagement.Infrastructure.ClientProfiles;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Observability;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// --- Logging -----------------------------------------------------------
// Built-in structured logging (no third-party logging framework: not justified
// for Phase 0). JSON console output in non-Development so logs are machine
// parseable by whatever collects them in the deployment environment; human
// readable console in Development.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    });
}

// --- Services ------------------------------------------------------------
builder.Services.AddControllers().AddJsonOptions(options =>
{
    // WorkforceType/EngagementType/AllocationStatus (Phase 2) are meant to be
    // read as the stable names declared in Domain, not opaque numbers a client
    // has to hardcode a mapping for (Phase 2 scope). Query-string enum binding already accepts names by
    // default; this makes JSON bodies/responses consistent with that.
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddOpenApi();


builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// ProblemDetails for every error response (validation, auth failures, unhandled
// exceptions). No stack traces ever leave the process — see GlobalExceptionHandler.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var correlationId = context.HttpContext.Items.TryGetValue(CorrelationId.HttpContextItemKey, out var id)
            ? id?.ToString()
            : context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions["correlationId"] = correlationId;
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// CSRF double-submit token: readable by JS (not HttpOnly) so the SPA can echo it
// back as a header; still Secure + SameSite=Strict so only this site's pages can
// read or send it. The session cookie itself stays HttpOnly (see Infrastructure).
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "EquipmentManagement.Csrf";
    options.Cookie.HttpOnly = false;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.HeaderName = "X-CSRF-TOKEN";
});

// CORS: no wildcard origins, ever — this API is cookie-authenticated, and a
// wildcard origin combined with credentials is explicitly disallowed by browsers
// and by docs/PROJECT_SPEC.md section 22. Origins must be configured explicitly;
// an empty list (the Production default) allows no cross-origin browser access.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

// Global rate limiting: a conservative default for every endpoint. Sensitive
// endpoints layer a stricter named policy on top of this via
// [EnableRateLimiting("...")] — see the "login" policy below. Configurable for
// the same reason as "login" below: the integration test suite legitimately
// makes far more than 100 requests/min against one shared test host across a
// whole test class (e.g. concurrent-creation tests), and raising this only for
// the test host doesn't weaken any real deployment's default.
var globalPermitLimit = builder.Configuration.GetValue("RateLimiting:Global:PermitLimit", 100);
var globalWindowSeconds = builder.Configuration.GetValue("RateLimiting:Global:WindowSeconds", 60);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = globalPermitLimit,
                Window = TimeSpan.FromSeconds(globalWindowSeconds),
                QueueLimit = 0,
            }));

    // Brute-force protection for login, on top of Identity's own per-account
    // lockout (5 failed attempts / 15 min, see Infrastructure/DependencyInjection.cs):
    // this policy caps attempts per source IP across ALL accounts, so lockout
    // can't be trivially sidestepped by spraying many different email addresses.
    // 10/min (the production-sensible default below) is generous for a genuine
    // user who mistypes a password a few times, while making a scripted
    // credential-stuffing run materially slower (Phase 1 scope). Configurable (not hardcoded) so the
    // integration test suite — which shares one WebApplicationFactory's, and
    // therefore one rate limiter's, state across every login in a test class —
    // can raise it via RateLimiting__Login__PermitLimit without weakening the
    // real default; see ApiWebApplicationFactory.
    var loginPermitLimit = builder.Configuration.GetValue("RateLimiting:Login:PermitLimit", 10);
    var loginWindowSeconds = builder.Configuration.GetValue("RateLimiting:Login:WindowSeconds", 60);
    options.AddFixedWindowLimiter("login", limiterOptions =>
    {
        limiterOptions.PermitLimit = loginPermitLimit;
        limiterOptions.Window = TimeSpan.FromSeconds(loginWindowSeconds);
        limiterOptions.QueueLimit = 0;
    });
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

var app = builder.Build();

// The client profile compiled into this build (docs/WHITE_LABEL.md). A
// demonstration profile is allowed everywhere, but a Production start says so.
var clientProfile = app.Services.GetRequiredService<ClientProfile>();
Program.LogClientProfile(app.Logger, clientProfile.Identity.Id, clientProfile.Identity.ProductName);
if (app.Environment.IsProduction() && clientProfile.Identity.IsDemonstration)
{
    Program.LogDemonstrationProfile(app.Logger, clientProfile.Identity.Id);
}

// Accepted (an internal relay on a private network may legitimately speak
// plain SMTP) but never silently: every start of such an instance says so.
if (app.Environment.IsProduction()
    && app.Services.GetRequiredService<IOptions<EmailOptions>>().Value.TlsMode == EmailTlsMode.None)
{
    Program.LogSmtpWithoutTls(app.Logger);
}

// --- Startup: migrations, role/permission seeding, admin bootstrap ---------
// Migrations auto-apply in Development only (ADR-008): production databases are
// migrated explicitly and deliberately (see README), never as a side effect of
// the process starting — an accidental production start against an unmigrated
// database should fail loudly, not silently change schema.
if (app.Environment.IsDevelopment())
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var dbContext = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();
}

// Role/permission seeding and admin bootstrap are data, not schema — safe and
// necessary in every REAL environment (a fresh production database needs the
// Administration/HR roles and their permission claims to exist before anyone can
// sign in, same as Development). Both are idempotent; see IdentitySeeder and
// AdminBootstrapper for why repeated runs are safe.
//
// Skipped in the "Testing" environment (WebApplicationFactory-hosted integration
// tests): HealthEndpointsTests deliberately boots the host without a database
// connection at all (see its doc comment), and DB-dependent tests apply
// migrations and seed explicitly and directly (see IdentitySeedingTests /
// ApiTestUsers) rather than relying on this side effect of process startup.
if (!app.Environment.IsEnvironment("Testing"))
{
    try
    {
        await using var seedScope = app.Services.CreateAsyncScope();
        var roleSeeder = seedScope.ServiceProvider.GetRequiredService<IdentitySeeder>();
        await roleSeeder.SeedAsync();

        var adminBootstrapper = seedScope.ServiceProvider.GetRequiredService<AdminBootstrapper>();
        await adminBootstrapper.RunAsync();
    }
    catch (Exception ex)
    {
        // A raw provider exception here (most commonly: the database has not been
        // migrated yet — "relation ... does not exist") is otherwise indistinguishable
        // from any other startup crash in a log stream. One clear FATAL line first
        // — before the exception propagates and the process exits (Phase 10
        // hardening: fail fast, but *diagnosably* fast) — is a deliberate
        // "run migrations before starting the app" nudge, not a new failure mode:
        // the app still refuses to start, exactly as before.
        Program.LogStartupSeedFailure(app.Logger, ex);
        throw;
    }
}

// --- Middleware pipeline ---------------------------------------------------
// Forwarded-header handling for a TLS-terminating reverse proxy (Phase 10).
// Disabled by default: an app with this on but no trusted proxy configured would
// let ANY caller spoof its own client IP (defeating rate limiting) and scheme
// (defeating Secure-cookie/HTTPS-redirect guarantees) — so this fails fast rather
// than silently trusting the whole internet. Must run before anything that reads
// Request.Scheme or Connection.RemoteIpAddress (rate limiter, HTTPS redirection).
if (builder.Configuration.GetValue("ForwardedHeaders:Enabled", false))
{
    var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
    var knownNetworks = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
    if (knownProxies.Length == 0 && knownNetworks.Length == 0)
    {
        throw new InvalidOperationException(
            "ForwardedHeaders:Enabled is true but no ForwardedHeaders:KnownProxies or " +
            "ForwardedHeaders:KnownNetworks are configured — refusing to trust X-Forwarded-* " +
            "headers from an unspecified source. Set at least one trusted reverse-proxy IP " +
            "(ForwardedHeaders__KnownProxies__0) or network in CIDR form " +
            "(ForwardedHeaders__KnownNetworks__0, e.g. \"10.0.0.0/8\"). See README.");
    }

    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
    foreach (var proxy in knownProxies)
    {
        forwardedHeadersOptions.KnownProxies.Add(IPAddress.Parse(proxy));
    }
    foreach (var network in knownNetworks)
    {
        var parts = network.Split('/', 2);
        forwardedHeadersOptions.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse(parts[0]), int.Parse(parts[1], CultureInfo.InvariantCulture)));
    }

    app.UseForwardedHeaders(forwardedHeadersOptions);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    // AllowAnonymous: the FallbackPolicy below would otherwise require the auth
    // cookie even for this Development-only schema document (matches the
    // README's documented anonymous `curl .../openapi/v1.json` smoke check).
    app.MapOpenApi().AllowAnonymous();
}

// Baseline response headers (Phase 10) for every response, API included:
// this is a cookie-authenticated JSON API, so none of these should ever be
// controversial or break a legitimate client. `default-src 'none'` is safe for
// an API that never itself serves renderable HTML/script; the frontend (a
// separately-hosted SPA — see README's deployment section) carries its own,
// richer CSP appropriate to its bundle.
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "geolocation=(), camera=(), microphone=()");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseCors("Frontend");

app.UseAuthentication();
app.UseMiddleware<RequirePasswordChangeMiddleware>();
app.UseAuthorization();

app.MapControllers();

// AllowAnonymous is required here now that AddAuthorization's FallbackPolicy
// (Phase 10 hardening) requires authentication by default on anything without
// its own authorization metadata — a load balancer/orchestrator health probe
// never carries the auth cookie.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).AllowAnonymous();

app.Run();

/// <summary>Entry point class, exposed so WebApplicationFactory&lt;Program&gt; can bootstrap it in tests.</summary>
public partial class Program
{
    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "Startup failed during role/permission seeding or Administration bootstrap. " +
                  "If this is a new environment, has the database been migrated (see README: 'dotnet ef database update')?")]
    private static partial void LogStartupSeedFailure(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Email__Smtp__TlsMode is None: email, including Responsibility Term PDFs, is sent to the mail server unencrypted. " +
                  "Use StartTls or SslOnConnect unless the mail server is a trusted relay on a private network.")]
    private static partial void LogSmtpWithoutTls(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client profile: {ClientProfile} ({ProductName}).")]
    private static partial void LogClientProfile(ILogger logger, string clientProfile, string productName);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Client profile {ClientProfile} is a demonstration profile: its Responsibility Term wording is illustrative " +
                  "and has not been legally reviewed. Use an organisation's own reviewed client profile for real use.")]
    private static partial void LogDemonstrationProfile(ILogger logger, string clientProfile);
}
