using System.ComponentModel.DataAnnotations;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;

namespace EquipmentManagement.Api.Contracts.Handovers;

/// <summary>
/// <see cref="DeliveryMethod"/> is required for every new handover (Iteration
/// 3). It is <c>HandoverDeliveryMethod?</c> rather than the
/// plain enum so that omitting it fails validation instead of silently
/// defaulting to the enum's first member (the same tri-state reasoning as
/// <c>EndConsultantAllocationRequest.IsEndOfEngagement</c>).
/// </summary>
public sealed record CreateHandoverRequest([Required] Guid ConsultantId, [Required] HandoverDeliveryMethod? DeliveryMethod, string? Notes);

/// <summary>Chooses or changes the delivery method of a Draft handover with no dispatch yet. Also how a legacy Draft with no recorded method gets one.</summary>
public sealed record SetHandoverDeliveryMethodRequest([Required] HandoverDeliveryMethod? DeliveryMethod);

/// <summary>Everything else about a draft handover (items, inspections) is changed through its own dedicated endpoint, not this one.</summary>
public sealed record UpdateHandoverHeaderRequest(DateOnly? DeliveredOn, string? Notes);

public sealed record AddHandoverItemRequest([Required] Guid EquipmentId);

public sealed record RecordInspectionRequest([Required] EquipmentCondition Condition, string? Notes);

/// <summary>"Registar envio" — only usable on a Draft handover, before finalization (see <c>EquipmentDispatchService</c>'s remarks on why).</summary>
public sealed record RegisterDispatchRequest([Required] DateOnly ExpectedDeliveryFromDate, DateOnly? ExpectedDeliveryToDate);

public sealed record EquipmentDispatchResponse(
    Guid Id, Guid EquipmentHandoverId, DateOnly ExpectedDeliveryFromDate, DateOnly? ExpectedDeliveryToDate,
    DateTime DispatchedAtUtc, string DispatchedByDisplayNameSnapshot);

/// <summary>A dispatch attempt cancelled by "Anular envio", kept as history.</summary>
public sealed record CancelledDispatchResponse(
    Guid Id, DateTime DispatchedAtUtc, string DispatchedByDisplayNameSnapshot,
    DateTime CancelledAtUtc, string CancelledByDisplayNameSnapshot, string CancellationReason);

/// <summary>"Anular envio". The reason is mandatory; the server trims it and rejects a blank one.</summary>
public sealed record CancelDispatchRequest([Required, MaxLength(EquipmentDispatch.MaxCancellationReasonLength)] string Reason);

public sealed record ConsultantSnapshotResponse(string FullName, string Nif, string Address, string Phone, string Email);

public sealed record AllocationSnapshotResponse(string WorkforceType, string EngagementType, string RoleOrFunction, string? CompanyName);

public sealed record EquipmentSnapshotResponse(
    string AssetTag, string EquipmentTypeName, string? Manufacturer, string? Model, string? SerialNumber,
    string? Processor, string? Memory, string? Storage);

public sealed record DeliveryInspectionPhotoResponse(
    Guid Id, string OriginalFileName, string ContentType, long SizeBytes, int SortOrder, DateTime UploadedAtUtc);

public sealed record DeliveryInspectionResponse(
    Guid Id, EquipmentCondition Condition, string? Notes, string InspectedByDisplayNameSnapshot, DateTime InspectedAtUtc,
    IReadOnlyList<DeliveryInspectionPhotoResponse> Photos);

public sealed record EquipmentHandoverItemResponse(
    Guid Id, Guid EquipmentId, string AssetTag, string EquipmentTypeName, string? Manufacturer, string? Model,
    string? SerialNumber, EquipmentSnapshotResponse? EquipmentSnapshot, DeliveryInspectionResponse? Inspection,
    EquipmentStatus EquipmentStatus);

/// <summary><see cref="OperationalStatus"/> is the user-facing completion status (Iteration 1) — see <see cref="HandoverOperationalStatus"/>'s remarks. <see cref="Status"/> remains the real physical-handover lifecycle field.</summary>
public sealed record EquipmentHandoverSummaryResponse(
    Guid Id, Guid ConsultantId, string ConsultantFullName, HandoverStatus Status, HandoverOperationalStatus OperationalStatus,
    DateOnly? DeliveredOn, int ItemCount, DateTime CreatedAtUtc, DateTime? FinalizedAtUtc);

/// <param name="Dispatch">Present once "Registar envio" has been used — its mere presence is what the frontend uses to reflect the composition lock (see <c>HandoverRules.CanModifyComposition</c>); the backend enforces the lock regardless of what the client displays.</param>
/// <param name="DeliveryMethod">Null only for handovers created before Iteration 3 (never backfilled; see <c>EquipmentHandover.DeliveryMethod</c>).</param>
public sealed record EquipmentHandoverDetailResponse(
    Guid Id, Guid ConsultantId, string ConsultantFullName, HandoverStatus Status, HandoverOperationalStatus OperationalStatus,
    HandoverDeliveryMethod? DeliveryMethod, DateOnly? DeliveredOn, string? Notes,
    string CreatedByDisplayNameSnapshot, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, DateTime? FinalizedAtUtc,
    ConsultantSnapshotResponse? ConsultantSnapshot, AllocationSnapshotResponse? AllocationSnapshot,
    IReadOnlyList<EquipmentHandoverItemResponse> Items,
    EquipmentDispatchResponse? Dispatch = null,
    IReadOnlyList<CancelledDispatchResponse>? CancelledDispatches = null);

/// <param name="EquipmentStatus">The equipment's live lifecycle status. For an Active assignment, <c>ReturnPending</c> means its return is already initiated (custody is unchanged until physical receipt).</param>
public sealed record EquipmentAssignmentResponse(
    Guid Id, Guid EquipmentId, string AssetTag, string EquipmentTypeName, string? Manufacturer, string? Model,
    Guid ConsultantId, string ConsultantFullName, AssignmentStatus Status, Guid? EquipmentHandoverId,
    DateTime StartedAtUtc, DateTime? EndedAtUtc, EquipmentStatus EquipmentStatus,
    EquipmentAssignmentOrigin Origin = EquipmentAssignmentOrigin.Handover);
