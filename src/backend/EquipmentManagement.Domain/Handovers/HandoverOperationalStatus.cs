namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// Derived, presentation-facing completion status for an
/// <see cref="EquipmentHandover"/> (Iteration 1). Never
/// persisted and never a substitute for <see cref="HandoverStatus"/>: that
/// enum continues to govern the real physical-handover lifecycle (item
/// creation, snapshots, <see cref="EquipmentAssignment"/> creation) exactly as
/// before. This value only changes what the UI calls the handover, computed
/// fresh from <see cref="HandoverStatus"/> plus whether the handover's
/// current (<see cref="ResponsibilityTerms.ResponsibilityTermStatus.Generated"/>)
/// Responsibility Term has a signed artifact — see
/// <see cref="HandoverOperationalStatusRules.Derive"/> for the exact rule.
/// </summary>
public enum HandoverOperationalStatus
{
    /// <summary>Mirrors <see cref="HandoverStatus.Draft"/> exactly.</summary>
    Draft,

    /// <summary>
    /// Physically finalized (custody already transferred: the
    /// <see cref="EquipmentAssignment"/> is Active) but the current
    /// Responsibility Term either does not exist yet or has no signed artifact
    /// on record. Custody is unaffected by this state — see ADR-081
    /// (Iteration 1).
    /// </summary>
    AwaitingSignedTerm,

    /// <summary>Physically finalized AND the current Responsibility Term has a signed artifact on record.</summary>
    Completed,
}
