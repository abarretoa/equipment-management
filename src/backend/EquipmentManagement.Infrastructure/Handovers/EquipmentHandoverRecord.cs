using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;

namespace EquipmentManagement.Infrastructure.Handovers;

/// <summary>Least-privilege list projection — consultant full name only, no NIF/Address/Phone/Email (same discipline as <c>ConsultantSummaryRecord</c>).</summary>
public sealed record EquipmentHandoverSummaryRecord(
    Guid Id,
    Guid ConsultantId,
    string ConsultantFullName,
    HandoverStatus Status,
    HandoverOperationalStatus OperationalStatus,
    DateOnly? DeliveredOn,
    int ItemCount,
    DateTime CreatedAtUtc,
    DateTime? FinalizedAtUtc);

public sealed record ConsultantSnapshotRecord(string FullName, string Nif, string Address, string Phone, string Email);

public sealed record AllocationSnapshotRecord(string WorkforceType, string EngagementType, string RoleOrFunction, string? CompanyName);

public sealed record EquipmentSnapshotRecord(
    string AssetTag, string EquipmentTypeName, string? Manufacturer, string? Model, string? SerialNumber,
    string? Processor, string? Memory, string? Storage);

public sealed record DeliveryInspectionPhotoRecord(
    Guid Id, string OriginalFileName, string ContentType, long SizeBytes, int SortOrder, DateTime UploadedAtUtc);

public sealed record DeliveryInspectionRecord(
    Guid Id,
    EquipmentCondition Condition,
    string? Notes,
    string InspectedByDisplayNameSnapshot,
    DateTime InspectedAtUtc,
    IReadOnlyList<DeliveryInspectionPhotoRecord> Photos);

/// <summary>Detail-level item projection: current live equipment fields (including its live <see cref="EquipmentStatus"/>, which tells a Draft whether the item can still progress) plus the finalized snapshot (null while the handover is still a draft).</summary>
public sealed record EquipmentHandoverItemRecord(
    Guid Id,
    Guid EquipmentId,
    string AssetTag,
    string EquipmentTypeName,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    EquipmentSnapshotRecord? EquipmentSnapshot,
    DeliveryInspectionRecord? Inspection,
    EquipmentStatus EquipmentStatus);

public sealed record EquipmentHandoverDetailRecord(
    Guid Id,
    Guid ConsultantId,
    string ConsultantFullName,
    HandoverStatus Status,
    HandoverOperationalStatus OperationalStatus,
    HandoverDeliveryMethod? DeliveryMethod,
    DateOnly? DeliveredOn,
    string? Notes,
    Guid CreatedByUserId,
    string CreatedByDisplayNameSnapshot,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? FinalizedAtUtc,
    ConsultantSnapshotRecord? ConsultantSnapshot,
    AllocationSnapshotRecord? AllocationSnapshot,
    IReadOnlyList<EquipmentHandoverItemRecord> Items,
    EquipmentDispatchRecord? Dispatch = null,
    IReadOnlyList<CancelledDispatchRecord>? CancelledDispatches = null);

/// <param name="EquipmentStatus">The equipment's live lifecycle status — for an Active assignment, <c>ReturnPending</c> means a return has been initiated while custody is still with the Consultant. Descriptive only; custody itself is still decided by <paramref name="Status"/> alone.</param>
public sealed record EquipmentAssignmentRecord(
    Guid Id,
    Guid EquipmentId,
    string AssetTag,
    string EquipmentTypeName,
    string? Manufacturer,
    string? Model,
    Guid ConsultantId,
    string ConsultantFullName,
    AssignmentStatus Status,
    Guid? EquipmentHandoverId,
    EquipmentAssignmentOrigin Origin,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    EquipmentStatus EquipmentStatus);

public sealed record CreateHandoverData(Guid ConsultantId, HandoverDeliveryMethod DeliveryMethod, string? Notes);

public sealed record UpdateHandoverHeaderData(DateOnly? DeliveredOn, string? Notes);

public sealed record RecordInspectionData(EquipmentCondition Condition, string? Notes);

/// <summary><see cref="Search"/> matches the consultant's full name (Phase 9 refinement).</summary>
public sealed record EquipmentHandoverListFilter(Guid? ConsultantId, HandoverStatus? Status, string? Search = null);

public sealed record EquipmentAssignmentListFilter(Guid? EquipmentId, Guid? ConsultantId, AssignmentStatus? Status);
