namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// Reusable reference data describing a *kind* of physical asset (docs/PROJECT_SPEC.md,
/// section 7) — "Laptop," "Monitor," "Mouse." Never the physical asset itself;
/// see <see cref="Equipment"/> for that. Creatable by HR or Administration
/// without a code change (docs/PROJECT_SPEC.md, section 7: "outros tipos
/// criados pelo RH ou Diretoria").
/// </summary>
public sealed class EquipmentType
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Case-insensitive-comparable form of <see cref="Name"/>, used for duplicate detection.</summary>
    public required string NormalizedName { get; set; }

    public EquipmentTypeKind Kind { get; set; }

    /// <summary>
    /// The short prefix (e.g. "LT") baked into every <see cref="Equipment.AssetTag"/>
    /// issued for this type at the time of issuance — see
    /// <see cref="Equipment.AssetTag"/>'s remarks for why editing this later
    /// never changes already-issued tags.
    /// </summary>
    public required string AssetTagPrefix { get; set; }

    /// <summary>
    /// Archived types are never hard-deleted (docs/PROJECT_SPEC.md, section 16)
    /// — existing equipment keeps referencing them, but new equipment cannot be
    /// registered under an inactive type.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
