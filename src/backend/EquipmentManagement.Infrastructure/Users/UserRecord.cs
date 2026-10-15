namespace EquipmentManagement.Infrastructure.Users;

/// <summary>
/// The safe, service-layer projection of an internal user — never the
/// <c>ApplicationUser</c> persistence entity itself (docs/PROJECT_SPEC.md, section
/// 35). <see cref="ConcurrencyStamp"/> is exposed so a caller can round-trip it on
/// update/role-change/activation requests for optimistic-concurrency detection
/// (Phase 1 scope).
/// </summary>
public sealed record UserRecord(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    string? Address,
    string Role,
    bool IsActive,
    bool MustChangePassword,
    string ConcurrencyStamp);

/// <summary>The result of creating a user: the record plus the one-time temporary password.</summary>
public sealed record CreatedUser(UserRecord User, string TemporaryPassword);
