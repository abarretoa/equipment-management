namespace EquipmentManagement.Domain.DataImports;

/// <summary>
/// The server-owned snapshot binding one Excel preview to its later
/// confirmation (Phase 8 scope). A confirmation request
/// identifies only <see cref="Id"/> — it never re-submits browser-interpreted
/// row data, so "preview one dataset, confirm a different dataset" is
/// structurally impossible.
///
/// <see cref="RowsJson"/> holds the normalized preview rows as a `jsonb`
/// array of a fixed, concrete DTO type chosen by <see cref="ImportType"/>
/// (<c>ConsultantImportRowSnapshot</c> or <c>EquipmentImportRowSnapshot</c>)
/// — never polymorphic/untrusted deserialization (Phase 8 scope).
/// This may contain Consultant/Equipment PII: that is acceptable, while it is
/// still needed, because this is operational database state, not a log or
/// audit trail — audit/log output must never contain it (see the Phase 8 ADR
/// on this). It is retained only for as long as it is operationally
/// necessary to preview and confirm the import — see ADR-060 and ADR-077: <see cref="RowsJson"/> is set to <see langword="null"/> the
/// moment it stops being needed (once <see cref="Status"/> reaches
/// <see cref="ImportSessionStatus.Completed"/>/<see cref="ImportSessionStatus.Cancelled"/>,
/// or once a <see cref="ImportSessionStatus.Pending"/> session is past
/// <see cref="ExpiresAtUtc"/> — by the scheduled cleanup (ADR-077) or on its
/// next access, whichever comes first) — every other
/// column here is deliberately non-PII operational metadata that survives
/// that clearing.
///
/// The raw uploaded workbook itself is never persisted — only
/// <see cref="FileSha256"/> is kept, for traceability (Phase 8 scope).
/// </summary>
public sealed class ImportSession
{
    public Guid Id { get; set; }

    public ImportType ImportType { get; set; }

    public ImportSessionStatus Status { get; set; }

    public required string OriginalFileName { get; set; }

    public required string FileSha256 { get; set; }

    public int TotalRows { get; set; }

    public int ValidRows { get; set; }

    public int WarningRows { get; set; }

    public int ErrorRows { get; set; }

    /// <summary>
    /// Null once no longer needed: after <see cref="Status"/> becomes
    /// <see cref="ImportSessionStatus.Completed"/>/<see cref="ImportSessionStatus.Cancelled"/>,
    /// or after a <see cref="ImportSessionStatus.Pending"/> session passes
    /// <see cref="ExpiresAtUtc"/> (scheduled cleanup, ADR-077, or lazily on access). A non-null value here for
    /// a still-<see cref="ImportSessionStatus.Pending"/>,
    /// still-unexpired session is the only state carrying Consultant/
    /// Equipment PII.
    /// </summary>
    public string? RowsJson { get; set; }

    /// <summary>
    /// Fixed at creation from the configured validity window
    /// (<c>ImportLimitsOptions.SessionValidityHours</c>) — stored, not
    /// recomputed from <see cref="CreatedAtUtc"/> on every check, so a later
    /// configuration change never retroactively shifts an already-issued
    /// session's expiry.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public required string CreatedByDisplayNameSnapshot { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid? ConfirmedByUserId { get; set; }

    public string? ConfirmedByDisplayNameSnapshot { get; set; }

    public DateTime? ConfirmedAtUtc { get; set; }
}
