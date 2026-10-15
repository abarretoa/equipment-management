namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// Structured technical data required only for laptops
/// (docs/PROJECT_SPEC.md, section 7). A 1:0..1 extension of
/// <see cref="Equipment"/>, not a generic JSON bag — see ADR-027 on why. Values
/// are free text on purpose (Phase 3 scope): "16 GB" / "512 GB SSD" / "1 TB NVMe" don't share one numeric
/// shape worth normalizing prematurely.
/// </summary>
public sealed class LaptopSpecification
{
    /// <summary>Both primary key and foreign key to <see cref="Equipment"/> — see ApplicationDbContext.</summary>
    public Guid EquipmentId { get; set; }

    public required string Processor { get; set; }

    public required string Memory { get; set; }

    public required string Storage { get; set; }
}
