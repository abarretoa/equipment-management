using Microsoft.AspNetCore.Authorization;

namespace EquipmentManagement.Infrastructure.Authorization;

/// <summary>
/// Grants a PermissionRequirement when the signed-in user has a "permission" claim
/// matching it. Claims are assigned per role when Phase 1 defines the
/// role-to-permission mapping (via RoleManager.AddClaimAsync) — this handler only
/// evaluates claims already present on the principal, it never decides the mapping.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    public const string PermissionClaimType = "permission";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var hasPermission = context.User.Claims.Any(claim =>
            claim.Type == PermissionClaimType &&
            string.Equals(claim.Value, requirement.Permission, StringComparison.Ordinal));

        if (hasPermission)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
