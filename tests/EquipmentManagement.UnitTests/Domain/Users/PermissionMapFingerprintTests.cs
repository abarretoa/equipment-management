using EquipmentManagement.Domain.Users;

namespace EquipmentManagement.UnitTests.Domain.Users;

/// <summary>The deterministic permission-map fingerprint issued into auth cookies (ADR-078).</summary>
public sealed class PermissionMapFingerprintTests
{
    private static Dictionary<string, IEnumerable<string>> Map(params (string Role, string[] Permissions)[] roles)
    {
        var map = new Dictionary<string, IEnumerable<string>>(StringComparer.Ordinal);
        foreach (var (role, permissions) in roles)
        {
            map[role] = permissions;
        }

        return map;
    }

    private static readonly Dictionary<string, IEnumerable<string>> Baseline =
        Map(("Administration", ["Permission.A", "Permission.B"]), ("HR", ["Permission.C"]));

    [Fact]
    public void SameMap_DifferentInsertionOrder_SameFingerprint()
    {
        var reordered = Map(("HR", ["Permission.C"]), ("Administration", ["Permission.B", "Permission.A"]));

        Assert.Equal(PermissionMapFingerprint.Compute(Baseline), PermissionMapFingerprint.Compute(reordered));
    }

    [Fact]
    public void IsDeterministic_LowerHexSha256()
    {
        var first = PermissionMapFingerprint.Compute(Baseline);

        Assert.Equal(first, PermissionMapFingerprint.Compute(Baseline));
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void PermissionAdded_ChangesFingerprint() =>
        Assert.NotEqual(
            PermissionMapFingerprint.Compute(Baseline),
            PermissionMapFingerprint.Compute(Map(("Administration", ["Permission.A", "Permission.B"]), ("HR", ["Permission.C", "Permission.D"]))));

    [Fact]
    public void PermissionRemoved_ChangesFingerprint() =>
        Assert.NotEqual(
            PermissionMapFingerprint.Compute(Baseline),
            PermissionMapFingerprint.Compute(Map(("Administration", ["Permission.A"]), ("HR", ["Permission.C"]))));

    [Fact]
    public void PermissionMovedBetweenRoles_ChangesFingerprint() =>
        Assert.NotEqual(
            PermissionMapFingerprint.Compute(Baseline),
            PermissionMapFingerprint.Compute(Map(("Administration", ["Permission.A"]), ("HR", ["Permission.B", "Permission.C"]))));

    [Fact]
    public void RoleRenamedOrAdded_ChangesFingerprint()
    {
        Assert.NotEqual(
            PermissionMapFingerprint.Compute(Baseline),
            PermissionMapFingerprint.Compute(Map(("Administration", ["Permission.A", "Permission.B"]), ("Hr", ["Permission.C"]))));
        Assert.NotEqual(
            PermissionMapFingerprint.Compute(Baseline),
            PermissionMapFingerprint.Compute(Map(("Administration", ["Permission.A", "Permission.B"]), ("HR", ["Permission.C"]), ("Auditor", []))));
    }

    [Fact]
    public void IdentifiersAreExact_NotCaseFolded_AndBoundariesAreUnambiguous()
    {
        Assert.NotEqual(
            PermissionMapFingerprint.Compute(Baseline),
            PermissionMapFingerprint.Compute(Map(("Administration", ["permission.a", "Permission.B"]), ("HR", ["Permission.C"]))));
        Assert.NotEqual(
            PermissionMapFingerprint.Compute(Map(("R", ["a,b"]))),
            PermissionMapFingerprint.Compute(Map(("R", ["a", "b"]))));
    }

    [Fact]
    public void DuplicatePermissions_DoNotChangeSemantics() =>
        Assert.Equal(
            PermissionMapFingerprint.Compute(Baseline),
            PermissionMapFingerprint.Compute(Map(("Administration", ["Permission.A", "Permission.B", "Permission.A"]), ("HR", ["Permission.C"]))));

    [Fact]
    public void RolePermissionsFingerprint_IsTheFingerprintOfTheCurrentMap()
    {
        var current = Roles.All.ToDictionary(role => role, role => (IEnumerable<string>)RolePermissions.GetPermissions(role));

        Assert.Equal(PermissionMapFingerprint.Compute(current), RolePermissions.Fingerprint);
    }
}
