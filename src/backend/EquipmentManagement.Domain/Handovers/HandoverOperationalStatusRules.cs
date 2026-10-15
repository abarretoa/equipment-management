namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// The single source of truth for deriving <see cref="HandoverOperationalStatus"/>
/// (Iteration 1). Exact rule: Draft handovers are
/// always <see cref="HandoverOperationalStatus.Draft"/>; a Finalized handover
/// is <see cref="HandoverOperationalStatus.Completed"/> only when its current
/// (Status == Generated — at most one per handover, see
/// <see cref="ResponsibilityTerms.ResponsibilityTerm"/>'s remarks) Responsibility
/// Term has a signed artifact on record, and
/// <see cref="HandoverOperationalStatus.AwaitingSignedTerm"/> otherwise —
/// including when no term has been generated yet at all, and
/// regardless of any older Superseded version's own signature (item C5: a
/// signed superseded version never completes the current delivery).
/// </summary>
public static class HandoverOperationalStatusRules
{
    /// <param name="status">The handover's real, persisted lifecycle status.</param>
    /// <param name="hasSignedCurrentTerm">
    /// Whether the handover's current (Generated) Responsibility Term has a
    /// <see cref="ResponsibilityTerms.ResponsibilityTermSignedArtifact"/> on
    /// record — false both when no term has been generated yet and when the
    /// current term exists but is unsigned. Meaningless (and ignored) while
    /// <paramref name="status"/> is <see cref="HandoverStatus.Draft"/>.
    /// </param>
    public static HandoverOperationalStatus Derive(HandoverStatus status, bool hasSignedCurrentTerm) => status switch
    {
        HandoverStatus.Draft => HandoverOperationalStatus.Draft,
        HandoverStatus.Finalized when hasSignedCurrentTerm => HandoverOperationalStatus.Completed,
        HandoverStatus.Finalized => HandoverOperationalStatus.AwaitingSignedTerm,
        _ => HandoverOperationalStatus.AwaitingSignedTerm,
    };
}
