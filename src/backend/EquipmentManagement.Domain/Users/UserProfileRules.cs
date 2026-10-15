namespace EquipmentManagement.Domain.Users;

/// <summary>
/// Pure validation rules for the internal-user profile shape (docs/PROJECT_SPEC.md,
/// section 5 and 11). Deliberately framework-free so these rules are unit-testable
/// without a database or ASP.NET Core Identity — the actual persistence/uniqueness
/// concerns (duplicate email, etc.) belong to the Infrastructure service that calls
/// these, not here.
/// </summary>
public static class UserProfileRules
{
    /// <summary>First/last name limit — the <c>FirstName</c>/<c>LastName</c> column size.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Address limit — the <c>Address</c> column size. Validated on every user/profile request so an over-long value is a 400, never a database error.</summary>
    public const int MaxAddressLength = 500;

    /// <summary>
    /// Phone is optional for internal users: <c>null</c>, empty and whitespace-only
    /// all mean "no phone" and are stored as <c>null</c>, so a phone can be cleared.
    /// Any other value is kept as entered (format is validated at the API boundary).
    /// </summary>
    public static string? NormalizeOptionalPhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? null : phone;

    /// <summary>
    /// Only the two fixed business roles may ever be assigned to an internal user
    /// (docs/PROJECT_SPEC.md, section 3 — never a person's name/title as a role).
    /// </summary>
    public static bool IsSupportedRole(string role) => Roles.All.Contains(role, StringComparer.Ordinal);

    /// <summary>
    /// HR profiles require a home address; Administration profiles do not
    /// (docs/PROJECT_SPEC.md, section 5). Because a user's role can change, this is
    /// a validated-when-relevant rule rather than a schema-level requirement on a
    /// nullable column.
    /// </summary>
    public static bool RequiresAddress(string role) => role == Roles.HR;

    /// <summary>
    /// True when the given address value is missing/blank — used together with
    /// <see cref="RequiresAddress"/> to decide whether a create/update request is
    /// valid for the target role.
    /// </summary>
    public static bool IsAddressMissing(string? address) => string.IsNullOrWhiteSpace(address);
}
