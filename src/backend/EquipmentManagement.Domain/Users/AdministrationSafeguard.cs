namespace EquipmentManagement.Domain.Users;

/// <summary>
/// The "don't lock the company out of Administration access" invariant
/// (Phase 1 scope; see ADR-016). Deliberately a pure function over a
/// caller-supplied count rather than a query itself: the Infrastructure service
/// counts other active Administration users (a database concern) and this class
/// only encodes the decision, so the rule is unit-testable without a database.
/// </summary>
public static class AdministrationSafeguard
{
    /// <summary>
    /// True when it is safe to deactivate a user, remove them from the
    /// Administration role, or otherwise revoke their Administration access,
    /// given how many OTHER active Administration users exist besides them.
    /// </summary>
    public static bool CanRemoveAdministrationAccess(int otherActiveAdministratorCount) =>
        otherActiveAdministratorCount > 0;
}
