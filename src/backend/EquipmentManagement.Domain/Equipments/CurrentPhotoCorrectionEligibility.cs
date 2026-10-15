namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// Whether an equipment's CURRENT condition photo set may be administratively
/// corrected ("Alterar fotografias") — see
/// <see cref="EquipmentRules.ResolveCurrentPhotoCorrection"/>. Only
/// <see cref="Eligible"/> allows it; every other value names the reason.
/// </summary>
public enum CurrentPhotoCorrectionEligibility
{
    Eligible,

    /// <summary>An Active <c>EquipmentAssignment</c> exists — the consultant has custody (Assigned, or ReturnPending before receipt).</summary>
    InCustody,

    /// <summary>The equipment is on a carrier Handover whose dispatch is registered but not yet confirmed: its composition is locked and a prepared term may reference the current set.</summary>
    InDispatchedHandover,

    /// <summary>Retired equipment's record is closed.</summary>
    Retired,
}
