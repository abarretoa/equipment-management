namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// Structured data required only for mobile phones — a 1:0..1 extension of
/// <see cref="Equipment"/>, mirroring <see cref="LaptopSpecification"/>'s
/// shape exactly (ADR-027 already anticipates this exact extension pattern
/// for a new <see cref="EquipmentTypeKind"/>). Deliberately excludes SIM
/// PIN/PUK: those are security secrets, never persisted in plaintext by this
/// application, even though the original paper form has cells for them.
/// <see cref="MobileNumber"/> is the phone's own
/// telephone number (a client profile may label it after its mobile operator; the
/// domain field stays carrier-neutral).
/// </summary>
public sealed class MobilePhoneSpecification
{
    /// <summary>Both primary key and foreign key to <see cref="Equipment"/> — see ApplicationDbContext.</summary>
    public Guid EquipmentId { get; set; }

    /// <summary>The IMEI as entered, preserved for display.</summary>
    public required string Imei { get; set; }

    /// <summary>
    /// <see cref="MobilePhoneSpecificationRules.NormalizeImei"/> of <see cref="Imei"/>
    /// — the product-wide uniqueness key: no two mobile phones may share an
    /// IMEI, backed by a unique index (same pattern as
    /// <see cref="Equipment.NormalizedSerialNumber"/>).
    /// </summary>
    public required string NormalizedImei { get; set; }

    public string? MobileNumber { get; set; }

    public string? SimSerialNumber { get; set; }
}
