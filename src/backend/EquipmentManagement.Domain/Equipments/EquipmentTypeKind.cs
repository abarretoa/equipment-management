namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// Distinguishes equipment types that carry structured laptop specifications
/// from every other (generic) type, without comparing type names as strings
/// (Phase 3 scope). Persisted as text,
/// not the numeric value (same convention as Phase 2's enums).
/// </summary>
public enum EquipmentTypeKind
{
    Generic,
    Laptop,

    /// <summary>Carries a <see cref="MobilePhoneSpecification"/> 1:0..1 extension, mirroring Laptop's own — the exact extension anticipated by ADR-027.</summary>
    MobilePhone,
}
