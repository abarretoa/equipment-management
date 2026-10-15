namespace EquipmentManagement.Domain.DataImports;

/// <summary>
/// Pure business rules for the ImportSession preview-to-confirm-and-retire
/// lifecycle (Phase 8 scope; Phase 8 retention review).
/// Framework- and database-free. The validity window duration itself is a
/// configuration concern (<c>ImportLimitsOptions.SessionValidityHours</c>,
/// Infrastructure) applied once at session creation to compute
/// <see cref="ImportSession.ExpiresAtUtc"/> — these rules only ever compare
/// "now" against that already-fixed timestamp, never recompute a window.
/// </summary>
public static class ImportSessionRules
{
    /// <summary>True once <paramref name="nowUtc"/> is past the session's fixed expiry.</summary>
    public static bool IsExpired(DateTime expiresAtUtc, DateTime nowUtc) => nowUtc > expiresAtUtc;

    /// <summary>
    /// True specifically for a still-<see cref="ImportSessionStatus.Pending"/>
    /// session that has passed its expiry — the case that must both reject
    /// confirmation with a distinct, actionable error
    /// (<c>ServiceErrorCode.ImportSessionExpired</c>, not the generic
    /// "not confirmable") and trigger lazy PII cleanup of
    /// <see cref="ImportSession.RowsJson"/> on next access.
    /// </summary>
    public static bool IsExpiredPending(ImportSessionStatus status, DateTime expiresAtUtc, DateTime nowUtc) =>
        status == ImportSessionStatus.Pending && IsExpired(expiresAtUtc, nowUtc);

    /// <summary>
    /// All-or-nothing (Phase 8 scope): a session with any error
    /// row can never be confirmed, regardless of warnings — HR must correct
    /// the file and re-preview. Only <see cref="ImportSessionStatus.Pending"/>
    /// sessions within their validity window are confirmable.
    /// </summary>
    public static bool CanConfirm(ImportSessionStatus status, int errorRows, DateTime expiresAtUtc, DateTime nowUtc) =>
        status == ImportSessionStatus.Pending && errorRows == 0 && !IsExpired(expiresAtUtc, nowUtc);

    /// <summary>Only a Pending session may be explicitly cancelled (Phase 8 scope) — expired-but-still-Pending included, since cancelling one is just an earlier, deliberate version of the same PII cleanup that would otherwise happen lazily.</summary>
    public static bool CanCancel(ImportSessionStatus status) => status == ImportSessionStatus.Pending;
}
