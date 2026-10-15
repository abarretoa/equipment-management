using System.ComponentModel.DataAnnotations;
using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.Api.Contracts.Equipments;

public sealed record CreateEquipmentTypeRequest(
    [Required, MaxLength(200)] string Name,
    [Required] EquipmentTypeKind Kind,
    [Required, MaxLength(6)] string AssetTagPrefix);

/// <summary><see cref="EquipmentTypeKind"/> is immutable after creation — see <c>UpdateEquipmentTypeData</c>'s remarks.</summary>
public sealed record UpdateEquipmentTypeRequest(
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(6)] string AssetTagPrefix);

public sealed record EquipmentTypeResponse(
    Guid Id, string Name, EquipmentTypeKind Kind, string AssetTagPrefix, bool IsActive, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
