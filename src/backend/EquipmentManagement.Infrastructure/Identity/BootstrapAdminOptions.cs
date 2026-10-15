namespace EquipmentManagement.Infrastructure.Identity;

/// <summary>
/// Configuration for <see cref="AdminBootstrapper"/>, bound from the
/// "BootstrapAdmin" configuration section — i.e. the
/// <c>BootstrapAdmin__Enabled</c>/<c>BootstrapAdmin__Email</c>/... environment
/// variables (Phase 1 scope).
/// Disabled and empty by default: real values must never be committed, only ever
/// supplied via environment variables or a secret manager.
/// </summary>
public sealed class BootstrapAdminOptions
{
    public const string SectionName = "BootstrapAdmin";

    public bool Enabled { get; set; }

    public string? Email { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Phone { get; set; }

    public string? Password { get; set; }
}
