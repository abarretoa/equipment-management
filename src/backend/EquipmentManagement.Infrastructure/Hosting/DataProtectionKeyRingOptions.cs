namespace EquipmentManagement.Infrastructure.Hosting;

/// <summary>
/// Where ASP.NET Core Data Protection keeps its key ring. The auth cookie and
/// the antiforgery token are both encrypted with these keys, so they must
/// outlive the process: with the framework default (a directory inside the
/// container) every redeploy would sign everyone out. Required in Production
/// (see <see cref="ProductionConfigurationValidator"/>); optional elsewhere,
/// where the framework default is kept.
/// <para>
/// The application name that isolates these keys from any other application
/// sharing a key store comes from the client profile
/// (<c>ClientIdentity.DataProtectionApplicationName</c>), so it stays stable
/// across deployments regardless of the content root path the framework
/// would otherwise derive it from.
/// </para>
/// </summary>
public static class DataProtectionKeyRingOptions
{
    public const string KeyRingPathKey = "DataProtection:KeyRingPath";
}
