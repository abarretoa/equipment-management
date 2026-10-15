namespace EquipmentManagement.Domain.DataImports;

/// <summary>
/// A small, truthful status model (Phase 8 scope) — no automated
/// "Expired" status is claimed anywhere; expiry is a validity window checked at
/// confirmation time (see <c>ImportSessionRules.CanConfirm</c>). The scheduled
/// cleanup (ADR-077) only clears an expired session's RowsJson; it never
/// changes its status.
/// </summary>
public enum ImportSessionStatus
{
    /// <summary>Previewed, not yet confirmed — confirmable while within the validity window and free of row errors.</summary>
    Pending,

    /// <summary>Persisted successfully. Terminal — a completed session can never be confirmed again.</summary>
    Completed,

    /// <summary>Explicitly invalidated by its creator before confirmation (Phase 8 scope). Terminal.</summary>
    Cancelled,
}
