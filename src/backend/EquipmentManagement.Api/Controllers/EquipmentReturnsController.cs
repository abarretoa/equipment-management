using System.Security.Claims;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.Returns;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Domain.Returns;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Handovers;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Returns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// The return/return-inspection/damage workflow (docs/PROJECT_SPEC.md, section
/// 12; Phase 5 scope). A return is initiated from
/// <see cref="EquipmentAssignmentsController.Return"/>, not here — everything
/// in this controller acts on an already-initiated <see cref="EquipmentReturn"/>.
/// <see cref="Receive"/>, <see cref="Cancel"/> and <see cref="FinalizeInspection"/>
/// are the lifecycle-mutating actions (gated by <c>Equipment.Return</c>,
/// mirroring <c>Equipment.Assign</c>'s role for handovers); everything else
/// that only edits draft evidence is gated by <c>EquipmentReturn.Update</c>.
/// </summary>
[ApiController]
[Route("api/equipment-returns")]
public sealed class EquipmentReturnsController(
    EquipmentReturnService returnService, EquipmentReturnGroupQueryService groupQueryService, UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.EquipmentReturn.Read)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? consultantId, [FromQuery] Guid? equipmentId, [FromQuery] ReturnStatus? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await returnService.ListAsync(new EquipmentReturnListFilter(consultantId, equipmentId, status, search), page, pageSize, cancellationToken);
        return Ok(new PagedResponse<EquipmentReturnSummaryResponse>(
            result.Items.Select(ToSummaryResponse).ToList(), result.Page, result.PageSize, result.TotalCount));
    }

    /// <summary>
    /// The operational list the Returns page shows: one row per return group
    /// (one initiation event, however many items) plus one row per return that
    /// has no group. <see cref="List"/> stays the flat per-return list that
    /// counts are based on.
    /// </summary>
    [HttpGet("overview")]
    [Authorize(Policy = Permissions.EquipmentReturn.Read)]
    public async Task<IActionResult> Overview(
        [FromQuery] ReturnStatus? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await groupQueryService.ListOverviewAsync(new EquipmentReturnOverviewFilter(status, search), page, pageSize, cancellationToken);
        return Ok(new PagedResponse<EquipmentReturnListRowResponse>(
            result.Items.Select(row => new EquipmentReturnListRowResponse(
                row.Group is null ? null : ToGroupResponse(row.Group),
                row.Return is null ? null : ToSummaryResponse(row.Return))).ToList(),
            result.Page, result.PageSize, result.TotalCount));
    }

    [HttpGet("groups/{groupId:guid}")]
    [Authorize(Policy = Permissions.EquipmentReturn.Read)]
    public async Task<IActionResult> GetGroupById(Guid groupId, CancellationToken cancellationToken)
    {
        var detail = await groupQueryService.GetByIdAsync(groupId, cancellationToken);
        return detail is null
            ? NotFound()
            : Ok(new EquipmentReturnGroupDetailResponse(
                ToGroupResponse(detail.Group), detail.OtherPendingReturns.Select(ToSummaryResponse).ToList()));
    }

    /// <summary>
    /// Starts a manual return for one or more items held by one Consultant —
    /// the Returns-area entry point. Gated by the same <c>Equipment.Return</c>
    /// permission as starting a return from an equipment's page
    /// (<see cref="EquipmentAssignmentsController.Return"/>), and running the
    /// same operation.
    /// </summary>
    [HttpPost("groups")]
    [Authorize(Policy = Permissions.Equipment.Return)]
    [ValidateCsrf]
    public async Task<IActionResult> StartGroup([FromBody] StartReturnGroupRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await returnService.InitiateGroupAsync(
            request.ConsultantId!.Value, request.EquipmentAssignmentIds!,
            new InitiateReturnData(request.ExpectedReturnDate, request.Reason), actorId, actorName, cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblem(result.ErrorCode, result.ErrorMessage!);
        }

        return CreatedAtAction(
            nameof(GetGroupById), new { groupId = result.Value!.GroupId },
            new StartReturnGroupResponse(result.Value.GroupId, result.Value.ReturnIds.Count, result.Value.ReturnIds));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.EquipmentReturn.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var equipmentReturn = await returnService.GetByIdAsync(id, cancellationToken);
        return equipmentReturn is null ? NotFound() : Ok(ToDetailResponse(equipmentReturn));
    }

    /// <summary>Multipart — see <see cref="ReceiveReturnRequest"/>'s remarks on the condition-photo requirement.</summary>
    [HttpPost("{id:guid}/receive")]
    [Authorize(Policy = Permissions.Equipment.Return)]
    [ValidateCsrf]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> Receive(Guid id, [FromForm] ReceiveReturnRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var photos = (request.Photos ?? []).Select(f => new ReceivedPhotoFile(f.OpenReadStream(), f.FileName, f.ContentType)).ToList();

        try
        {
            var result = await returnService.ReceiveAsync(
                id, new ReceiveReturnData(request.ReceivedOn, request.ReceivedByUserId), photos, actorId, actorName, cancellationToken);

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
    /// Calls off a Pending return before physical receipt (Phase 5
    /// cancellation amendment) — reverses only the pending-return workflow,
    /// never custody: the assignment stays Active and Equipment goes back to
    /// Assigned. Once physical receipt has occurred, this is impossible (409).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Permissions.Equipment.Return)]
    [ValidateCsrf]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelReturnRequest? request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await returnService.CancelAsync(id, new CancelReturnData(request?.Reason), actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToDetailResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpPut("{id:guid}/inspection")]
    [Authorize(Policy = Permissions.EquipmentReturn.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> RecordInspection(Guid id, [FromBody] UpdateReturnInspectionRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await returnService.RecordInspectionAsync(
            id, new UpdateReturnInspectionData(request.Condition, request.Notes, request.HasDamage, request.DamageDescription),
            actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToInspectionResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    /// <summary>Multipart upload — real image signature is validated server-side, never the declared Content-Type alone (see <c>EquipmentReturnService.UploadInspectionPhotoAsync</c>).</summary>
    [HttpPost("{id:guid}/inspection/photos")]
    [Authorize(Policy = Permissions.EquipmentReturn.Update)]
    [ValidateCsrf]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadInspectionPhoto(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return Problem(title: "No file was uploaded.", statusCode: StatusCodes.Status400BadRequest);
        }

        var (actorId, actorName) = await GetActorAsync();
        await using var stream = file.OpenReadStream();
        var result = await returnService.UploadInspectionPhotoAsync(id, stream, file.FileName, file.ContentType, actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToPhotoResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpDelete("{id:guid}/inspection/photos/{photoId:guid}")]
    [Authorize(Policy = Permissions.EquipmentReturn.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> DeleteInspectionPhoto(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await returnService.DeleteInspectionPhotoAsync(id, photoId, actorId, actorName, cancellationToken);

        return result.Succeeded ? NoContent() : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpGet("{id:guid}/inspection/photos/{photoId:guid}/content")]
    [Authorize(Policy = Permissions.EquipmentReturn.Read)]
    public async Task<IActionResult> GetInspectionPhotoContent(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await returnService.GetInspectionPhotoContentAsync(id, photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.Value.Content, photo.Value.ContentType, photo.Value.OriginalFileName);
    }

    [HttpPost("{id:guid}/damage/photos")]
    [Authorize(Policy = Permissions.EquipmentReturn.Update)]
    [ValidateCsrf]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadDamagePhoto(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return Problem(title: "No file was uploaded.", statusCode: StatusCodes.Status400BadRequest);
        }

        var (actorId, actorName) = await GetActorAsync();
        await using var stream = file.OpenReadStream();
        var result = await returnService.UploadDamagePhotoAsync(id, stream, file.FileName, file.ContentType, actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToDamagePhotoResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpDelete("{id:guid}/damage/photos/{photoId:guid}")]
    [Authorize(Policy = Permissions.EquipmentReturn.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> DeleteDamagePhoto(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await returnService.DeleteDamagePhotoAsync(id, photoId, actorId, actorName, cancellationToken);

        return result.Succeeded ? NoContent() : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpGet("{id:guid}/damage/photos/{photoId:guid}/content")]
    [Authorize(Policy = Permissions.EquipmentReturn.Read)]
    public async Task<IActionResult> GetDamagePhotoContent(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await returnService.GetDamagePhotoContentAsync(id, photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.Value.Content, photo.Value.ContentType, photo.Value.OriginalFileName);
    }

    [HttpPost("{id:guid}/inspection/finalize")]
    [Authorize(Policy = Permissions.Equipment.Return)]
    [ValidateCsrf]
    public async Task<IActionResult> FinalizeInspection(Guid id, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await returnService.FinalizeInspectionAsync(id, actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToDetailResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
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
            ServiceErrorCode.InactiveReference => StatusCodes.Status409Conflict,
            ServiceErrorCode.InvalidLifecycleTransition => StatusCodes.Status409Conflict,
            ServiceErrorCode.ReturnAlreadyInProgress => StatusCodes.Status409Conflict,
            ServiceErrorCode.ReturnNotPending => StatusCodes.Status409Conflict,
            ServiceErrorCode.ReturnNotReceived => StatusCodes.Status409Conflict,
            ServiceErrorCode.InspectionMissing => StatusCodes.Status409Conflict,
            ServiceErrorCode.InsufficientPhotos => StatusCodes.Status409Conflict,
            ServiceErrorCode.DamageEvidenceRequired => StatusCodes.Status409Conflict,
            ServiceErrorCode.Concurrency => StatusCodes.Status409Conflict,
            ServiceErrorCode.InvalidFile => StatusCodes.Status400BadRequest,
            ServiceErrorCode.FileTooLarge => StatusCodes.Status400BadRequest,
            ServiceErrorCode.Validation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest,
        };

        return Problem(title: message, statusCode: status);
    }

    private static EquipmentReturnSummaryResponse ToSummaryResponse(EquipmentReturnSummaryRecord r) => new(
        r.Id, r.EquipmentAssignmentId, r.EquipmentId, r.AssetTag, r.ConsultantId, r.ConsultantFullName, r.Status, r.Reason,
        r.ExpectedReturnDate, r.ReceivedOn, r.CreatedAtUtc, r.CompletedAtUtc);

    private static ReturnInspectionPhotoResponse ToPhotoResponse(ReturnInspectionPhotoRecord p) => new(
        p.Id, p.OriginalFileName, p.ContentType, p.SizeBytes, p.SortOrder, p.UploadedAtUtc);

    private static DamagePhotoResponse ToDamagePhotoResponse(DamagePhotoRecord p) => new(
        p.Id, p.OriginalFileName, p.ContentType, p.SizeBytes, p.SortOrder, p.UploadedAtUtc);

    private static DamageResponse ToDamageResponse(DamageRecord d) => new(
        d.Id, d.Description, d.Status, d.ReportedAtUtc, d.ResolvedAtUtc, d.Photos.Select(ToDamagePhotoResponse).ToList());

    private static EquipmentSnapshotResponse ToSnapshotResponse(EquipmentSnapshotRecord s) => new(
        s.AssetTag, s.EquipmentTypeName, s.Manufacturer, s.Model, s.SerialNumber, s.Processor, s.Memory, s.Storage);

    private static ReturnInspectionResponse ToInspectionResponse(ReturnInspectionRecord i) => new(
        i.Id, i.Condition, i.Notes, i.HasDamage, i.DamageDescription, i.InspectedByDisplayNameSnapshot, i.InspectedAtUtc, i.FinalizedAtUtc,
        i.EquipmentSnapshot is null ? null : ToSnapshotResponse(i.EquipmentSnapshot),
        i.Photos.Select(ToPhotoResponse).ToList(), i.DamagePhotos.Select(ToDamagePhotoResponse).ToList(),
        i.Damage is null ? null : ToDamageResponse(i.Damage));

    private static EquipmentReturnDetailResponse ToDetailResponse(EquipmentReturnDetailRecord r) => new(
        r.Id, r.EquipmentAssignmentId, r.EquipmentId, r.AssetTag, r.EquipmentTypeName, r.ConsultantId, r.ConsultantFullName, r.Status,
        r.Reason, r.ExpectedReturnDate, r.ReceivedOn, r.ReceivedAtUtc, r.ReceivedByDisplayNameSnapshot, r.CreatedByDisplayNameSnapshot,
        r.CreatedAtUtc, r.UpdatedAtUtc, r.CompletedAtUtc, r.CancelledAtUtc, r.CancelledByDisplayNameSnapshot, r.CancellationReason,
        r.Inspection is null ? null : ToInspectionResponse(r.Inspection), r.ReturnGroupId, r.ReturnGroupSize);

    private static EquipmentReturnGroupResponse ToGroupResponse(EquipmentReturnGroupRecord g) => new(
        g.Id, g.ConsultantId, g.ConsultantFullName, g.IsEndOfEngagement, g.CollectionStatus, g.ExpectedFromDate, g.ExpectedToDate,
        g.ReceivedCount, g.CreatedByDisplayNameSnapshot, g.CreatedAtUtc,
        g.Items.Select(i => new EquipmentReturnGroupItemResponse(
            i.ReturnId, i.EquipmentId, i.AssetTag, i.EquipmentTypeName, i.Manufacturer, i.Model, i.Status, i.EquipmentStatus,
            i.ExpectedReturnDate, i.ReceivedOn)).ToList());
}
