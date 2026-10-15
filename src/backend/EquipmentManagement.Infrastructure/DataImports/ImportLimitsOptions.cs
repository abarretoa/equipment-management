using System.ComponentModel.DataAnnotations;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// Conservative, configurable resource-exhaustion limits for Excel import
/// (Phase 8 scope) — this is an internal, low-volume tool, not a
/// big-data ETL platform. Bound from the "Imports" configuration section.
/// </summary>
public sealed class ImportLimitsOptions
{
    public const string SectionName = "Imports";

    public long MaxUploadBytes { get; set; } = 5 * 1024 * 1024;

    public int MaxRowsPerImport { get; set; } = 2000;

    public int MaxColumns { get; set; } = 30;

    public int MaxCellLength { get; set; } = 1000;

    public int MaxWorksheets { get; set; } = 5;

    /// <summary>
    /// How long a <c>Domain.DataImports.ImportSession</c> stays confirmable
    /// after creation (Phase 8 scope; Phase 8 retention review).
    /// Applied once, at preview time, to compute the session's fixed
    /// <c>ExpiresAtUtc</c> — changing this value later never retroactively
    /// shifts an already-issued session's expiry.
    /// </summary>
    public int SessionValidityHours { get; set; } = 24;

    /// <summary>
    /// How often <c>ExpiredImportSessionCleanupWorker</c> clears the preview
    /// snapshot of expired, still-Pending sessions (ADR-077). Bounded to
    /// 1 minute..24 hours; validated at startup.
    /// </summary>
    [Range(1, 1440)]
    public int ExpiredSessionCleanupIntervalMinutes { get; set; } = 60;
}
