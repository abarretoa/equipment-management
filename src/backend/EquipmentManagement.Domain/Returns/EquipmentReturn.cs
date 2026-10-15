namespace EquipmentManagement.Domain.Returns;

/// <summary>
/// A single return process for one <see cref="Handovers.EquipmentAssignment"/>
/// (docs/PROJECT_SPEC.md, section 12; Phase 5 scope). Deliberately
/// scoped to one assignment, not the whole originating multi-item
/// <see cref="Handovers.EquipmentHandover"/> — a consultant may return a laptop
/// today and a monitor tomorrow, each as its own independent return.
/// <see cref="EquipmentId"/> and <see cref="ConsultantId"/> are read-convenience
/// copies of immutable facts already established by the assignment at return time
/// (not mutable state duplicated from a live row) — see ADR-038.
/// Never hard-deleted; a completed (or abandoned mid-flight) return remains
/// historical evidence forever, same as <see cref="Handovers.EquipmentHandover"/>.
/// </summary>
public sealed class EquipmentReturn
{
    public Guid Id { get; set; }

    public Guid EquipmentAssignmentId { get; set; }

    public Guid EquipmentId { get; set; }

    public Guid ConsultantId { get; set; }

    /// <summary>
    /// The initiation event this return was created by (see
    /// <see cref="EquipmentReturnGroup"/>), set once at creation and never
    /// changed. Null only for returns created before grouping existed; they
    /// are never backfilled.
    /// </summary>
    public Guid? ReturnGroupId { get; set; }

    public ReturnStatus Status { get; set; }

    /// <summary>Free-text, optional — no fixed reason taxonomy (Phase 5 scope: "if a simple optional reason text is more appropriate for MVP, prefer that").</summary>
    public string? Reason { get; set; }

    public DateOnly? ExpectedReturnDate { get; set; }

    /// <summary>The business date of physical receipt, set only by <c>EquipmentReturnService.ReceiveAsync</c>.</summary>
    public DateOnly? ReceivedOn { get; set; }

    public DateTime? ReceivedAtUtc { get; set; }

    /// <summary>
    /// The internal user who physically received the equipment — may differ from
    /// the authenticated actor who recorded the action (docs/PROJECT_SPEC.md,
    /// section 12: "esses dois responsáveis podem ser diferentes"). A historical
    /// snapshot (<see cref="ReceivedByDisplayNameSnapshot"/>), not a live lookup —
    /// see Phase 5 scope.
    /// </summary>
    public Guid? ReceivedByUserId { get; set; }

    public string? ReceivedByDisplayNameSnapshot { get; set; }

    public Guid CreatedByUserId { get; set; }

    public required string CreatedByDisplayNameSnapshot { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Set only when the return inspection is finalized (Status becomes <see cref="ReturnStatus.Completed"/>).</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Set only when the return is cancelled (Status becomes <see cref="ReturnStatus.Cancelled"/>) — see <c>EquipmentReturnService.CancelAsync</c>.</summary>
    public DateTime? CancelledAtUtc { get; set; }

    /// <summary>
    /// The internal user who cancelled the return — the audit actor
    /// (<c>AuditEntry.ActorUserId</c>) is always the authenticated caller;
    /// this is that same caller for cancellation (unlike <see cref="ReceivedByUserId"/>,
    /// there is no "someone else physically did this" distinction here).
    /// </summary>
    public Guid? CancelledByUserId { get; set; }

    /// <summary>
    /// Historical snapshot, not a live lookup — readable even if the
    /// cancelling user's name later changes or they become inactive.
    /// </summary>
    public string? CancelledByDisplayNameSnapshot { get; set; }

    /// <summary>Optional free text (e.g. "Return initiated in error."). Never copied into the audit trail — see <c>AuditActions.EquipmentReturn.Cancelled</c>'s remarks.</summary>
    public string? CancellationReason { get; set; }
}
