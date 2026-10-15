using System.ComponentModel.DataAnnotations;
using EquipmentManagement.Domain.Maintenance;

namespace EquipmentManagement.Api.Contracts.Maintenance;

public sealed record StartMaintenanceRequest(string? Notes);

public sealed record CompleteMaintenanceRequest([Required] MaintenanceOutcome Outcome, string? Notes);

public sealed record MaintenanceSummaryResponse(
    Guid Id, Guid EquipmentId, string AssetTag, string EquipmentTypeName, string? Manufacturer, string? Model,
    MaintenanceStatus Status, DateTime SentAtUtc, DateTime? CompletedAtUtc);

public sealed record MaintenanceDetailResponse(
    Guid Id, Guid EquipmentId, string AssetTag, string EquipmentTypeName, Guid? DamageId, MaintenanceStatus Status,
    string? Notes, MaintenanceOutcome? Outcome, DateTime SentAtUtc, DateTime? CompletedAtUtc, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
