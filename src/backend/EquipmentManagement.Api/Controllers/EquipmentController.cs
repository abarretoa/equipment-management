using System.Security.Claims;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Maintenance;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Equipments;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Maintenance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// The physical-asset registry (docs/PROJECT_SPEC.md, section 7), plus the
/// single write endpoint that starts maintenance on a <c>Damaged</c> equipment
/// (docs/PROJECT_SPEC.md, section 13; Phase 5 scope — this acts on
/// an Equipment resource, so it lives here rather than on
/// <see cref="MaintenanceController"/>, mirroring how a return is initiated
/// from <see cref="EquipmentAssignmentsController"/>). Retirement and starting
/// maintenance are the only lifecycle-mutating actions on this controller;
/// generic <c>PUT</c> can never change <see cref="Equipment.Status"/> or move
/// equipment to a different <see cref="EquipmentType"/> — see ADR-028 (lifecycle status) and ADR-027
/// (EquipmentType immutability).
/// </summary>
[ApiController]
[Route("api/equipment")]
public sealed class EquipmentController(
    EquipmentService equipmentService, MaintenanceService maintenanceService, EquipmentHistoryService historyService,
    UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.Equipment.Read)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] EquipmentStatus? status,
        [FromQuery] Guid? equipmentTypeId,
        [FromQuery] string? manufacturer,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await equipmentService.ListAsync(
            new EquipmentListFilter(search, status, equipmentTypeId, manufacturer), page, pageSize, cancellationToken);

        return Ok(new PagedResponse<EquipmentSummaryResponse>(
            result.Items.Select(ToSummaryResponse).ToList(), result.Page, result.PageSize, result.TotalCount));
    }

    [HttpGet("stats")]
    [Authorize(Policy = Permissions.Equipment.Read)]
    public async Task<IActionResult> GetStats([FromQuery] Guid? equipmentTypeId, CancellationToken cancellationToken)
    {
        var stats = await equipmentService.GetSummaryAsync(equipmentTypeId, cancellationToken);
        return Ok(new EquipmentStatsResponse(
            stats.TotalCount,
            stats.ByStatus.Select(s => new EquipmentStatusCountResponse(s.Status, s.Count)).ToList(),
            stats.AvailableByType.Select(t => new EquipmentTypeCountResponse(t.EquipmentTypeId, t.EquipmentTypeName, t.Count)).ToList()));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Equipment.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var equipment = await equipmentService.GetByIdAsync(id, cancellationToken);
        return equipment is null ? NotFound() : Ok(ToDetailResponse(equipment));
    }

    /// <summary>Newest-first operational timeline (Iteration 1) — see <c>EquipmentHistoryService</c>'s remarks.</summary>
    [HttpGet("{id:guid}/history")]
    [Authorize(Policy = Permissions.Equipment.Read)]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken cancellationToken)
    {
        var history = await historyService.GetHistoryAsync(id, cancellationToken);
        return history is null ? NotFound() : Ok(history.Select(ToHistoryResponse).ToList());
    }

    /// <summary>Multipart — see <see cref="CreateEquipmentRequest"/>'s remarks on the condition-photo requirement.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.Equipment.Create)]
    [ValidateCsrf]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> Create([FromForm] CreateEquipmentRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var photos = (request.Photos ?? []).Select(f => new EquipmentPhotoFile(f.OpenReadStream(), f.FileName, f.ContentType)).ToList();

        try
        {
            var result = await equipmentService.CreateAsync(
                new CreateEquipmentData(
                    request.EquipmentTypeId, request.Manufacturer, request.Model, request.SerialNumber, request.Notes,
                    ToLaptopRecord(request.Processor, request.Memory, request.Storage), request.PurchaseDate, request.InvoiceNumber,
                    request.PurchaseVendor, ToMobileRecord(request.Imei, request.MobileNumber, request.SimSerialNumber)),
                photos, actorId, actorName, cancellationToken: cancellationToken);

            if (!result.Succeeded)
            {
                return ToProblem(result.ErrorCode, result.ErrorMessage!);
            }

            return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, ToDetailResponse(result.Value));
        }
        finally
        {
            foreach (var photo in photos)
            {
                await photo.Content.DisposeAsync();
            }
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Equipment.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEquipmentRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await equipmentService.UpdateAsync(
            id,
            new UpdateEquipmentData(
                request.Manufacturer, request.Model, request.SerialNumber, request.Notes, ToRecord(request.LaptopSpecification),
                request.PurchaseDate, request.InvoiceNumber, request.PurchaseVendor, ToRecord(request.MobilePhoneSpecification)),
            actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToDetailResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    /// <summary>The legacy backfill action — only usable when the equipment has no current photo set (event B; see <c>EquipmentService.EstablishPhotosAsync</c>).</summary>
    [HttpPost("{id:guid}/photos")]
    [Authorize(Policy = Permissions.Equipment.PhotosManage)]
    [ValidateCsrf]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> EstablishPhotos(Guid id, [FromForm] EstablishEquipmentPhotosRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var photos = request.Photos.Select(f => new EquipmentPhotoFile(f.OpenReadStream(), f.FileName, f.ContentType)).ToList();

        try
        {
            var result = await equipmentService.EstablishPhotosAsync(id, photos, actorId, actorName, cancellationToken);
            return result.Succeeded ? Ok(ToDetailResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
        }
        finally
        {
            foreach (var photo in photos)
            {
                await photo.Content.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// "Alterar fotografias" — Administration-only correction of the current
    /// set outside custody. Atomically replaces it with a complete new set of 3
    /// (replaced slots uploaded, the rest copied); the previous set is kept as
    /// history. Never deletes an individual photo.
    /// </summary>
    [HttpPost("{id:guid}/current-photos/replacement")]
    [Authorize(Policy = Permissions.Equipment.PhotosReplace)]
    [ValidateCsrf]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> ReplaceCurrentPhotos(Guid id, [FromForm] ReplaceCurrentEquipmentPhotosRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var replacements = new Dictionary<int, EquipmentPhotoFile>();
        foreach (var (slot, file) in new[] { (1, request.Photo1), (2, request.Photo2), (3, request.Photo3) })
        {
            if (file is not null)
            {
                replacements[slot] = new EquipmentPhotoFile(file.OpenReadStream(), file.FileName, file.ContentType);
            }
        }

        try
        {
            var result = await equipmentService.ReplaceCurrentPhotosAsync(
                id, new ReplaceCurrentPhotosData(request.ExpectedPhotoSetId!.Value, replacements), actorId, actorName, cancellationToken);
            return result.Succeeded ? Ok(ToDetailResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
        }
        finally
        {
            foreach (var photo in replacements.Values)
            {
                await photo.Content.DisposeAsync();
            }
        }
    }

    [HttpGet("{id:guid}/photos/{photoId:guid}/content")]
    [Authorize(Policy = Permissions.Equipment.Read)]
    public async Task<IActionResult> GetPhotoContent(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await equipmentService.GetPhotoContentAsync(id, photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.Value.Content, photo.Value.ContentType, photo.Value.OriginalFileName);
    }

    [HttpPost("{id:guid}/retire")]
    [Authorize(Policy = Permissions.Equipment.Retire)]
    [ValidateCsrf]
    public async Task<IActionResult> Retire(Guid id, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await equipmentService.RetireAsync(id, actorId, actorName, cancellationToken);
        return result.Succeeded ? Ok(ToDetailResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpPost("{id:guid}/maintenance")]
    [Authorize(Policy = Permissions.EquipmentMaintenance.Manage)]
    [ValidateCsrf]
    public async Task<IActionResult> StartMaintenance(Guid id, [FromBody] StartMaintenanceRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await maintenanceService.StartAsync(id, new StartMaintenanceData(request.Notes), actorId, actorName, cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblem(result.ErrorCode, result.ErrorMessage!);
        }

        return CreatedAtAction(
            nameof(MaintenanceController.GetById), "Maintenance", new { id = result.Value!.Id }, ToMaintenanceResponse(result.Value));
    }

    private async Task<(Guid Id, string DisplayName)> GetActorAsync()
    {
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var actor = await userManager.FindByIdAsync(id.ToString());
        return (id, actor is null ? id.ToString() : $"{actor.FirstName} {actor.LastName}");
    }

    private ObjectResult ToProblem(ServiceErrorCode code, string message)
    {
        var status = code switch
        {
            ServiceErrorCode.NotFound => StatusCodes.Status404NotFound,
            ServiceErrorCode.DuplicateSerialNumber => StatusCodes.Status409Conflict,
            ServiceErrorCode.DuplicateImei => StatusCodes.Status409Conflict,
            ServiceErrorCode.InactiveReference => StatusCodes.Status409Conflict,
            ServiceErrorCode.InvalidLifecycleTransition => StatusCodes.Status409Conflict,
            ServiceErrorCode.MaintenanceNotEligible => StatusCodes.Status409Conflict,
            ServiceErrorCode.PhotoSetAlreadyExists => StatusCodes.Status409Conflict,
            ServiceErrorCode.Concurrency => StatusCodes.Status409Conflict,
            ServiceErrorCode.InvalidFile => StatusCodes.Status400BadRequest,
            ServiceErrorCode.FileTooLarge => StatusCodes.Status400BadRequest,
            ServiceErrorCode.Validation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest,
        };

        return Problem(title: message, statusCode: status);
    }

    private static MaintenanceDetailResponse ToMaintenanceResponse(MaintenanceDetailRecord m) => new(
        m.Id, m.EquipmentId, m.AssetTag, m.EquipmentTypeName, m.DamageId, m.Status, m.Notes, m.Outcome, m.SentAtUtc, m.CompletedAtUtc,
        m.CreatedAtUtc, m.UpdatedAtUtc);

    private static LaptopSpecificationRecord? ToRecord(LaptopSpecificationContract? contract) =>
        contract is null ? null : new LaptopSpecificationRecord(contract.Processor, contract.Memory, contract.Storage);

    private static LaptopSpecificationRecord? ToLaptopRecord(string? processor, string? memory, string? storage) =>
        processor is null && memory is null && storage is null ? null : new LaptopSpecificationRecord(processor ?? "", memory ?? "", storage ?? "");

    private static MobilePhoneSpecificationRecord? ToMobileRecord(string? imei, string? mobileNumber, string? simSerialNumber) =>
        imei is null && mobileNumber is null && simSerialNumber is null ? null : new MobilePhoneSpecificationRecord(imei ?? "", mobileNumber, simSerialNumber);

    private static MobilePhoneSpecificationRecord? ToRecord(MobilePhoneSpecificationContract? contract) =>
        contract is null ? null : new MobilePhoneSpecificationRecord(contract.Imei, contract.MobileNumber, contract.SimSerialNumber);

    private static LaptopSpecificationContract? ToContract(LaptopSpecificationRecord? record) =>
        record is null ? null : new LaptopSpecificationContract(record.Processor, record.Memory, record.Storage);

    private static MobilePhoneSpecificationContract? ToContract(MobilePhoneSpecificationRecord? record) =>
        record is null ? null : new MobilePhoneSpecificationContract(record.Imei, record.MobileNumber, record.SimSerialNumber);

    private static EquipmentPhotoSetResponse? ToContract(EquipmentPhotoSetRecord? record) =>
        record is null ? null : new EquipmentPhotoSetResponse(
            record.Id, record.Source, record.CreatedAtUtc,
            record.Photos.Select(p => new EquipmentPhotoResponse(p.Id, p.OriginalFileName, p.ContentType, p.SizeBytes, p.SortOrder, p.UploadedAtUtc)).ToList());

    private static EquipmentSummaryResponse ToSummaryResponse(EquipmentSummaryRecord e) => new(
        e.Id, e.AssetTag, e.EquipmentTypeName, e.Manufacturer, e.Model, e.SerialNumber, e.Status, e.CreatedAtUtc,
        e.CurrentConsultantId, e.CurrentConsultantName);

    private static EquipmentDetailResponse ToDetailResponse(EquipmentRecord e) => new(
        e.Id, e.EquipmentTypeId, e.EquipmentTypeName, e.AssetTag, e.Manufacturer, e.Model, e.SerialNumber, e.Notes,
        e.Status, e.CreatedAtUtc, e.UpdatedAtUtc, ToContract(e.LaptopSpecification), ToCustodyContract(e.CurrentCustody),
        e.PurchaseDate, e.InvoiceNumber, e.PurchaseVendor, ToPendingRetirementRequestContract(e.PendingRetirementRequest),
        ToContract(e.MobilePhoneSpecification), ToContract(e.CurrentPhotoSet), e.CurrentPhotosCorrectable);

    private static EquipmentPendingRetirementRequestResponse? ToPendingRetirementRequestContract(EquipmentPendingRetirementRequestRecord? r) =>
        r is null ? null : new EquipmentPendingRetirementRequestResponse(r.Id, r.RequestedByDisplayName, r.RequestedAtUtc, r.RequestReason);

    private static EquipmentHistoryEventResponse ToHistoryResponse(EquipmentHistoryEventRecord e) => new(
        e.EventType, e.OccurredAtUtc, e.ConsultantId, e.ConsultantName, e.CompanyName, e.Role, e.HandoverId, e.ReturnId,
        e.MaintenanceId, e.Condition, e.Outcome, e.Description, e.RetirementRequestId, e.RequestedByName, e.AssignmentOrigin);

    private static CurrentCustodyResponse? ToCustodyContract(EquipmentCurrentCustodyRecord? c) =>
        c is null ? null : new CurrentCustodyResponse(
            c.ConsultantId, c.ConsultantFullName, c.WorkforceType, c.EngagementType, c.CompanyName, c.RoleOrFunction,
            c.AssignedAtUtc, c.EquipmentHandoverId, c.Origin);
}
