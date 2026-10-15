namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// The full lifecycle vocabulary (docs/PROJECT_SPEC.md, section 8; see
/// docs/DOMAIN_MODEL.md for every transition that produces each value).
/// Persisted and serialized by name, never by ordinal.
/// </summary>
public enum EquipmentStatus
{
    Available,

    /// <summary>
    /// Shipped on a carrier Handover ("Registar envio") and not yet confirmed
    /// as received ("Em trânsito"). An operational state only: it is never
    /// proof of custody. There is no Assignment until Confirmar entrega, and
    /// current custody derives only from an Active <c>EquipmentAssignment</c>.
    /// </summary>
    InTransit,

    Assigned,
    ReturnPending,
    UnderInspection,
    Damaged,
    UnderMaintenance,
    Retired,
}
