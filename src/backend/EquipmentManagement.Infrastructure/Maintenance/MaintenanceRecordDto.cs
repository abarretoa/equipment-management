using EquipmentManagement.Domain.Maintenance;

namespace EquipmentManagement.Infrastructure.Maintenance;

/// <summary><see cref="EquipmentTypeName"/>/<see cref="Manufacturer"/>/<see cref="Model"/> let the list identify equipment beyond its AssetTag alone (Phase 9 refinement).</summary>
public sealed record MaintenanceSummaryRecord(
    Guid Id, Guid EquipmentId, string AssetTag, string EquipmentTypeName, string? Manufacturer, string? Model,
    MaintenanceStatus Status, DateTime SentAtUtc, DateTime? CompletedAtUtc);

public sealed record MaintenanceDetailRecord(
    Guid Id,
    Guid EquipmentId,
    string AssetTag,
    string EquipmentTypeName,
    Guid? DamageId,
    MaintenanceStatus Status,
    string? Notes,
    MaintenanceOutcome? Outcome,
    DateTime SentAtUtc,
    DateTime? CompletedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record StartMaintenanceData(string? Notes);

public sealed record CompleteMaintenanceData(MaintenanceOutcome Outcome, string? Notes);

public sealed record MaintenanceListFilter(Guid? EquipmentId, MaintenanceStatus? Status);
