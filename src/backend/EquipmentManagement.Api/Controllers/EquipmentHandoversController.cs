using System.Security.Claims;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.ResponsibilityTerms;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Handovers;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.ResponsibilityTerms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// The delivery/handover/assignment workflow (docs/PROJECT_SPEC.md, sections
/// 9-11, 19-21). A draft can be freely edited (items, inspections, photos);
/// <see cref="Finalize"/> is the single one-way transition that creates
/// <see cref="Domain.Handovers.EquipmentAssignment"/> rows and moves equipment
/// to <see cref="Domain.Equipments.EquipmentStatus.Assigned"/> — see
/// <c>EquipmentHandoverService.FinalizeAsync</c>.
/// </summary>
[ApiController]
[Route("api/equipment-handovers")]
public sealed class EquipmentHandoversController(
    EquipmentHandoverService handoverService,
    EquipmentDispatchService dispatchService,
    ResponsibilityTermService termService,
    ResponsibilityTermDeliveryService deliveryService,
    ResponsibilityTermSignedArtifactService signedArtifactService,
    UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.EquipmentHandover.Read)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? consultantId, [FromQuery] HandoverStatus? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await handoverService.ListAsync(new EquipmentHandoverListFilter(consultantId, status, search), page, pageSize, cancellationToken);
        return Ok(new PagedResponse<EquipmentHandoverSummaryResponse>(
            result.Items.Select(ToSummaryResponse).ToList(), result.Page, result.PageSize, result.TotalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.EquipmentHandover.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var handover = await handoverService.GetByIdAsync(id, cancellationToken);
        return handover is null ? NotFound() : Ok(ToDetailResponse(handover));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.EquipmentHandover.Create)]
    [ValidateCsrf]
    public async Task<IActionResult> Create([FromBody] CreateHandoverRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await handoverService.CreateAsync(
            new CreateHandoverData(request.ConsultantId, request.DeliveryMethod!.Value, request.Notes), actorId, actorName, cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblem(result.ErrorCode, result.ErrorMessage!);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, ToDetailResponse(result.Value));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.EquipmentHandover.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> UpdateHeader(Guid id, [FromBody] UpdateHandoverHeaderRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await handoverService.UpdateHeaderAsync(
            id, new UpdateHandoverHeaderData(request.DeliveredOn, request.Notes), actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToDetailResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    /// <summary>
    /// Sets "Método de entrega" on a Draft handover (Iteration 3).
    /// Rejected once a dispatch exists: the method is locked together with the
    /// rest of the shipment composition (<c>HandoverRules.CanChangeDeliveryMethod</c>).
    /// </summary>
    [HttpPut("{id:guid}/delivery-method")]
    [Authorize(Policy = Permissions.EquipmentHandover.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> SetDeliveryMethod(Guid id, [FromBody] SetHandoverDeliveryMethodRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await handoverService.SetDeliveryMethodAsync(id, request.DeliveryMethod!.Value, actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToDetailResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    /// <summary>
    /// "Eliminar rascunho" — deletes an abandoned Draft before its operational
    /// lock (<c>HandoverRules.CanDeleteDraft</c>). The same permission that
    /// already governs removing a Draft's items; eligibility is enforced
    /// server-side by <c>EquipmentHandoverService.DeleteDraftAsync</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.EquipmentHandover.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> DeleteDraft(Guid id, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await handoverService.DeleteDraftAsync(id, actorId, actorName, cancellationToken);

        return result.Succeeded ? NoContent() : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpPost("{id:guid}/items")]
    [Authorize(Policy = Permissions.EquipmentHandover.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> AddItem(Guid id, [FromBody] AddHandoverItemRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await handoverService.AddItemAsync(id, request.EquipmentId, actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToDetailResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    [Authorize(Policy = Permissions.EquipmentHandover.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> RemoveItem(Guid id, Guid itemId, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await handoverService.RemoveItemAsync(id, itemId, actorId, actorName, cancellationToken);

        return result.Succeeded ? NoContent() : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpPut("{id:guid}/items/{itemId:guid}/inspection")]
    [Authorize(Policy = Permissions.EquipmentHandover.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> RecordInspection(
        Guid id, Guid itemId, [FromBody] RecordInspectionRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await handoverService.RecordInspectionAsync(
            id, itemId, new RecordInspectionData(request.Condition, request.Notes), actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToInspectionResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    /// <summary>
    /// Multipart upload (docs/PROJECT_SPEC.md, section 10). The file's actual
    /// bytes are validated by magic-byte signature server-side — the declared
    /// <see cref="IFormFile.ContentType"/> is never trusted alone (see
    /// <c>EquipmentHandoverService.UploadPhotoAsync</c>).
    /// </summary>
    [HttpPost("{id:guid}/items/{itemId:guid}/inspection/photos")]
    [Authorize(Policy = Permissions.EquipmentHandover.Update)]
    [ValidateCsrf]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadPhoto(Guid id, Guid itemId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return Problem(title: "No file was uploaded.", statusCode: StatusCodes.Status400BadRequest);
        }

        var (actorId, actorName) = await GetActorAsync();
        await using var stream = file.OpenReadStream();
        var result = await handoverService.UploadPhotoAsync(
            id, itemId, stream, file.FileName, file.ContentType, actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToPhotoResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}/inspection/photos/{photoId:guid}")]
    [Authorize(Policy = Permissions.EquipmentHandover.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> DeletePhoto(Guid id, Guid itemId, Guid photoId, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await handoverService.DeletePhotoAsync(id, itemId, photoId, actorId, actorName, cancellationToken);

        return result.Succeeded ? NoContent() : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpGet("{id:guid}/items/{itemId:guid}/inspection/photos/{photoId:guid}/content")]
    [Authorize(Policy = Permissions.EquipmentHandover.Read)]
    public async Task<IActionResult> GetPhotoContent(Guid id, Guid itemId, Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await handoverService.GetPhotoContentAsync(id, itemId, photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.Value.Content, photo.Value.ContentType, photo.Value.OriginalFileName);
    }

    [HttpPost("{id:guid}/finalize")]
    [Authorize(Policy = Permissions.Equipment.Assign)]
    [ValidateCsrf]
    public async Task<IActionResult> Finalize(Guid id, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await handoverService.FinalizeAsync(id, actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToDetailResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    /// <summary>
    /// "Registar envio" — records that HR has physically shipped this Draft
    /// handover's equipment and notifies the Consultant of the expected
    /// delivery window. Only usable while the handover is still Draft (see
    /// <c>EquipmentDispatchService</c>'s remarks); once dispatched, the
    /// handover's item composition is locked (<c>HandoverRules.CanModifyComposition</c>)
    /// but it can still be finalized normally.
    /// </summary>
    [HttpPost("{id:guid}/dispatch")]
    [Authorize(Policy = Permissions.EquipmentHandover.Dispatch)]
    [ValidateCsrf]
    public async Task<IActionResult> RegisterDispatch(Guid id, [FromBody] RegisterDispatchRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await dispatchService.RegisterAsync(
            id, new RegisterDispatchData(request.ExpectedDeliveryFromDate, request.ExpectedDeliveryToDate), actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToDispatchResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    /// <summary>
    /// "Anular envio" — cancels the active carrier dispatch before delivery is
    /// confirmed: the shipped equipment returns from InTransit to Available,
    /// no Assignment is involved, and the cancelled attempt stays as history.
    /// Every precondition is re-checked server-side (see
    /// <c>EquipmentDispatchService.CancelAsync</c>).
    /// </summary>
    [HttpPost("{id:guid}/dispatch/cancel")]
    [Authorize(Policy = Permissions.EquipmentHandover.CancelDispatch)]
    [ValidateCsrf]
    public async Task<IActionResult> CancelDispatch(Guid id, [FromBody] CancelDispatchRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await dispatchService.CancelAsync(id, request.Reason, actorId, actorName, cancellationToken);

        return result.Succeeded ? NoContent() : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    /// <summary>
    /// Generates a new Responsibility Term version for this handover — the
    /// same operation whether this is the first document or a regeneration
    /// (docs/PROJECT_SPEC.md, section 11; Phase 6 scope). Takes
    /// no request body: every field of the resulting document is either
    /// server-derived or comes from this handover's own historical snapshots,
    /// never from caller input.
    /// </summary>
    [HttpPost("{id:guid}/responsibility-terms")]
    [Authorize(Policy = Permissions.ResponsibilityTerm.Generate)]
    [ValidateCsrf]
    public async Task<IActionResult> GenerateResponsibilityTerm(Guid id, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await termService.GenerateAsync(id, actorId, actorName, cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblem(result.ErrorCode, result.ErrorMessage!);
        }

        return CreatedAtAction(
            nameof(ResponsibilityTermsController.GetById), "ResponsibilityTerms", new { id = result.Value!.Id },
            await ToTermResponseAsync(result.Value, cancellationToken));
    }

    /// <summary>Newest version first (Phase 6 scope) — metadata only, never PDF bytes and never <c>StorageKey</c>.</summary>
    [HttpGet("{id:guid}/responsibility-terms")]
    [Authorize(Policy = Permissions.ResponsibilityTerm.Read)]
    public async Task<IActionResult> ListResponsibilityTerms(Guid id, CancellationToken cancellationToken)
    {
        var terms = await termService.ListAsync(id, cancellationToken);
        var responses = new List<ResponsibilityTermResponse>(terms.Count);
        foreach (var term in terms)
        {
            responses.Add(await ToTermResponseAsync(term, cancellationToken));
        }

        return Ok(responses);
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
            ServiceErrorCode.HandoverNotDraft => StatusCodes.Status409Conflict,
            ServiceErrorCode.EquipmentAlreadyInHandover => StatusCodes.Status409Conflict,
            ServiceErrorCode.InvalidLifecycleTransition => StatusCodes.Status409Conflict,
            ServiceErrorCode.InspectionMissing => StatusCodes.Status409Conflict,
            ServiceErrorCode.InsufficientPhotos => StatusCodes.Status409Conflict,
            ServiceErrorCode.Concurrency => StatusCodes.Status409Conflict,
            ServiceErrorCode.HandoverNotFinalized => StatusCodes.Status409Conflict,
            ServiceErrorCode.SnapshotIncomplete => StatusCodes.Status409Conflict,
            ServiceErrorCode.MissingCurrentPhotoSet => StatusCodes.Status409Conflict,
            ServiceErrorCode.HandoverCompositionLocked => StatusCodes.Status409Conflict,
            ServiceErrorCode.DispatchAlreadyRegistered => StatusCodes.Status409Conflict,
            ServiceErrorCode.ConsultantNotEligible => StatusCodes.Status409Conflict,
            ServiceErrorCode.DeliveryMethodRequired => StatusCodes.Status409Conflict,
            ServiceErrorCode.DispatchNotApplicable => StatusCodes.Status409Conflict,
            ServiceErrorCode.DispatchRequired => StatusCodes.Status409Conflict,
            ServiceErrorCode.InconsistentAllocationState => StatusCodes.Status409Conflict,
            ServiceErrorCode.InvalidFile => StatusCodes.Status400BadRequest,
            ServiceErrorCode.FileTooLarge => StatusCodes.Status400BadRequest,
            ServiceErrorCode.Validation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest,
        };

        return Problem(title: message, statusCode: status);
    }

    private static EquipmentDispatchResponse ToDispatchResponse(EquipmentDispatchRecord d) => new(
        d.Id, d.EquipmentHandoverId, d.ExpectedDeliveryFromDate, d.ExpectedDeliveryToDate, d.DispatchedAtUtc, d.DispatchedByDisplayNameSnapshot);

    private static EquipmentHandoverSummaryResponse ToSummaryResponse(EquipmentHandoverSummaryRecord h) => new(
        h.Id, h.ConsultantId, h.ConsultantFullName, h.Status, h.OperationalStatus, h.DeliveredOn, h.ItemCount, h.CreatedAtUtc, h.FinalizedAtUtc);

    private static DeliveryInspectionPhotoResponse ToPhotoResponse(DeliveryInspectionPhotoRecord p) => new(
        p.Id, p.OriginalFileName, p.ContentType, p.SizeBytes, p.SortOrder, p.UploadedAtUtc);

    private static DeliveryInspectionResponse ToInspectionResponse(DeliveryInspectionRecord i) => new(
        i.Id, i.Condition, i.Notes, i.InspectedByDisplayNameSnapshot, i.InspectedAtUtc, i.Photos.Select(ToPhotoResponse).ToList());

    private static EquipmentSnapshotResponse ToSnapshotResponse(EquipmentSnapshotRecord s) => new(
        s.AssetTag, s.EquipmentTypeName, s.Manufacturer, s.Model, s.SerialNumber, s.Processor, s.Memory, s.Storage);

    private static EquipmentHandoverItemResponse ToItemResponse(EquipmentHandoverItemRecord i) => new(
        i.Id, i.EquipmentId, i.AssetTag, i.EquipmentTypeName, i.Manufacturer, i.Model, i.SerialNumber,
        i.EquipmentSnapshot is null ? null : ToSnapshotResponse(i.EquipmentSnapshot), i.Inspection is null ? null : ToInspectionResponse(i.Inspection),
        i.EquipmentStatus);

    private static EquipmentHandoverDetailResponse ToDetailResponse(EquipmentHandoverDetailRecord h) => new(
        h.Id, h.ConsultantId, h.ConsultantFullName, h.Status, h.OperationalStatus, h.DeliveryMethod, h.DeliveredOn, h.Notes, h.CreatedByDisplayNameSnapshot,
        h.CreatedAtUtc, h.UpdatedAtUtc, h.FinalizedAtUtc,
        h.ConsultantSnapshot is null ? null : new ConsultantSnapshotResponse(h.ConsultantSnapshot.FullName, h.ConsultantSnapshot.Nif, h.ConsultantSnapshot.Address, h.ConsultantSnapshot.Phone, h.ConsultantSnapshot.Email),
        h.AllocationSnapshot is null ? null : new AllocationSnapshotResponse(h.AllocationSnapshot.WorkforceType, h.AllocationSnapshot.EngagementType, h.AllocationSnapshot.RoleOrFunction, h.AllocationSnapshot.CompanyName),
        h.Items.Select(ToItemResponse).ToList(), h.Dispatch is null ? null : ToDispatchResponse(h.Dispatch),
        h.CancelledDispatches?.Select(d => new CancelledDispatchResponse(
            d.Id, d.DispatchedAtUtc, d.DispatchedByDisplayNameSnapshot, d.CancelledAtUtc, d.CancelledByDisplayNameSnapshot, d.CancellationReason)).ToList());

    /// <summary>Mirrors <c>ResponsibilityTermsController.ToResponseAsync</c> — kept here too rather than shared, so each controller owns its own response-composition without cross-controller coupling.</summary>
    private async Task<ResponsibilityTermResponse> ToTermResponseAsync(ResponsibilityTermRecord t, CancellationToken cancellationToken)
    {
        var (lastSentAtUtc, sendAttemptCount, hasAttemptingDelivery) = await deliveryService.GetSendSummaryAsync(t.Id, cancellationToken);
        var signedArtifact = await signedArtifactService.GetAsync(t.Id, cancellationToken);
        var executionStatus = ResponsibilityTermDeliveryRules.DeriveExecutionStatus(signedArtifact is not null, lastSentAtUtc is not null, hasAttemptingDelivery);

        return new ResponsibilityTermResponse(
            t.Id, t.EquipmentHandoverId, t.DocumentNumber, t.Version, t.Status, t.TemplateVersion, t.GeneratedAtUtc,
            t.GeneratedByDisplayNameSnapshot, t.FileName, t.SizeBytes, t.Sha256, t.CreatedAtUtc,
            executionStatus, lastSentAtUtc, sendAttemptCount,
            signedArtifact is null ? null : new ResponsibilityTermSignatureResponse(
                signedArtifact.SignedOn, signedArtifact.UploadedAtUtc, signedArtifact.SizeBytes, signedArtifact.Sha256, signedArtifact.UploadedByDisplayNameSnapshot));
    }
}
