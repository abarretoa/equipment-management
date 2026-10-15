using EquipmentManagement.Domain.Handovers;

namespace EquipmentManagement.Domain.ResponsibilityTerms;

/// <summary>
/// Pure business rules for Responsibility Term generation eligibility
/// (docs/PROJECT_SPEC.md, section 11; Phase 6 scope). Framework-
/// and database-free — <c>Infrastructure.ResponsibilityTerms.ResponsibilityTermService</c>
/// supplies the actual rows to check these against.
/// </summary>
public static class ResponsibilityTermRules
{
    /// <summary>
    /// A term may be generated for a Finalized handover, or — carrier delivery
    /// only — prepared ahead of physical receipt once "Registar envio" has
    /// locked the composition (see <see cref="CanPrepareBeforeDelivery"/>).
    /// Never for any other Draft.
    /// </summary>
    public static bool CanGenerate(HandoverStatus handoverStatus, HandoverDeliveryMethod? deliveryMethod, bool hasDispatch) =>
        handoverStatus == HandoverStatus.Finalized || CanPrepareBeforeDelivery(handoverStatus, deliveryMethod, hasDispatch);

    /// <summary>
    /// A carrier Draft whose dispatch is registered: the composition is locked,
    /// so the term can be prepared (its snapshot frozen at generation time)
    /// while the equipment is still in transit. In-person Drafts keep the
    /// original rule — generation only after "Confirmar entrega".
    /// </summary>
    public static bool CanPrepareBeforeDelivery(HandoverStatus handoverStatus, HandoverDeliveryMethod? deliveryMethod, bool hasDispatch) =>
        handoverStatus == HandoverStatus.Draft && deliveryMethod == HandoverDeliveryMethod.Carrier && hasDispatch;

    /// <summary>
    /// Sending a term for signature, and registering its signed copy, both
    /// require physical receipt to have been confirmed ("Confirmar entrega",
    /// i.e. a Finalized handover) — a term prepared before delivery is never
    /// sent or signed while the equipment is still in transit.
    /// </summary>
    public static bool AllowsSignatureWorkflow(HandoverStatus handoverStatus) => handoverStatus == HandoverStatus.Finalized;
}
