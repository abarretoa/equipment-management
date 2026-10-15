using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EquipmentManagement.Infrastructure.Identity;

/// <summary>
/// Creates the first Administration user from configuration, so no real
/// credentials are ever committed to source control or a migration
/// (Phase 1 scope). Disabled
/// unless <c>BootstrapAdmin__Enabled=true</c> is explicitly set. Safe to invoke on
/// every startup: if a user with the configured email already exists, this is a
/// pure no-op — it never touches that user's password, role, or any other field,
/// so leaving it enabled across restarts cannot silently overwrite an
/// administrator's later changes (e.g. if they changed their own password).
/// </summary>
public sealed partial class AdminBootstrapper(
    UserManager<ApplicationUser> userManager,
    IOptions<BootstrapAdminOptions> options,
    ApplicationDbContext dbContext,
    AuditService auditService,
    ILogger<AdminBootstrapper> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var config = options.Value;
        if (!config.Enabled)
        {
            return;
        }

        var missingFields = new List<string>();
        if (string.IsNullOrWhiteSpace(config.Email))
        {
            missingFields.Add(nameof(config.Email));
        }

        if (string.IsNullOrWhiteSpace(config.FirstName))
        {
            missingFields.Add(nameof(config.FirstName));
        }

        if (string.IsNullOrWhiteSpace(config.LastName))
        {
            missingFields.Add(nameof(config.LastName));
        }

        if (string.IsNullOrWhiteSpace(config.Phone))
        {
            missingFields.Add(nameof(config.Phone));
        }

        if (string.IsNullOrWhiteSpace(config.Password))
        {
            missingFields.Add(nameof(config.Password));
        }

        if (missingFields.Count > 0)
        {
            // Never include the configured values themselves (the password above
            // all) in this message — only which fields were missing.
            throw new InvalidOperationException(
                $"BootstrapAdmin is enabled but incomplete. Missing: {string.Join(", ", missingFields)}. " +
                "Set every BootstrapAdmin__* environment variable (see .env.example) or disable BootstrapAdmin__Enabled.");
        }

        if (await userManager.FindByEmailAsync(config.Email!) is not null)
        {
            LogAlreadyExists(logger);
            return;
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = config.Email,
            Email = config.Email,
            EmailConfirmed = true,
            PhoneNumber = config.Phone,
            FirstName = config.FirstName!,
            LastName = config.LastName!,
            IsActive = true,
            // Unlike admin-created users, this password is operator-controlled
            // (sourced from a secret manager, not shown on a screen to be typed
            // in later), so there is no "temporary password" to rotate.
            MustChangePassword = false,
        };

        // User, role and audit row commit together (ADR-076): a crash between
        // them must not leave a role-less account that the "already exists"
        // check above would then skip forever.
        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);

        var createResult = await userManager.CreateAsync(user, config.Password!);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Code));
            throw new InvalidOperationException($"Failed to create bootstrap Administration user: {errors}");
        }

        await userManager.AddToRoleAsync(user, Roles.Administration);

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.System.AdministrationUserBootstrapped,
            EntityType: "User",
            EntityId: user.Id.ToString(),
            ActorUserId: null,
            ActorDisplayNameSnapshot: "System (bootstrap)"), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        LogCreated(logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Bootstrap Administration user already exists; skipping.")]
    private static partial void LogAlreadyExists(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bootstrap Administration user provisioned.")]
    private static partial void LogCreated(ILogger logger);
}
