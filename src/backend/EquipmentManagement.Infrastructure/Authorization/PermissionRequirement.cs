using Microsoft.AspNetCore.Authorization;

namespace EquipmentManagement.Infrastructure.Authorization;

/// <summary>
/// Marks an authorization policy as requiring a single named permission (see
/// EquipmentManagement.Domain.Users.Permissions). One policy is registered per
/// permission at startup, so controllers can write
/// [Authorize(Policy = Permissions.Consultant.Read)] instead of checking roles.
/// </summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
