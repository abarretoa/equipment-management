namespace EquipmentManagement.Domain.Maintenance;

/// <summary>
/// One maintenance episode for a piece of equipment (docs/PROJECT_SPEC.md,
/// section 13; Phase 5 scope). Deliberately minimal — no vendor,
/// cost, quote, or attachment fields (those remain out of scope for the MVP;
/// docs/PROJECT_SPEC.md, section 3). At most one <see cref="MaintenanceStatus.Active"/>
/// row may exist per <see cref="EquipmentId"/> (see the partial unique index) —
/// the database-enforced backstop, mirroring the
/// <see cref="Handovers.EquipmentAssignment"/> pattern from Phase 4.
/// </summary>
public sealed class MaintenanceRecord
{
    public Guid Id { get; set; }

    public Guid EquipmentId { get; set; }

    /// <summary>
    /// The equipment's currently-<see cref="Damages.DamageStatus.Open"/> damage
    /// record at the moment maintenance started, if one exists — optional
    /// because nothing in this codebase currently produces a
    /// <see cref="Equipments.EquipmentStatus.Damaged"/> equipment without one,
    /// but the field stays nullable rather than assuming that will always hold.
    /// </summary>
    public Guid? DamageId { get; set; }

    public MaintenanceStatus Status { get; set; }

    public string? Notes { get; set; }

    public MaintenanceOutcome? Outcome { get; set; }

    public DateTime SentAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public required string CreatedByDisplayNameSnapshot { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
