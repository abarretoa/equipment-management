using EquipmentManagement.Domain.Allocations;

namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// Pure business rules for the handover/delivery-inspection workflow
/// (docs/PROJECT_SPEC.md, sections 9-10, 21). Framework- and database-free so
/// they're unit-testable in isolation — <c>Infrastructure.Handovers.EquipmentHandoverService</c>
/// supplies the actual rows to check these against.
/// </summary>
public static class HandoverRules
{
    /// <summary>Items, inspections and photos can only be added/changed while the handover is still a draft.</summary>
    public static bool CanModify(HandoverStatus status) => status == HandoverStatus.Draft;

    /// <summary>Only a draft can be finalized — finalization is a one-way, one-time transition.</summary>
    public static bool CanFinalize(HandoverStatus status) => status == HandoverStatus.Draft;

    /// <summary>The delivery date must be known and cannot be in the future (docs/PROJECT_SPEC.md, section 10: "data").</summary>
    public static bool IsValidDeliveredOn(DateOnly? deliveredOn, DateOnly today) => deliveredOn is not null && deliveredOn <= today;

    /// <summary>
    /// A Draft handover without a registered dispatch is freely editable, same
    /// as always. Once dispatched, its shipment composition (items) is locked
    /// — the dispatch email/audit record already told the Consultant what to
    /// expect, and the composition must never drift from that after the fact.
    /// The handover itself stays Draft; dispatch introduces no new lifecycle
    /// status. Finalization is unaffected by this lock.
    /// </summary>
    public static bool CanModifyComposition(HandoverStatus status, bool hasDispatch) => status == HandoverStatus.Draft && !hasDispatch;

    /// <summary>
    /// The delivery method belongs to the shipment composition (Iteration
    /// 3): it may be chosen or changed while the handover is a
    /// Draft with no dispatch, and is locked by exactly the same rule as the
    /// Consultant and items once a dispatch exists.
    /// </summary>
    public static bool CanChangeDeliveryMethod(HandoverStatus status, bool hasDispatch) => CanModifyComposition(status, hasDispatch);

    /// <summary>
    /// A Consultant may receive a NEW delivery only while they have their one
    /// <see cref="AllocationStatus.Active"/> allocation (Iteration
    /// 3). Deliberately reuses the existing allocation lifecycle rather than
    /// comparing dates: <see cref="AllocationStatus"/> is explicit and an
    /// allocation is only ever over through a deliberate "end" action, so an
    /// Active allocation whose StartDate is still in the future (a planned
    /// engagement) qualifies too, which lets the company prepare and ship equipment
    /// before the Consultant's first working day. No Active allocation never
    /// qualifies. More than one is inconsistent data (see
    /// <see cref="AllocationRules.CanCreateActiveAllocation"/> and its partial
    /// unique index) and is reported as such, never resolved by choosing one.
    /// This is an eligibility gate only: it never creates, ends or otherwise
    /// touches custody (<see cref="EquipmentAssignment"/>).
    /// </summary>
    public static ConsultantHandoverEligibility ResolveConsultantEligibility(int activeAllocationCount) => activeAllocationCount switch
    {
        <= 0 => ConsultantHandoverEligibility.NoActiveAllocation,
        1 => ConsultantHandoverEligibility.Eligible,
        _ => ConsultantHandoverEligibility.MultipleActiveAllocations,
    };

    /// <summary>
    /// "Registar envio" only applies to a <see cref="HandoverDeliveryMethod.Carrier"/>
    /// handover. An in-person delivery has no shipping event, and a legacy
    /// handover with no method recorded must have one chosen first.
    /// </summary>
    public static bool AllowsDispatch(HandoverDeliveryMethod? deliveryMethod) => deliveryMethod == HandoverDeliveryMethod.Carrier;

    /// <summary>
    /// An abandoned Draft may be deleted only before its operational lock:
    /// still Draft, no dispatch registered ("Registar envio" locks it, like the
    /// composition), and nothing operational hanging off it — no custody
    /// (<see cref="EquipmentAssignment"/>) and no Responsibility Term, whose
    /// immutable artifacts are never deleted with a Draft. Any of those on a
    /// nominal Draft is unexpected data, so deletion fails closed.
    /// </summary>
    public static bool CanDeleteDraft(HandoverStatus status, bool hasDispatch, bool hasAssignment, bool hasResponsibilityTerm) =>
        status == HandoverStatus.Draft && !hasDispatch && !hasAssignment && !hasResponsibilityTerm;

    /// <summary>A Carrier handover cannot be confirmed until its dispatch has been registered; an in-person one never needs one.</summary>
    public static bool RequiresDispatchBeforeFinalize(HandoverDeliveryMethod deliveryMethod) => deliveryMethod == HandoverDeliveryMethod.Carrier;

    /// <summary>
    /// "Anular envio" cancels the active dispatch of a Carrier handover only
    /// before delivery is confirmed: still Draft, with an active dispatch.
    /// Once Confirmar entrega has run, custody exists and the only way back is
    /// a Return. The service also requires every item still InTransit, no
    /// Active assignment and no signed term.
    /// </summary>
    public static bool CanCancelDispatch(HandoverStatus status, HandoverDeliveryMethod? deliveryMethod, bool hasActiveDispatch) =>
        status == HandoverStatus.Draft && deliveryMethod == HandoverDeliveryMethod.Carrier && hasActiveDispatch;
}
