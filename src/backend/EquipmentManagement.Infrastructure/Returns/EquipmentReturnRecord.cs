using EquipmentManagement.Domain.Damages;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.Returns;
using EquipmentManagement.Infrastructure.Handovers;

namespace EquipmentManagement.Infrastructure.Returns;

public sealed record ReturnInspectionPhotoRecord(
    Guid Id, string OriginalFileName, string ContentType, long SizeBytes, int SortOrder, DateTime UploadedAtUtc);

public sealed record DamagePhotoRecord(
    Guid Id, string OriginalFileName, string ContentType, long SizeBytes, int SortOrder, DateTime UploadedAtUtc);

public sealed record DamageRecord(
    Guid Id,
    string Description,
    DamageStatus Status,
    DateTime ReportedAtUtc,
    DateTime? ResolvedAtUtc,
    IReadOnlyList<DamagePhotoRecord> Photos);

public sealed record ReturnInspectionRecord(
    Guid Id,
    EquipmentCondition Condition,
    string? Notes,
    bool HasDamage,
    string? DamageDescription,
    string InspectedByDisplayNameSnapshot,
    DateTime InspectedAtUtc,
    DateTime? FinalizedAtUtc,
    EquipmentSnapshotRecord? EquipmentSnapshot,
    IReadOnlyList<ReturnInspectionPhotoRecord> Photos,
    IReadOnlyList<DamagePhotoRecord> DamagePhotos,
    DamageRecord? Damage);

/// <summary>Least-privilege list projection — no inspection detail (same discipline as <c>EquipmentHandoverSummaryRecord</c>).</summary>
public sealed record EquipmentReturnSummaryRecord(
    Guid Id,
    Guid EquipmentAssignmentId,
    Guid EquipmentId,
    string AssetTag,
    Guid ConsultantId,
    string ConsultantFullName,
    ReturnStatus Status,
    string? Reason,
    DateOnly? ExpectedReturnDate,
    DateOnly? ReceivedOn,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record EquipmentReturnDetailRecord(
    Guid Id,
    Guid EquipmentAssignmentId,
    Guid EquipmentId,
    string AssetTag,
    string EquipmentTypeName,
    Guid ConsultantId,
    string ConsultantFullName,
    ReturnStatus Status,
    string? Reason,
    DateOnly? ExpectedReturnDate,
    DateOnly? ReceivedOn,
    DateTime? ReceivedAtUtc,
    string? ReceivedByDisplayNameSnapshot,
    Guid CreatedByUserId,
    string CreatedByDisplayNameSnapshot,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancelledByDisplayNameSnapshot,
    string? CancellationReason,
    ReturnInspectionRecord? Inspection,
    Guid? ReturnGroupId = null,
    int ReturnGroupSize = 0);

public sealed record InitiateReturnData(DateOnly? ExpectedReturnDate, string? Reason);

/// <summary>The compact result of starting a manual return: the group created for it and the independent returns in it.</summary>
public sealed record StartedReturnGroupRecord(Guid GroupId, IReadOnlyList<Guid> ReturnIds);

/// <summary>One return inside a <see cref="EquipmentReturnGroupRecord"/> — enough to list it and link to its own page, where every action on it still lives.</summary>
public sealed record EquipmentReturnGroupItemRecord(
    Guid ReturnId,
    Guid EquipmentId,
    string AssetTag,
    string EquipmentTypeName,
    string? Manufacturer,
    string? Model,
    ReturnStatus Status,
    EquipmentStatus EquipmentStatus,
    DateOnly? ExpectedReturnDate,
    DateOnly? ReceivedOn);

/// <summary>
/// A return group with everything derived that a screen needs.
/// <paramref name="IsEndOfEngagement"/> is true when the group was created by
/// ending an allocation. <paramref name="ExpectedFromDate"/>/<paramref name="ExpectedToDate"/>
/// are not stored on the group: for an end of engagement they are the
/// allocation's collection window, otherwise the expected return date the
/// returns were started with.
/// </summary>
public sealed record EquipmentReturnGroupRecord(
    Guid Id,
    Guid ConsultantId,
    string ConsultantFullName,
    bool IsEndOfEngagement,
    ReturnGroupCollectionStatus CollectionStatus,
    DateOnly? ExpectedFromDate,
    DateOnly? ExpectedToDate,
    int ReceivedCount,
    string CreatedByDisplayNameSnapshot,
    DateTime CreatedAtUtc,
    IReadOnlyList<EquipmentReturnGroupItemRecord> Items);

/// <summary>
/// One row of the operational Returns list: either a group (with all of its
/// returns) or a single return that has no group because it predates
/// grouping. Exactly one of the two is set.
/// </summary>
public sealed record EquipmentReturnListRowRecord(EquipmentReturnGroupRecord? Group, EquipmentReturnSummaryRecord? Return);

/// <summary>
/// A group's own returns plus the Consultant's other returns still pending
/// physical receipt that belong elsewhere (started before this group
/// existed, or separately). They are shown for context only and are never
/// part of the group.
/// </summary>
public sealed record EquipmentReturnGroupDetailRecord(
    EquipmentReturnGroupRecord Group, IReadOnlyList<EquipmentReturnSummaryRecord> OtherPendingReturns);

/// <summary>
/// <see cref="Status"/> filters each row by the single status it is in as a
/// whole (<c>ReturnRules.DeriveListFilterStatus</c>): a group by its derived
/// collection state, an ungrouped return by its own status — never by whether
/// any one of a group's returns matches. <see cref="Search"/> matches the
/// consultant's full name or any of the row's asset tags.
/// </summary>
public sealed record EquipmentReturnOverviewFilter(ReturnStatus? Status, string? Search);

public sealed record ReceiveReturnData(DateOnly ReceivedOn, Guid ReceivedByUserId);

/// <summary>One condition-refresh photo submitted with physical receipt — for Laptop/MobilePhone equipment, exactly 3 are required (<c>ReturnRules.RequiredReceivePhotoCount</c>).</summary>
public sealed record ReceivedPhotoFile(Stream Content, string OriginalFileName, string DeclaredContentType);

public sealed record CancelReturnData(string? Reason);

public sealed record UpdateReturnInspectionData(EquipmentCondition Condition, string? Notes, bool HasDamage, string? DamageDescription);

/// <summary><see cref="Search"/> matches the consultant's full name or the equipment's asset tag (Phase 9 refinement).</summary>
public sealed record EquipmentReturnListFilter(Guid? ConsultantId, Guid? EquipmentId, ReturnStatus? Status, string? Search = null);
