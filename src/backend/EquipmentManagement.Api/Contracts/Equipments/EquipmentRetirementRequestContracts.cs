using System.ComponentModel.DataAnnotations;
using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.Api.Contracts.Equipments;

public sealed record CreateEquipmentRetirementRequestRequest(
    [Required] Guid EquipmentId,
    [Required, MaxLength(2000)] string Reason);

public sealed record RejectEquipmentRetirementRequestRequest([Required, MaxLength(2000)] string Reason);

/// <summary>Row shape for the Administration-only "Solicitações de abate" review list and for the create/approve/reject responses.</summary>
public sealed record EquipmentRetirementRequestResponse(
    Guid Id,
    Guid EquipmentId,
    string AssetTag,
    string EquipmentTypeName,
    string? Manufacturer,
    string? Model,
    EquipmentStatus EquipmentStatus,
    string RequestedByDisplayName,
    DateTime RequestedAtUtc,
    string RequestReason,
    EquipmentRetirementRequestStatus Status,
    string? DecidedByDisplayName,
    DateTime? DecidedAtUtc,
    string? DecisionReason);
