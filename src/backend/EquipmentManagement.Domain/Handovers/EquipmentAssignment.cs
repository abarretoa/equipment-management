namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// The authoritative record of which consultant currently holds which piece of
/// equipment (docs/PROJECT_SPEC.md, sections 9, 20-21, invariant #1: "um
/// equipamento não pode estar atribuído simultaneamente a dois consultores").
/// Created by <see cref="EquipmentHandover"/> finalization, one row per
/// finalized <see cref="EquipmentHandoverItem"/> (<see cref="EquipmentAssignmentOrigin.Handover"/>),
/// or — for custody that already existed before the application recorded it —
/// by the Current Inventory import (<see cref="EquipmentAssignmentOrigin.CurrentInventoryImport"/>,
/// which has no Handover at all). Never mutated to represent a new custody; a
/// change of holder always ends this row (Phase 6) and creates a new one via a
/// new handover. This is the single source of truth for "who currently has
/// this equipment" — <see cref="Equipments.Equipment"/> itself deliberately
/// carries no consultant reference (see its own remarks), whatever the origin.
/// A partial unique index on (EquipmentId) WHERE Status = 'Active' is the
/// database-enforced backstop for the invariant above.
/// </summary>
public sealed class EquipmentAssignment
{
    public Guid Id { get; set; }

    public Guid EquipmentId { get; set; }

    public Guid ConsultantId { get; set; }

    /// <summary>See <see cref="EquipmentAssignmentOrigin"/>; a CHECK constraint keeps it consistent with the two Handover references below.</summary>
    public EquipmentAssignmentOrigin Origin { get; set; }

    /// <summary>The finalized Handover this custody came from — null exactly when <see cref="Origin"/> is <see cref="EquipmentAssignmentOrigin.CurrentInventoryImport"/>.</summary>
    public Guid? EquipmentHandoverId { get; set; }

    /// <summary>Null exactly when <see cref="EquipmentHandoverId"/> is.</summary>
    public Guid? EquipmentHandoverItemId { get; set; }

    public AssignmentStatus Status { get; set; }

    /// <summary>
    /// When this custody began. For a Handover, the finalization instant; for
    /// a Current Inventory import, the date HR supplied (at 00:00 UTC — the
    /// workbook carries no time of day, and none is invented) or, when none
    /// was supplied, the moment the import was confirmed.
    /// </summary>
    public DateTime StartedAtUtc { get; set; }

    public DateTime? EndedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
