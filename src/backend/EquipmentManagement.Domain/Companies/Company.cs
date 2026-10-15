namespace EquipmentManagement.Domain.Companies;

/// <summary>
/// A normalized external company/client reference used by
/// <c>Allocations.ConsultantAllocation</c> (docs/PROJECT_SPEC.md, section 19).
/// Deliberately minimal — no financial, billing, contact or address fields; this
/// entity exists only so allocations can point at a stable, deduplicated company
/// identity. See ADR-019.
/// </summary>
public sealed class Company
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// Case-insensitive-comparable form of <see cref="Name"/> (see
    /// <see cref="CompanyRules.NormalizeName"/>), used for duplicate detection.
    /// Never shown to a user — purely a lookup/uniqueness aid.
    /// </summary>
    public required string NormalizedName { get; set; }

    /// <summary>
    /// Archived companies are never hard-deleted (docs/PROJECT_SPEC.md, section
    /// 16) — existing historical allocations keep referencing them, but a new
    /// allocation cannot be created against an inactive company.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
