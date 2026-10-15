namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// Explicit lifecycle state for <see cref="EquipmentHandover"/>
/// (docs/PROJECT_SPEC.md, section 10). A draft can still gain/lose items and
/// have its inspections recorded; once <see cref="Finalized"/> the handover,
/// its items and their snapshots become historical and immutable — see the
/// ADR on handover finalization.
/// </summary>
public enum HandoverStatus
{
    Draft,
    Finalized,
}
