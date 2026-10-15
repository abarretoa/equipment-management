using EquipmentManagement.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EquipmentManagement.Infrastructure.Identity;

/// <summary>
/// Ensures the two fixed application roles exist and carry the current
/// permission-claim mapping from <see cref="RolePermissions"/>. Deliberately does
/// NOT create any user accounts — see <see cref="AdminBootstrapper"/> for that,
/// a separate, explicitly opt-in concern (committing seeded credentials, even
/// "dev-only" ones, is a real secret-hygiene risk). Idempotent — safe to run on
/// every startup, in every environment: role/permission data (unlike schema
/// migrations, see ADR-008) must exist wherever the app runs.
/// </summary>
public sealed partial class IdentitySeeder(
    RoleManager<ApplicationRole> roleManager,
    ILogger<IdentitySeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var roleName in Roles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new ApplicationRole(roleName);
                var createResult = await roleManager.CreateAsync(role);
                if (!createResult.Succeeded)
                {
                    var errors = string.Join(", ", createResult.Errors.Select(e => e.Code));
                    LogRoleSeedFailed(logger, roleName, errors);
                    throw new InvalidOperationException($"Failed to seed role '{roleName}': {errors}");
                }

                LogRoleSeeded(logger, roleName);
            }

            await SyncPermissionClaimsAsync(role, roleName, cancellationToken);
        }
    }

    /// <summary>
    /// Makes the role's stored permission claims match <see cref="RolePermissions"/>
    /// exactly: adds missing ones, removes ones no longer granted. This is what
    /// keeps role-to-permission mapping "data, not code deployed once" (ADR-005) —
    /// changing the central map and restarting the app is enough to apply it.
    /// </summary>
    private async Task SyncPermissionClaimsAsync(ApplicationRole role, string roleName, CancellationToken cancellationToken)
    {
        var desired = RolePermissions.GetPermissions(roleName).ToHashSet(StringComparer.Ordinal);
        var current = await roleManager.GetClaimsAsync(role);
        var currentPermissionClaims = current
            .Where(c => c.Type == Authorization.PermissionAuthorizationHandler.PermissionClaimType)
            .ToList();

        foreach (var staleClaim in currentPermissionClaims.Where(c => !desired.Contains(c.Value)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await roleManager.RemoveClaimAsync(role, staleClaim);
        }

        var alreadyPresent = currentPermissionClaims.Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var permission in desired.Where(p => !alreadyPresent.Contains(p)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(
                Authorization.PermissionAuthorizationHandler.PermissionClaimType, permission));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded role {RoleName}")]
    private static partial void LogRoleSeeded(ILogger logger, string roleName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to seed role {RoleName}: {Errors}")]
    private static partial void LogRoleSeedFailed(ILogger logger, string roleName, string errors);
}
