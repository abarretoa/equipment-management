using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.Maintenance;

namespace EquipmentManagement.Infrastructure.Equipments;

public sealed record LaptopSpecificationRecord(string Processor, string Memory, string Storage);

public sealed record MobilePhoneSpecificationRecord(string Imei, string? MobileNumber, string? SimSerialNumber);

public sealed record EquipmentPhotoRecord(Guid Id, string OriginalFileName, string ContentType, long SizeBytes, int SortOrder, DateTime UploadedAtUtc);

/// <summary>The equipment's current condition photo set (exactly 3 photos) — null when none has ever been established (legacy Laptop/MobilePhone equipment; see ADR-079).</summary>
public sealed record EquipmentPhotoSetRecord(Guid Id, PhotoSetSource Source, DateTime CreatedAtUtc, IReadOnlyList<EquipmentPhotoRecord> Photos);

/// <summary>
/// "Who currently has this equipment" — resolved exclusively from the
/// Active-status <see cref="EquipmentManagement.Domain.Handovers.EquipmentAssignment"/>
/// row for this equipment (never from <see cref="EquipmentStatus"/> or
/// historical Handover/Return records). WorkforceType/EngagementType/
/// CompanyName/RoleOrFunction come from the consultant's current
/// ConsultantAllocation, if one still exists, and may legitimately be null.
/// </summary>
public sealed record EquipmentCurrentCustodyRecord(
    Guid ConsultantId,
    string ConsultantFullName,
    WorkforceType? WorkforceType,
    EngagementType? EngagementType,
    string? CompanyName,
    string? RoleOrFunction,
    DateTime AssignedAtUtc,
    Guid? EquipmentHandoverId,
    EquipmentAssignmentOrigin Origin);

/// <summary>Full detail projection — never the EF entity itself (docs/PROJECT_SPEC.md, section 24).</summary>
public sealed record EquipmentRecord(
    Guid Id,
    Guid EquipmentTypeId,
    string EquipmentTypeName,
    string AssetTag,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? Notes,
    EquipmentStatus Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    LaptopSpecificationRecord? LaptopSpecification,
    EquipmentCurrentCustodyRecord? CurrentCustody,
    DateOnly? PurchaseDate,
    string? InvoiceNumber,
    string? PurchaseVendor,
    EquipmentPendingRetirementRequestRecord? PendingRetirementRequest = null,
    MobilePhoneSpecificationRecord? MobilePhoneSpecification = null,
    EquipmentPhotoSetRecord? CurrentPhotoSet = null,
    bool CurrentPhotosCorrectable = false);

/// <summary>
/// Least-privilege list projection (Phase 3 scope) — no Notes, no LaptopSpecification (detail-level only).
/// CurrentConsultantId/Name are resolved from the Active-status
/// EquipmentAssignment for this equipment, the same source of truth as
/// <see cref="EquipmentCurrentCustodyRecord"/> — never from Status alone.
/// </summary>
public sealed record EquipmentSummaryRecord(
    Guid Id,
    string AssetTag,
    string EquipmentTypeName,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    EquipmentStatus Status,
    DateTime CreatedAtUtc,
    Guid? CurrentConsultantId,
    string? CurrentConsultantName);

public sealed record CreateEquipmentData(
    Guid EquipmentTypeId,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? Notes,
    LaptopSpecificationRecord? LaptopSpecification,
    DateOnly? PurchaseDate = null,
    string? InvoiceNumber = null,
    string? PurchaseVendor = null,
    MobilePhoneSpecificationRecord? MobilePhoneSpecification = null);

/// <summary>"Alterar fotografias": the replacement files keyed by slot (1-3); every slot not listed keeps its current photo. <paramref name="ExpectedPhotoSetId"/> is the current set the user was looking at — a different current set means a concurrent change.</summary>
public sealed record ReplaceCurrentPhotosData(Guid ExpectedPhotoSetId, IReadOnlyDictionary<int, EquipmentPhotoFile> ReplacementsBySlot);

/// <summary>One condition photo submitted with equipment creation or the legacy backfill action — for Laptop/MobilePhone, exactly 3 are required (<c>EquipmentRules.RequiresConditionPhotos</c>).</summary>
public sealed record EquipmentPhotoFile(Stream Content, string OriginalFileName, string DeclaredContentType);

/// <summary>
/// <see cref="EquipmentTypeId"/>, AssetTag, and Status are deliberately absent
/// — all immutable/lifecycle-controlled after creation (Phase 3 scope). See ADR-027. Purchase fields are plain editable metadata (Iteration 1) — omitting/blanking them on update clears the stored value.
/// </summary>
public sealed record UpdateEquipmentData(
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? Notes,
    LaptopSpecificationRecord? LaptopSpecification,
    DateOnly? PurchaseDate = null,
    string? InvoiceNumber = null,
    string? PurchaseVendor = null,
    MobilePhoneSpecificationRecord? MobilePhoneSpecification = null);

public sealed record EquipmentListFilter(string? Search, EquipmentStatus? Status, Guid? EquipmentTypeId, string? Manufacturer);

/// <summary>
/// One operational timeline entry (Iteration 1). Consultant
/// name/company/role come from the originating Handover's historical
/// snapshots (never a live Consultant/ConsultantAllocation lookup), so the
/// timeline reads truthfully even after the consultant later changes company
/// or role. Deliberately excludes NIF/address/phone/personal email — those
/// never leave the Handover snapshot itself. <see cref="Condition"/>,
/// <see cref="Outcome"/> and <see cref="Description"/> stay raw (enum/free
/// text), matching every other status field in this API — the frontend maps
/// them to pt-PT labels via <c>shared/status/statusMaps.ts</c>, exactly like
/// <see cref="EquipmentStatus"/>/<see cref="Returns.ReturnStatus"/> elsewhere.
/// </summary>
public sealed record EquipmentHistoryEventRecord(
    EquipmentHistoryEventType EventType,
    DateTime OccurredAtUtc,
    Guid? ConsultantId,
    string? ConsultantName,
    string? CompanyName,
    string? Role,
    Guid? HandoverId,
    Guid? ReturnId,
    Guid? MaintenanceId,
    EquipmentCondition? Condition,
    MaintenanceOutcome? Outcome,
    string? Description,
    Guid? RetirementRequestId = null,
    string? RequestedByName = null,
    EquipmentAssignmentOrigin? AssignmentOrigin = null);

public sealed record EquipmentStatusCountRecord(EquipmentStatus Status, int Count);

public sealed record EquipmentTypeCountRecord(Guid EquipmentTypeId, string EquipmentTypeName, int Count);

/// <summary>
/// Single-query aggregate for the frontend inventory dashboard (Phase 9
/// refinement) — avoids the alternative of one list call per status/
/// type just to read <c>totalCount</c>. Read-only, no lifecycle implication.
/// </summary>
public sealed record EquipmentSummaryStats(
    int TotalCount, IReadOnlyList<EquipmentStatusCountRecord> ByStatus, IReadOnlyList<EquipmentTypeCountRecord> AvailableByType);
