using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Allocations;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Authorization;
using EquipmentManagement.Infrastructure.ClientProfiles;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Companies;
using EquipmentManagement.Infrastructure.Consultants;
using EquipmentManagement.Infrastructure.DataImports;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.Infrastructure.Equipments;
using EquipmentManagement.Infrastructure.Files;
using EquipmentManagement.Infrastructure.Handovers;
using EquipmentManagement.Infrastructure.Hosting;
using EquipmentManagement.Infrastructure.Documents;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Maintenance;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.Infrastructure.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Returns;
using EquipmentManagement.Infrastructure.Users;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EquipmentManagement.Infrastructure;

/// <summary>
/// Composition entry point for the Infrastructure layer: persistence, Identity,
/// cookie authentication and permission-policy authorization. Called once from
/// EquipmentManagement.Api's Program.cs (the composition root).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // IsNullOrWhiteSpace, not just null: appsettings.json's own Production default is
        // ConnectionStrings:Default = "" (present but empty, a fail-safe placeholder — see
        // that file) — a bare `?? throw` never fires against an empty string, so an
        // unconfigured Production deployment would sail past this check and only fail much
        // later, confusingly, on the first real database access (Phase 10 hardening).
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'Default' is not configured. Set ConnectionStrings__Default (see .env.example).");
        }

        // A Production instance must be able to do its whole job from the first
        // request: real issuer, usable SMTP settings, a public URL, and evidence
        // files and session keys on storage that outlives the container. Checked
        // here, before anything is built, so a misconfigured deployment stops with
        // one message listing every problem instead of reporting itself healthy.
        if (environment.IsProduction())
        {
            ProductionConfigurationValidator.ThrowIfInvalid(configuration);
        }

        // The client profile compiled into this build (docs/WHITE_LABEL.md).
        // Loading it validates it, so a broken profile stops startup here.
        var clientProfile = ClientProfileLoader.Embedded;
        ClientProfileLoader.EnsureExpected(clientProfile, configuration[ClientProfileLoader.ExpectedProfileKey]);
        services.AddSingleton(clientProfile);

        AddDataProtectionKeyRing(services, configuration, clientProfile.Identity.DataProtectionApplicationName);

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Tightened beyond ASP.NET Core Identity's defaults: this app holds
                // consultant personal data and, eventually, financial data.
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
                options.User.RequireUniqueEmail = true;
                // Internal users are provisioned administratively (Phase 1), not
                // self-registered, so there is no signup email-confirmation flow.
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager<ApplicationSignInManager>()
            .AddClaimsPrincipalFactory<ApplicationClaimsPrincipalFactory>()
            .AddErrorDescriber<PortugueseIdentityErrorDescriber>()
            .AddDefaultTokenProviders();

        services.AddHttpContextAccessor();

        services
            .AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = "EquipmentManagement.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;

                // This is a JSON API consumed by a SPA: never redirect to an HTML
                // login page (ASP.NET Core Identity's cookie default). Return plain
                // status codes so the frontend can react programmatically.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };

                // Phase 0 wired Identity via AddIdentityCore, so the framework's
                // own SecurityStampValidator (which AddIdentity would register
                // automatically) is absent. This closes that gap: see
                // ActiveUserCookieValidator's remarks for the full rationale.
                options.Events.OnValidatePrincipal = ActiveUserCookieValidator.ValidateAsync;
            });

        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(permission, policy =>
                    policy.Requirements.Add(new PermissionRequirement(permission)));
            }

            // Phase 10 hardening: every controller action today carries its own
            // explicit [Authorize] (verified directly — every mutating and every
            // read action across every controller has one, except the two
            // deliberately public exceptions: AuthController.Login and
            // AntiforgeryController.GetToken, both marked [AllowAnonymous]). This
            // fallback policy doesn't change that today; it exists so a FUTURE
            // action added without an [Authorize] attribute fails closed (401) by
            // default instead of silently becoming public — the project has no
            // AddControllersWithViews()-style convention check for this otherwise.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        services.AddScoped<IdentitySeeder>();

        services.AddOptions<BootstrapAdminOptions>()
            .Bind(configuration.GetSection(BootstrapAdminOptions.SectionName));
        services.AddScoped<AdminBootstrapper>();

        services.AddScoped<AuditService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<UserManagementService>();
        services.AddScoped<CompanyService>();
        services.AddScoped<ConsultantService>();
        services.AddScoped<ConsultantAllocationService>();
        services.AddScoped<ConsultantAllocationCollectionNotifier>();
        services.AddScoped<AssetTagGenerator>();
        services.AddScoped<EquipmentTypeService>();
        services.AddScoped<EquipmentService>();
        services.AddScoped<EquipmentHistoryService>();

        services.AddOptions<PrivateStorageOptions>()
            .Bind(configuration.GetSection(PrivateStorageOptions.SectionName));
        services.AddSingleton<IPrivateFileStorage, LocalPrivateFileStorage>();
        services.AddScoped<EquipmentHandoverService>();
        services.AddScoped<EquipmentDispatchService>();
        services.AddScoped<EquipmentAssignmentQueryService>();
        services.AddScoped<EquipmentReturnInitiator>();
        services.AddScoped<EquipmentReturnService>();
        services.AddScoped<EquipmentReturnGroupQueryService>();
        services.AddScoped<MaintenanceService>();

        services.AddOptions<ApplicationLinkOptions>().Bind(configuration.GetSection(ApplicationLinkOptions.SectionName));
        services.AddScoped<EquipmentRetirementRequestService>();

        services.AddOptions<DocumentIssuerOptions>()
            .Bind(configuration.GetSection(DocumentIssuerOptions.SectionName));
        services.AddSingleton<IResponsibilityTermPdfGenerator, ResponsibilityTermPdfGenerator>();
        services.AddScoped<DocumentNumberGenerator>();
        services.AddScoped<ResponsibilityTermService>();

        // Email (Phase 7). Automated tests ("Testing" environment) never send
        // a real email — the in-memory recording fake is registered instead,
        // shared as a singleton so a test can inspect what a request sent
        // (Phase 7 scope). Every other environment uses the
        // generic SMTP sender, which fails safely with a classified
        // EmailSendException (never a crash, never a fabricated success) when
        // Email:Smtp:Host/FromAddress are not configured.
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));
        services.AddOptions<EmailSignatureOptions>().Bind(configuration.GetSection(EmailSignatureOptions.SectionName));
        if (environment.IsEnvironment("Testing"))
        {
            // Registered under its concrete type too, so integration tests can
            // resolve the same singleton directly (via ApiWebApplicationFactory.Services)
            // to assert on what a request actually sent.
            services.AddSingleton<RecordingEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<RecordingEmailSender>());
        }
        else
        {
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }

        services.AddScoped<EmailFailureLogger>();

        services.AddScoped<ResponsibilityTermDeliveryService>();
        services.AddScoped<ResponsibilityTermSignedArtifactService>();

        // Excel import/export (Phase 8).
        services.AddOptions<ImportLimitsOptions>()
            .Bind(configuration.GetSection(ImportLimitsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<ExcelWorkbookReader>();
        services.AddScoped<ConsultantImportService>();
        services.AddScoped<EquipmentImportService>();
        services.AddScoped<CurrentInventoryImportService>();
        services.AddScoped<ConsultantExportService>();
        services.AddScoped<EquipmentExportService>();

        // Expired preview PII is cleared on a schedule, not only on next access
        // (ADR-077). Not started under "Testing": integration tests call
        // ExpiredImportSessionCleaner directly with a fixed clock, so no test
        // depends on — or is disturbed by — a real timer.
        services.AddScoped<ExpiredImportSessionCleaner>();
        if (!environment.IsEnvironment("Testing"))
        {
            services.AddHostedService<ExpiredImportSessionCleanupWorker>();
        }

        services
            .AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>("postgresql", tags: ["ready"])
            .AddCheck<Files.PrivateStorageHealthCheck>("private-storage", tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Persists the Data Protection key ring to <c>DataProtection:KeyRingPath</c>
    /// when one is configured (always, in Production — see
    /// <see cref="ProductionConfigurationValidator"/>). Without it the framework
    /// keeps the keys inside the container, so recreating the container would
    /// invalidate every auth cookie and antiforgery token. This changes only
    /// where the keys live: the cookie settings and the per-request user,
    /// security-stamp and permission-fingerprint validation are untouched.
    /// </summary>
    /// <remarks>
    /// The keys are stored unencrypted, readable only by the application user;
    /// the directory must be a private, persistent volume. With no path
    /// configured (Development, Testing) the framework default is kept.
    /// </remarks>
    private static void AddDataProtectionKeyRing(IServiceCollection services, IConfiguration configuration, string applicationName)
    {
        var keyRingPath = configuration[DataProtectionKeyRingOptions.KeyRingPathKey];
        if (string.IsNullOrWhiteSpace(keyRingPath))
        {
            return;
        }

        if (WritableDirectory.Check(keyRingPath, "DataProtection__KeyRingPath") is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        services.AddDataProtection()
            .SetApplicationName(applicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
    }
}
