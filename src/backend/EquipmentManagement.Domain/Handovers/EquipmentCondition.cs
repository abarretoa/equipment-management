namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// The physical condition recorded by a <see cref="DeliveryInspection"/>
/// (docs/PROJECT_SPEC.md, section 10 — "estado de cada equipamento"). Deliberately
/// separate from <see cref="Equipments.EquipmentStatus"/>, which tracks the
/// equipment's *custody/lifecycle* state, not its physical condition at one
/// point in time.
/// </summary>
public enum EquipmentCondition
{
    Excellent,
    Good,
    Fair,
    Poor,
}
