using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.Infrastructure.Equipments;

/// <summary>Embedded on <see cref="EquipmentRecord"/> when the equipment has a currently-Pending request — deliberately minimal, no account/user data beyond the requester's display name.</summary>
public sealed record EquipmentPendingRetirementRequestRecord(
    Guid Id, string RequestedByDisplayName, DateTime RequestedAtUtc, string RequestReason);

public sealed record EquipmentRetirementRequestRecord(
    Guid Id,
    Guid EquipmentId,
    string AssetTag,
    string EquipmentTypeName,
    string? Manufacturer,
    string? Model,
    EquipmentStatus EquipmentStatus,
    Guid RequestedByUserId,
    string RequestedByDisplayName,
    DateTime RequestedAtUtc,
    string RequestReason,
    EquipmentRetirementRequestStatus Status,
    Guid? DecidedByUserId,
    string? DecidedByDisplayName,
    DateTime? DecidedAtUtc,
    string? DecisionReason);

public sealed record EquipmentRetirementRequestListFilter(EquipmentRetirementRequestStatus? Status);
