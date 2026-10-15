namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// Pure validation/normalization rules for <see cref="Equipment"/>
/// (docs/PROJECT_SPEC.md, section 7; Phase 3 scope). Framework-free
/// so these are unit-testable without a database.
/// </summary>
public static class EquipmentRules
{
    /// <summary>Laptop and MobilePhone both require a manufacturer — both render a "Marca e Modelo" field on their Responsibility Term (Phase 3 scope).</summary>
    public static bool RequiresManufacturer(EquipmentTypeKind kind) => kind is EquipmentTypeKind.Laptop or EquipmentTypeKind.MobilePhone;

    /// <summary>Only Laptop requires a serial number — a MobilePhone is identified by its IMEI (<see cref="MobilePhoneSpecification"/>) instead (Phase 3 scope).</summary>
    public static bool RequiresSerialNumber(EquipmentTypeKind kind) => kind == EquipmentTypeKind.Laptop;

    /// <summary>
    /// Only Laptop and MobilePhone equipment carries a current condition photo
    /// set — the only two categories the Responsibility Term's photo
    /// requirement covers. A real enum discriminator, never a display-name
    /// string comparison.
    /// </summary>
    public static bool RequiresConditionPhotos(EquipmentTypeKind kind) => kind is EquipmentTypeKind.Laptop or EquipmentTypeKind.MobilePhone;

    /// <summary>
    /// "Alterar fotografias" is a correction outside custody and outside any
    /// in-flight delivery: never while a consultant holds the equipment
    /// (custody comes from the Active assignment, not the status label), never
    /// while it sits on a dispatched-but-unconfirmed carrier Handover (whose
    /// prepared term may reference the current set), and never once Retired.
    /// Return receipt remains the normal way photos change in the lifecycle.
    /// </summary>
    public static CurrentPhotoCorrectionEligibility ResolveCurrentPhotoCorrection(
        EquipmentStatus status, bool hasActiveCustody, bool isInDispatchedHandover) =>
        hasActiveCustody ? CurrentPhotoCorrectionEligibility.InCustody
        : isInDispatchedHandover || status == EquipmentStatus.InTransit ? CurrentPhotoCorrectionEligibility.InDispatchedHandover
        : status == EquipmentStatus.Retired ? CurrentPhotoCorrectionEligibility.Retired
        : CurrentPhotoCorrectionEligibility.Eligible;

    public static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>Trims and upper-invariants for case-insensitive duplicate comparison; null/blank stays null (no serial number).</summary>
    public static string? NormalizeSerialNumber(string? serialNumber) =>
        string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim().ToUpperInvariant();
}
