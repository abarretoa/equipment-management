using EquipmentManagement.Domain.Handovers;

namespace EquipmentManagement.Domain.Returns;

/// <summary>
/// The return-time inspection of exactly one <see cref="EquipmentReturn"/>
/// (docs/PROJECT_SPEC.md, section 12). At most one per return (see the unique
/// index on <see cref="EquipmentReturnId"/>) — recorded/updated freely while the
/// return is <see cref="ReturnStatus.Received"/>, then finalized exactly once.
/// Reuses <see cref="EquipmentCondition"/> and <see cref="Handovers.EquipmentSnapshot"/>
/// from the Phase 4 handover workflow rather than inventing parallel, incompatible
/// vocabularies (Phase 5 scope).
/// </summary>
public sealed class ReturnInspection
{
    public Guid Id { get; set; }

    public Guid EquipmentReturnId { get; set; }

    public EquipmentCondition Condition { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// A mutable draft field until finalization, at which point (if true) it
    /// drives creation of the corresponding <see cref="Damages.Damage"/> record —
    /// see <c>EquipmentReturnService.FinalizeInspectionAsync</c>.
    /// </summary>
    public bool HasDamage { get; set; }

    /// <summary>
    /// Draft damage description, required only at finalization time when
    /// <see cref="HasDamage"/> is true (checked there, not at every save — same
    /// "gate at finalization, not at every edit" philosophy as the Phase 4
    /// photo-minimum rule). Copied onto the materialized <see cref="Damages.Damage"/>
    /// row at finalization; not re-read from here afterward.
    /// </summary>
    public string? DamageDescription { get; set; }

    public Guid InspectedByUserId { get; set; }

    public required string InspectedByDisplayNameSnapshot { get; set; }

    public DateTime InspectedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Set only once, at finalization — after this, the inspection (and its photos) become immutable historical evidence.</summary>
    public DateTime? FinalizedAtUtc { get; set; }

    /// <summary>
    /// Null until finalization, then populated once from live data — the same
    /// snapshot-at-finalization pattern as <see cref="EquipmentHandoverItem.EquipmentSnapshot"/>
    /// (see ADR-030). Enables a later delivery-vs-return condition
    /// comparison without ever re-querying the (by-then possibly reassigned)
    /// live Equipment row.
    /// </summary>
    public EquipmentSnapshot? EquipmentSnapshot { get; set; }
}
