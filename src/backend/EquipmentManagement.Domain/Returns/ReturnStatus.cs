namespace EquipmentManagement.Domain.Returns;

/// <summary>
/// The return-process lifecycle (docs/PROJECT_SPEC.md, section 12; Phase 5 scope) — deliberately separate from <see cref="Equipments.EquipmentStatus"/>,
/// which tracks the equipment's own lifecycle in parallel.
/// </summary>
public enum ReturnStatus
{
    /// <summary>Initiated; equipment remains physically under the consultant's responsibility.</summary>
    Pending,

    /// <summary>Equipment has been physically received back — this is the moment custody actually ends (see <c>ReturnRules</c>).</summary>
    Received,

    /// <summary>Return inspection finalized; a terminal state, same as <see cref="Handovers.HandoverStatus.Finalized"/>'s role for handovers.</summary>
    Completed,

    /// <summary>
    /// The return process was called off before physical receipt (initiated by
    /// mistake, offboarding cancelled, etc. — Phase 5 cancellation amendment).
    /// Terminal, but unlike <see cref="Completed"/> it does not permanently
    /// consume the assignment's one-return lifetime: custody was never
    /// disturbed, so a later legitimate return can still be initiated for the
    /// same assignment (see ADR-044 and the corresponding partial
    /// unique index).
    /// </summary>
    Cancelled,
}
