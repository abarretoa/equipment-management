namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// How an <see cref="EquipmentAssignment"/> came to exist. Persisted as text
/// (same convention as every other enum here). A database CHECK constraint
/// ties it to the Handover references: <see cref="Handover"/> requires both
/// <see cref="EquipmentAssignment.EquipmentHandoverId"/> and
/// <see cref="EquipmentAssignment.EquipmentHandoverItemId"/>;
/// <see cref="CurrentInventoryImport"/> requires both to be null.
/// </summary>
public enum EquipmentAssignmentOrigin
{
    /// <summary>Created by finalizing an <see cref="EquipmentHandover"/> — the normal operational path.</summary>
    Handover,

    /// <summary>
    /// Established by the Current Inventory import: custody that already
    /// existed in the real world before it was recorded in the application
    /// (initial migration / go-live). There was no delivery, so there is no
    /// Handover, no delivery inspection and no Responsibility Term — none is
    /// fabricated to fill the gap.
    /// </summary>
    CurrentInventoryImport,
}
