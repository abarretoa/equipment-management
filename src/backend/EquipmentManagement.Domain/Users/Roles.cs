namespace EquipmentManagement.Domain.Users;

/// <summary>
/// The two login-capable user profiles defined by docs/PROJECT_SPEC.md, section 4.
/// Lives in Domain (not Infrastructure) so pure role/permission rules
/// (<see cref="RolePermissions"/>, <see cref="UserProfileRules"/>) can reference it
/// without depending on ASP.NET Core Identity.
/// </summary>
public static class Roles
{
    public const string Administration = "Administration";
    public const string HR = "HR";

    public static IReadOnlyCollection<string> All { get; } = [Administration, HR];
}
