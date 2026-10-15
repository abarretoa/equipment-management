namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// One individual physical asset (docs/PROJECT_SPEC.md, section 7) — "Laptop
/// #1, Serial ABC123" is a different <see cref="Equipment"/> row from "Laptop
/// #2, Serial XYZ456" even though both share an <see cref="EquipmentType"/>.
/// Deliberately carries no consultant/assignment reference — see ADR-031 on why current responsibility is not denormalized here; that belongs to a
/// future historical assignment record, not this entity.
/// </summary>
public sealed class Equipment
{
    public Guid Id { get; set; }

    /// <summary>
    /// Immutable after creation — see ADR-027.
    /// Changing it would silently invalidate whatever
    /// <see cref="LaptopSpecification"/>/manufacturer-requirement rules applied
    /// at creation time.
    /// </summary>
    public Guid EquipmentTypeId { get; set; }

    /// <summary>
    /// The stable, human-readable internal identifier (e.g. "LT-0023",
    /// docs/PROJECT_SPEC.md, section 7). Generated once at creation from a
    /// database sequence plus the equipment type's prefix at that time (see
    /// <c>Infrastructure.Equipments.AssetTagGenerator</c>) and never
    /// recomputed — so a later edit to <see cref="EquipmentType.AssetTagPrefix"/>
    /// never changes tags already issued. Immutable; never reused, even after
    /// retirement.
    /// </summary>
    public required string AssetTag { get; set; }

    /// <summary>Required when the equipment type's <see cref="EquipmentTypeKind"/> is Laptop; optional otherwise.</summary>
    public string? Manufacturer { get; set; }

    /// <summary>Always optional (docs/PROJECT_SPEC.md, section 7: "modelo opcional" — no laptop exception stated).</summary>
    public string? Model { get; set; }

    /// <summary>The serial number as entered, preserved for display. Required when the equipment type's Kind is Laptop.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>Case-insensitive-comparable form of <see cref="SerialNumber"/>, used for duplicate detection. Null exactly when SerialNumber is null.</summary>
    public string? NormalizedSerialNumber { get; set; }

    public string? Notes { get; set; }

    /// <summary>Optional purchase metadata (Iteration 1) — editable at any time, independent of lifecycle status. No backfill is expected for equipment registered before this field existed.</summary>
    public DateOnly? PurchaseDate { get; set; }

    /// <summary>Free text, no fixed format — suppliers issue invoice numbers in inconsistent shapes.</summary>
    public string? InvoiceNumber { get; set; }

    /// <summary>Free text store/supplier name — no predefined vendor list.</summary>
    public string? PurchaseVendor { get; set; }

    /// <summary>
    /// The only lifecycle field (Phase 3 scope) — deliberately no separate IsActive; Retired already means
    /// "no longer in operational circulation."
    /// </summary>
    public EquipmentStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
