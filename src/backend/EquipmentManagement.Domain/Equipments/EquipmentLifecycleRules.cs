namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// Governs which lifecycle transitions are currently implemented
/// (docs/PROJECT_SPEC.md, sections 8-9, 21). Transitions belonging to Phase 6+
/// (ReturnPending, UnderInspection, Damaged, UnderMaintenance) belong to a
/// future phase's application service, not this one — see
/// <c>Infrastructure.Equipments.EquipmentService</c> (retire) and
/// <c>Infrastructure.Handovers.EquipmentHandoverService</c> (assign), which
/// together are the only code paths that can change
/// <see cref="Equipment.Status"/>.
/// </summary>
public static class EquipmentLifecycleRules
{
    /// <summary>
    /// <c>Available</c> equipment may be retired directly. <c>Damaged</c>
    /// equipment may also be retired, to support the operational case where
    /// a damaged/non-repairable item is permanently removed from service
    /// (e.g. after a Maintenance <c>NotRepairable</c> outcome leaves it
    /// <c>Damaged</c> — see <c>Infrastructure.Maintenance.MaintenanceService</c>)
    /// without that retirement ever being automatic; Administration must
    /// still take the explicit retire action. Every other status is rejected:
    /// retiring already-assigned equipment out from under a consultant, or
    /// equipment mid-return/inspection/maintenance, is not a supported flow
    /// (docs/PROJECT_SPEC.md, section 16).
    /// </summary>
    public static bool CanRetire(EquipmentStatus currentStatus) =>
        currentStatus == EquipmentStatus.Available || currentStatus == EquipmentStatus.Damaged;

    /// <summary>
    /// Only <c>Available</c> equipment may be handed over (docs/PROJECT_SPEC.md,
    /// invariants #2-3: Retired equipment can never be assigned, and
    /// Damaged/UnderMaintenance equipment is blocked in the MVP pending an
    /// explicit future rule).
    /// </summary>
    public static bool CanAssign(EquipmentStatus currentStatus) => currentStatus == EquipmentStatus.Available;

    /// <summary>
    /// "Registar envio" ships only <c>Available</c> equipment, and moves it to
    /// <see cref="EquipmentStatus.InTransit"/>. The same item may sit in
    /// several Drafts; the first dispatch commits it, and every other Draft
    /// can no longer progress with it.
    /// </summary>
    public static bool CanDispatch(EquipmentStatus currentStatus) => currentStatus == EquipmentStatus.Available;

    /// <summary>
    /// The status each item must have for "Confirmar entrega" to move it to
    /// <see cref="EquipmentStatus.Assigned"/>: a carrier delivery confirms
    /// equipment it shipped (<see cref="EquipmentStatus.InTransit"/>); an
    /// in-person delivery hands over <see cref="EquipmentStatus.Available"/>
    /// equipment directly and never passes through InTransit.
    /// </summary>
    public static EquipmentStatus RequiredStatusForDeliveryConfirmation(bool isCarrier) =>
        isCarrier ? EquipmentStatus.InTransit : EquipmentStatus.Available;

    /// <summary>
    /// "Anular envio" returns shipped equipment (<see cref="EquipmentStatus.InTransit"/>)
    /// to <see cref="EquipmentStatus.Available"/>; anything else means the
    /// item is no longer in that dispatch's safe state, and the whole
    /// cancellation is refused.
    /// </summary>
    public static bool CanCancelDispatch(EquipmentStatus currentStatus) => currentStatus == EquipmentStatus.InTransit;
}
