namespace EquipmentManagement.Domain.ResponsibilityTerms;

/// <summary>
/// The minimal lifecycle needed for Phase 6 (docs/PROJECT_SPEC.md, section 11;
/// Phase 6 scope). <c>docs/PROJECT_SPEC.md</c> itself suggests a
/// richer eventual vocabulary — NOT_GENERATED, GENERATED, SENT, and later
/// SIGNED/ACCEPTED — but NOT_GENERATED needs no enum member here (the absence
/// of any <see cref="ResponsibilityTerm"/> row for a handover already means
/// that), and SENT/SIGNED/ACCEPTED describe email-delivery and
/// digital-signature workflows that are explicitly out of scope for this
/// phase. Adding that vocabulary now, before either feature exists, would be
/// dead configuration — the same reasoning already applied to
/// <c>Permissions.Equipment.Return</c> in Phase 4 before Phase 5 implemented
/// it.
/// </summary>
public enum ResponsibilityTermStatus
{
    /// <summary>The current, most recently generated version for its handover — at most one per handover (see the partial unique index).</summary>
    Generated,

    /// <summary>
    /// A prior version, replaced by a later regeneration. Terminal — a
    /// superseded term is never reactivated; its PDF bytes, hash, and
    /// metadata remain permanently unchanged and downloadable (Phase 6 scope).
    /// </summary>
    Superseded,
}
