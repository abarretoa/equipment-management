namespace EquipmentManagement.Domain.Damages;

/// <summary>
/// A recorded equipment defect (docs/PROJECT_SPEC.md, section 13; invariant #9).
/// Created exactly once, at return-inspection finalization, from the inspection's
/// draft <c>HasDamage</c>/<c>DamageDescription</c> fields — never a mutable draft
/// row itself (same materialize-at-finalization pattern as
/// <see cref="Handovers.EquipmentHandover.ConsultantSnapshot"/>). Deliberately no
/// Severity field for MVP (docs/PROJECT_SPEC.md, section 13, offers it only as
/// "pode"; Phase 5 scope — an unconfirmed classification is not worth
/// inventing now, and a nullable column can be added later without breaking
/// anything). Scoped to at most one per <see cref="ReturnInspectionId"/> for
/// this phase's workflow (see the unique index) — docs/PROJECT_SPEC.md, section
/// 20's 1:N relation remains available to widen to later without a breaking
/// change, if a future requirement needs multiple distinct damage entries per
/// return.
/// </summary>
public sealed class Damage
{
    public Guid Id { get; set; }

    public Guid EquipmentId { get; set; }

    public Guid EquipmentReturnId { get; set; }

    public Guid ReturnInspectionId { get; set; }

    public required string Description { get; set; }

    public DamageStatus Status { get; set; }

    public DateTime ReportedAtUtc { get; set; }

    public Guid ReportedByUserId { get; set; }

    public required string ReportedByDisplayNameSnapshot { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }
}
