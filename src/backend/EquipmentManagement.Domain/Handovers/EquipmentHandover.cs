namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// A delivery event for one or more equipment items to one consultant
/// (docs/PROJECT_SPEC.md, sections 9-10, 19-20). Carries no navigation
/// properties (like ConsultantAllocation and Equipment: reads go
/// through projections, docs/PROJECT_SPEC.md, section 24) —
/// <see cref="EquipmentHandoverItem"/> rows reference this by
/// <see cref="EquipmentHandoverItem.EquipmentHandoverId"/>.
/// </summary>
public sealed class EquipmentHandover
{
    public Guid Id { get; set; }

    public Guid ConsultantId { get; set; }

    public HandoverStatus Status { get; set; }

    /// <summary>
    /// The business date of physical delivery. Settable while <see cref="Draft"/>
    /// but must be set, and not in the future, before <see cref="Finalize"/> can
    /// succeed — see <c>HandoverRules.IsValidDeliveredOn</c>.
    /// </summary>
    public DateOnly? DeliveredOn { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Required for every handover created from Iteration 3
    /// onwards. Null is still a valid <em>historical</em> state: handovers
    /// created before the field existed were never backfilled, because their
    /// real delivery method is unknown and guessing would rewrite history. A
    /// legacy Draft with a null value must have a method chosen before it can
    /// be dispatched or finalized; a legacy Finalized handover simply stays
    /// null forever. Locked once an <see cref="EquipmentDispatch"/> exists,
    /// same as the rest of the shipment composition.
    /// </summary>
    public HandoverDeliveryMethod? DeliveryMethod { get; set; }

    /// <summary>The internal user who registered this handover (docs/PROJECT_SPEC.md, section 10: "utilizador responsável pelo registo").</summary>
    public Guid CreatedByUserId { get; set; }

    public required string CreatedByDisplayNameSnapshot { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Set only at finalization — see ADR-030.</summary>
    public DateTime? FinalizedAtUtc { get; set; }

    /// <summary>
    /// Null while <see cref="Draft"/>; populated once, at finalization, from the
    /// consultant row as it exists at that moment (docs/PROJECT_SPEC.md, section
    /// 11: "O PDF histórico não pode mudar se o consultor... for editado
    /// posteriormente"). A later edit to the live <c>Consultant</c> row never
    /// changes this. It is the Handover's delivery record (used by e.g. the
    /// equipment history); every Responsibility Term version owns its own
    /// separate copy of the data it was rendered from.
    /// </summary>
    public ConsultantSnapshot? ConsultantSnapshot { get; set; }

    /// <summary>
    /// Null while <see cref="Draft"/>, and remains null after finalization if the
    /// consultant had no active allocation at that moment — the professional
    /// context is still worth recording historically when it exists, but its
    /// absence is not an error (docs/PROJECT_SPEC.md, section 6).
    /// </summary>
    public AllocationSnapshot? AllocationSnapshot { get; set; }
}

/// <summary>Owned, immutable-once-written snapshot of the consultant's PII at finalization time. Never updated after finalization.</summary>
public sealed class ConsultantSnapshot
{
    public required string FullName { get; set; }

    public required string Nif { get; set; }

    public required string Address { get; set; }

    public required string Phone { get; set; }

    public required string Email { get; set; }
}

/// <summary>Owned snapshot of the consultant's active professional context at finalization time, if any.</summary>
public sealed class AllocationSnapshot
{
    public required string WorkforceType { get; set; }

    public required string EngagementType { get; set; }

    public required string RoleOrFunction { get; set; }

    public string? CompanyName { get; set; }
}
