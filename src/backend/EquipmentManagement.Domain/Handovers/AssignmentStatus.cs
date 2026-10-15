namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// Explicit lifecycle state for <see cref="EquipmentAssignment"/>. Phase 4 only
/// ever produces <see cref="Active"/> (via handover finalization); <see cref="Ended"/>
/// is future vocabulary for the Phase 6 return flow, declared now so the shape
/// doesn't change later — same rationale as <c>Permissions.Equipment.Return</c>.
/// </summary>
public enum AssignmentStatus
{
    Active,
    Ended,
}
