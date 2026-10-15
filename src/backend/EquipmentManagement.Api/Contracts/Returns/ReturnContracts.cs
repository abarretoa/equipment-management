using System.ComponentModel.DataAnnotations;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Domain.Damages;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.Returns;
using Microsoft.AspNetCore.Http;

namespace EquipmentManagement.Api.Contracts.Returns;

public sealed record InitiateReturnRequest(DateOnly? ExpectedReturnDate, string? Reason);

/// <summary>
/// Multipart — <see cref="Photos"/> carries the exactly-3 condition photos
/// required at physical receipt for Laptop/MobilePhone equipment (empty for
/// every other equipment kind; see <c>EquipmentRules.RequiresConditionPhotos</c>).
/// </summary>
public sealed record ReceiveReturnRequest([Required] DateOnly ReceivedOn, [Required] Guid ReceivedByUserId, List<IFormFile>? Photos);

public sealed record CancelReturnRequest(string? Reason);

public sealed record UpdateReturnInspectionRequest(
    [Required] EquipmentCondition Condition, string? Notes, bool HasDamage, string? DamageDescription);

public sealed record ReturnInspectionPhotoResponse(
    Guid Id, string OriginalFileName, string ContentType, long SizeBytes, int SortOrder, DateTime UploadedAtUtc);

public sealed record DamagePhotoResponse(
    Guid Id, string OriginalFileName, string ContentType, long SizeBytes, int SortOrder, DateTime UploadedAtUtc);

public sealed record DamageResponse(
    Guid Id, string Description, DamageStatus Status, DateTime ReportedAtUtc, DateTime? ResolvedAtUtc,
    IReadOnlyList<DamagePhotoResponse> Photos);

public sealed record ReturnInspectionResponse(
    Guid Id, EquipmentCondition Condition, string? Notes, bool HasDamage, string? DamageDescription,
    string InspectedByDisplayNameSnapshot, DateTime InspectedAtUtc, DateTime? FinalizedAtUtc,
    EquipmentSnapshotResponse? EquipmentSnapshot, IReadOnlyList<ReturnInspectionPhotoResponse> Photos,
    IReadOnlyList<DamagePhotoResponse> DamagePhotos, DamageResponse? Damage);

public sealed record EquipmentReturnSummaryResponse(
    Guid Id, Guid EquipmentAssignmentId, Guid EquipmentId, string AssetTag, Guid ConsultantId, string ConsultantFullName,
    ReturnStatus Status, string? Reason, DateOnly? ExpectedReturnDate, DateOnly? ReceivedOn, DateTime CreatedAtUtc, DateTime? CompletedAtUtc);

public sealed record EquipmentReturnDetailResponse(
    Guid Id, Guid EquipmentAssignmentId, Guid EquipmentId, string AssetTag, string EquipmentTypeName, Guid ConsultantId,
    string ConsultantFullName, ReturnStatus Status, string? Reason, DateOnly? ExpectedReturnDate, DateOnly? ReceivedOn,
    DateTime? ReceivedAtUtc, string? ReceivedByDisplayNameSnapshot, string CreatedByDisplayNameSnapshot, DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc, DateTime? CompletedAtUtc, DateTime? CancelledAtUtc, string? CancelledByDisplayNameSnapshot,
    string? CancellationReason, ReturnInspectionResponse? Inspection,
    Guid? ReturnGroupId = null, int ReturnGroupSize = 0);

/// <summary>
/// Starts a manual return for one or more pieces of equipment currently held
/// by one Consultant (Iteration 4). Each assignment gets its own
/// independent return; the request is all-or-nothing. It never touches the
/// Consultant's allocation.
/// </summary>
public sealed record StartReturnGroupRequest(
    [Required] Guid? ConsultantId,
    [Required, MinLength(1)] IReadOnlyList<Guid>? EquipmentAssignmentIds,
    DateOnly? ExpectedReturnDate,
    [MaxLength(500)] string? Reason);

public sealed record StartReturnGroupResponse(Guid ReturnGroupId, int ReturnsCreatedCount, IReadOnlyList<Guid> ReturnIds);

public sealed record EquipmentReturnGroupItemResponse(
    Guid ReturnId, Guid EquipmentId, string AssetTag, string EquipmentTypeName, string? Manufacturer, string? Model,
    ReturnStatus Status, EquipmentStatus EquipmentStatus, DateOnly? ExpectedReturnDate, DateOnly? ReceivedOn);

/// <summary>
/// One return-initiation event shown as one collection. Everything except the
/// identity fields is derived: <see cref="CollectionStatus"/> from the
/// returns, and the expected dates from the allocation's collection window
/// (end of engagement) or from the returns' own expected date (manual).
/// </summary>
public sealed record EquipmentReturnGroupResponse(
    Guid Id, Guid ConsultantId, string ConsultantFullName, bool IsEndOfEngagement, ReturnGroupCollectionStatus CollectionStatus,
    DateOnly? ExpectedFromDate, DateOnly? ExpectedToDate, int ReceivedCount, string CreatedByDisplayNameSnapshot, DateTime CreatedAtUtc,
    IReadOnlyList<EquipmentReturnGroupItemResponse> Items);

/// <summary>One row of the operational Returns list: exactly one of <see cref="Group"/> (a grouped collection) or <see cref="Return"/> (a return with no group, created before grouping existed) is set.</summary>
public sealed record EquipmentReturnListRowResponse(EquipmentReturnGroupResponse? Group, EquipmentReturnSummaryResponse? Return);

/// <param name="OtherPendingReturns">The same Consultant's other returns still awaiting physical receipt that are not part of this group — context only.</param>
public sealed record EquipmentReturnGroupDetailResponse(
    EquipmentReturnGroupResponse Group, IReadOnlyList<EquipmentReturnSummaryResponse> OtherPendingReturns);
