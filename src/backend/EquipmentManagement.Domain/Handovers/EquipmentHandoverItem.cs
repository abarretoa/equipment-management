namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// One equipment item within an <see cref="EquipmentHandover"/>
/// (docs/PROJECT_SPEC.md, sections 10, 20). An <see cref="Equipments.Equipment"/>
/// can appear in many items over time (one per handover it's ever part of), but
/// at most one such item may back a currently-<see cref="AssignmentStatus.Active"/>
/// <see cref="EquipmentAssignment"/> — enforced by a partial unique index on
/// EquipmentAssignment, not here.
/// </summary>
public sealed class EquipmentHandoverItem
{
    public Guid Id { get; set; }

    public Guid EquipmentHandoverId { get; set; }

    public Guid EquipmentId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Populated only at finalization — see <see cref="EquipmentHandover.ConsultantSnapshot"/>'s remarks for the same rationale.</summary>
    public EquipmentSnapshot? EquipmentSnapshot { get; set; }

    /// <summary>
    /// The equipment's <see cref="Equipments.EquipmentPhotoSet"/> that was
    /// <see cref="Equipments.PhotoSetStatus.Current"/> at the exact moment
    /// this Handover was finalized — populated once, at finalization, for
    /// Laptop/MobilePhone items only, and never touched again (for a carrier
    /// delivery, copied from the prepared term version's own link). A
    /// Responsibility Term generated after delivery copies this exact frozen
    /// set into its own <c>ResponsibilityTermItem</c>, never whichever set
    /// happens to be Current "now"; each version keeps its own link forever,
    /// even after a later Return changes the equipment's current condition
    /// photos. Null for non-photo-required kinds, and for
    /// any Handover finalized before this column existed (see ADR-079).
    /// </summary>
    public Guid? EquipmentPhotoSetId { get; set; }
}

/// <summary>Owned, immutable-once-written snapshot of the equipment's identifying data at finalization time (docs/PROJECT_SPEC.md, section 11).</summary>
public sealed class EquipmentSnapshot
{
    public required string AssetTag { get; set; }

    public required string EquipmentTypeName { get; set; }

    public string? Manufacturer { get; set; }

    public string? Model { get; set; }

    public string? SerialNumber { get; set; }

    public string? Processor { get; set; }

    public string? Memory { get; set; }

    public string? Storage { get; set; }

    /// <summary>The equipment type's <c>EquipmentTypeKind</c> at finalization time, as its string name — a real discriminator, snapshotted so device-specific Responsibility Term rendering never needs a live EquipmentType lookup.</summary>
    public string? Kind { get; set; }

    public string? Imei { get; set; }

    public string? MobileNumber { get; set; }

    public string? SimSerialNumber { get; set; }

    /// <summary>The equipment's free-text condition notes at finalization time — renders as "Outras Informações" on the device-specific Term.</summary>
    public string? Notes { get; set; }
}
