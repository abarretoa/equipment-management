using Microsoft.AspNetCore.Identity;

namespace EquipmentManagement.Infrastructure.Identity;

/// <summary>
/// Internal application user (Administration/Directors or HR). Consultants are a
/// separate domain entity (future phase) and never get a login here — see
/// docs/PROJECT_SPEC.md, section 4.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Soft-deactivation flag. Disabled users are excluded from sign-in even if
    /// their Identity account still technically exists, preserving audit history
    /// tied to their id (see docs/PROJECT_SPEC.md, section 16: archive over delete).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// HR profiles require an address (docs/PROJECT_SPEC.md, section 5);
    /// Administration profiles do not. Kept as a single nullable field validated
    /// conditionally by <see cref="Domain.Users.UserProfileRules"/> at
    /// create/update time, rather than a role-specific table or schema constraint
    /// — a user's role can change, and a nullable-but-conditionally-required field
    /// avoids that turning into a migration.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Set when Administration creates this user with a temporary password (or
    /// resets it) and cleared once the user successfully changes it. Enforced
    /// server-side by <c>RequirePasswordChangeMiddleware</c>, not just surfaced to
    /// the frontend (Phase 1 scope).
    /// </summary>
    public bool MustChangePassword { get; set; }
}
