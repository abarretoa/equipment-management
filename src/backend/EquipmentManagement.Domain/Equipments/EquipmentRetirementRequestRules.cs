namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// Deliberately delegates eligibility to <see cref="EquipmentLifecycleRules.CanRetire"/>
/// rather than duplicating it — a request may only be created (and later
/// approved) for equipment that could be retired right now. Whether a
/// specific request can still be decided is a separate, request-level
/// concern (its own <see cref="EquipmentRetirementRequestStatus"/>).
/// </summary>
public static class EquipmentRetirementRequestRules
{
    public static bool CanRequest(EquipmentStatus equipmentStatus) => EquipmentLifecycleRules.CanRetire(equipmentStatus);

    public static bool CanDecide(EquipmentRetirementRequestStatus status) => status == EquipmentRetirementRequestStatus.Pending;
}
