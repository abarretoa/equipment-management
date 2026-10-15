using System.ComponentModel.DataAnnotations;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.Maintenance;
using Microsoft.AspNetCore.Http;

namespace EquipmentManagement.Api.Contracts.Equipments;

public sealed record LaptopSpecificationContract(
    [Required, MaxLength(200)] string Processor,
    [Required, MaxLength(100)] string Memory,
    [Required, MaxLength(200)] string Storage);

/// <summary>Domain persistence stays carrier-neutral (<see cref="MobileNumber"/>) even when a client profile labels it after its mobile operator (e.g. "Cartão &lt;operator&gt;").</summary>
public sealed record MobilePhoneSpecificationContract(
    [Required, MaxLength(32)] string Imei,
    [MaxLength(30)] string? MobileNumber,
    [MaxLength(30)] string? SimSerialNumber);

/// <summary>
/// Multipart — flat fields (no nested Laptop/MobilePhone specification object)
/// so this binds cleanly via <c>[FromForm]</c> alongside <see cref="Photos"/>.
/// Deliberately excludes Status/AssetTag (system-managed) and any assignment/
/// consultant field. Laptop fields are required exactly when the referenced
/// type's Kind is Laptop, Mobile Phone fields exactly when it's MobilePhone,
/// and <see cref="Photos"/> (exactly 3) exactly when the Kind requires
/// condition photos — all validated server-side in <c>EquipmentService</c>,
/// not by this contract's shape alone (the type isn't known until the request
/// is processed).
/// </summary>
public sealed record CreateEquipmentRequest(
    [Required] Guid EquipmentTypeId,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? Notes,
    string? Processor,
    string? Memory,
    string? Storage,
    string? Imei,
    string? MobileNumber,
    string? SimSerialNumber,
    List<IFormFile>? Photos,
    DateOnly? PurchaseDate = null,
    [MaxLength(100)] string? InvoiceNumber = null,
    [MaxLength(200)] string? PurchaseVendor = null);

/// <summary>EquipmentTypeId, AssetTag and Status are immutable/lifecycle-controlled — see <c>UpdateEquipmentData</c>'s remarks. No photo field exists here at all — ordinary Equipment Edit can never create/replace condition photos. Purchase fields (Iteration 1) are plain editable metadata — no format restriction beyond length, since suppliers issue invoice numbers in inconsistent shapes.</summary>
public sealed record UpdateEquipmentRequest(
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? Notes,
    LaptopSpecificationContract? LaptopSpecification,
    MobilePhoneSpecificationContract? MobilePhoneSpecification = null,
    DateOnly? PurchaseDate = null,
    [MaxLength(100)] string? InvoiceNumber = null,
    [MaxLength(200)] string? PurchaseVendor = null);

/// <summary>Exactly 3 photos for a photo-required equipment's first current set — the legacy backfill action.</summary>
public sealed record EstablishEquipmentPhotosRequest([Required] List<IFormFile> Photos);

/// <summary>"Alterar fotografias": any of Photo1-3 replaces that slot; omitted slots keep their current photo. The result is always a complete new set of 3.</summary>
public sealed record ReplaceCurrentEquipmentPhotosRequest([Required] Guid? ExpectedPhotoSetId, IFormFile? Photo1, IFormFile? Photo2, IFormFile? Photo3);

public sealed record EquipmentPhotoResponse(Guid Id, string OriginalFileName, string ContentType, long SizeBytes, int SortOrder, DateTime UploadedAtUtc);

public sealed record EquipmentPhotoSetResponse(Guid Id, PhotoSetSource Source, DateTime CreatedAtUtc, IReadOnlyList<EquipmentPhotoResponse> Photos);

public sealed record EquipmentSummaryResponse(
    Guid Id, string AssetTag, string EquipmentTypeName, string? Manufacturer, string? Model,
    string? SerialNumber, EquipmentStatus Status, DateTime CreatedAtUtc,
    Guid? CurrentConsultantId, string? CurrentConsultantName);

/// <summary>
/// Current custody — resolved exclusively from the Active-status
/// EquipmentAssignment for this equipment, never from Equipment.Status.
/// Regime/Empresa/Função reflect the consultant's current allocation, if one
/// still exists, and may legitimately be null. <see cref="EquipmentHandoverId"/>
/// lets the UI offer a secondary "Ver entrega" link where that page is reachable;
/// it is null for custody established by the Current Inventory import
/// (<see cref="Origin"/>), which has no delivery behind it.
/// </summary>
public sealed record CurrentCustodyResponse(
    Guid ConsultantId, string ConsultantFullName, WorkforceType? WorkforceType, EngagementType? EngagementType,
    string? CompanyName, string? RoleOrFunction, DateTime AssignedAtUtc, Guid? EquipmentHandoverId,
    EquipmentAssignmentOrigin Origin = EquipmentAssignmentOrigin.Handover);

/// <summary>Present only while the equipment has a currently-Pending retirement request (see the retirement-request workflow) — deliberately minimal, requester display name only.</summary>
public sealed record EquipmentPendingRetirementRequestResponse(
    Guid Id, string RequestedByDisplayName, DateTime RequestedAtUtc, string RequestReason);

public sealed record EquipmentDetailResponse(
    Guid Id, Guid EquipmentTypeId, string EquipmentTypeName, string AssetTag, string? Manufacturer, string? Model,
    string? SerialNumber, string? Notes, EquipmentStatus Status, DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    LaptopSpecificationContract? LaptopSpecification, CurrentCustodyResponse? CurrentCustody,
    DateOnly? PurchaseDate, string? InvoiceNumber, string? PurchaseVendor,
    EquipmentPendingRetirementRequestResponse? PendingRetirementRequest = null,
    MobilePhoneSpecificationContract? MobilePhoneSpecification = null,
    EquipmentPhotoSetResponse? CurrentPhotoSet = null,
    bool CurrentPhotosCorrectable = false);

public sealed record EquipmentStatusCountResponse(EquipmentStatus Status, int Count);

public sealed record EquipmentTypeCountResponse(Guid EquipmentTypeId, string EquipmentTypeName, int Count);

public sealed record EquipmentStatsResponse(
    int TotalCount, IReadOnlyList<EquipmentStatusCountResponse> ByStatus, IReadOnlyList<EquipmentTypeCountResponse> AvailableByType);

/// <summary>One operational timeline entry for Equipment Detail's "Histórico do equipamento" (Iteration 1) — see <c>EquipmentHistoryEventRecord</c>'s remarks for the exact truthfulness/PII rules.</summary>
public sealed record EquipmentHistoryEventResponse(
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
