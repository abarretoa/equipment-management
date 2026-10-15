using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.Infrastructure.Equipments;

public sealed record EquipmentTypeRecord(
    Guid Id, string Name, EquipmentTypeKind Kind, string AssetTagPrefix,
    bool IsActive, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record CreateEquipmentTypeData(string Name, EquipmentTypeKind Kind, string AssetTagPrefix);

/// <summary>
/// <see cref="EquipmentType.Kind"/> is deliberately absent — immutable after
/// creation, since it decides whether equipment registered under this type
/// requires/forbids a <see cref="LaptopSpecification"/>, and existing equipment
/// already made that decision. <see cref="AssetTagPrefix"/> may change safely:
/// an issued <see cref="Equipment.AssetTag"/> is a stored, immutable string,
/// never recomputed from the type's current prefix (see ADR-026).
/// </summary>
public sealed record UpdateEquipmentTypeData(string Name, string AssetTagPrefix);

public sealed record EquipmentTypeListFilter(string? Search, bool? IsActive);
