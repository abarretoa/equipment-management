namespace EquipmentManagement.Infrastructure.Files;

/// <summary>Bound from the "PrivateStorage" configuration section (see appsettings.json / .env.example).</summary>
public sealed class PrivateStorageOptions
{
    public const string SectionName = "PrivateStorage";

    /// <summary>
    /// Filesystem root for <see cref="LocalPrivateFileStorage"/>. Deliberately
    /// outside any directory served by <c>UseStaticFiles</c> (which this API
    /// doesn't even call) and outside source control (docs/PROJECT_SPEC.md,
    /// section 23: "ficheiros devem ser privados por padrão"). Relative paths
    /// resolve against the content root.
    /// </summary>
    public string RootPath { get; set; } = "private-storage";

    /// <summary>
    /// Upper bound on one uploaded photo (docs/PROJECT_SPEC.md, section 22:
    /// "restrição de tamanho"). 10 MB is generous for a phone photo while
    /// blocking pathological uploads; confirmed only as an MVP default (the final
    /// limit is a deployment decision).
    /// </summary>
    public long MaxPhotoSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Upper bound on one uploaded signed Responsibility Term PDF (Phase 7 scope) — generous for a scanned/photographed signed
    /// document while still blocking pathological uploads. Confirmed only as
    /// an MVP default, same caveat as <see cref="MaxPhotoSizeBytes"/>.
    /// </summary>
    public long MaxSignedDocumentSizeBytes { get; set; } = 15 * 1024 * 1024;
}
