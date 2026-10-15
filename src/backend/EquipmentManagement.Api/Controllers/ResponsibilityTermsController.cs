using System.Security.Claims;
using EquipmentManagement.Api.Contracts.ResponsibilityTerms;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.ResponsibilityTerms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// Read/download access to already-generated Responsibility Terms, plus the
/// Phase 7 email-delivery and externally-signed-document workflow
/// (docs/PROJECT_SPEC.md, section 11; Phase 6/7 scope). Generation itself
/// lives on <see cref="EquipmentHandoversController"/> (scoped under the
/// handover it belongs to — <c>POST .../responsibility-terms</c>). No content
/// is ever publicly reachable — every route here requires authentication and
/// a <c>ResponsibilityTerm.*</c> permission (Phase 6 scope;
/// Phase 7 scope).
/// </summary>
[ApiController]
[Route("api/responsibility-terms")]
public sealed class ResponsibilityTermsController(
    ResponsibilityTermService termService,
    ResponsibilityTermDeliveryService deliveryService,
    ResponsibilityTermSignedArtifactService signedArtifactService,
    UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.ResponsibilityTerm.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var term = await termService.GetByIdAsync(id, cancellationToken);
        return term is null ? NotFound() : Ok(await ToResponseAsync(term, cancellationToken));
    }

    /// <summary>
    /// Streams the private PDF — never a public URL, never the storage key,
    /// and never base64 inside JSON (Phase 6 scope). The
    /// download filename is entirely server-derived from the document number
    /// (never Consultant name/NIF — Phase 6 scope); a caller
    /// cannot influence it. The access itself is audited, since the document
    /// contains Consultant PII (Phase 6 scope).
    /// </summary>
    [HttpGet("{id:guid}/content")]
    [Authorize(Policy = Permissions.ResponsibilityTerm.Read)]
    public async Task<IActionResult> GetContent(Guid id, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var content = await termService.GetContentAsync(id, actorId, actorName, cancellationToken);

        return content is null ? NotFound() : File(content.Value.Content, content.Value.ContentType, content.Value.FileName);
    }

    /// <summary>
    /// Sends (or resends) this exact term version to the consultant for
    /// external signature (Phase 7 scope). Only the current
    /// version can start a new send; a superseded term returns 409. Takes no
    /// implicit recipient — the historical Consultant snapshot email is used
    /// unless <see cref="SendResponsibilityTermRequest.RecipientEmail"/>
    /// overrides it (Phase 7 scope).
    /// </summary>
    [HttpPost("{id:guid}/send")]
    [Authorize(Policy = Permissions.ResponsibilityTerm.Send)]
    [ValidateCsrf]
    public async Task<IActionResult> Send(Guid id, [FromBody] SendResponsibilityTermRequest? request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await deliveryService.SendAsync(id, request?.RecipientEmail, actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToDeliveryResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    /// <summary>Full send-attempt history for this term, newest first (Phase 7 scope) — nothing is ever removed, including failed attempts.</summary>
    [HttpGet("{id:guid}/deliveries")]
    [Authorize(Policy = Permissions.ResponsibilityTerm.Read)]
    public async Task<IActionResult> ListDeliveries(Guid id, CancellationToken cancellationToken)
    {
        var deliveries = await deliveryService.ListAsync(id, cancellationToken);
        return Ok(deliveries.Select(ToDeliveryResponse).ToList());
    }

    /// <summary>
    /// Registers the externally-signed PDF HR received back for this exact
    /// term version (Phase 7 scope). Only PDF is accepted,
    /// validated by magic bytes, never trusting the declared Content-Type or
    /// filename alone. At most one signed artifact may ever exist per term
    /// version — a second upload is rejected (409), never an overwrite
    /// (Phase 7 scope).
    /// </summary>
    [HttpPost("{id:guid}/signed-document")]
    [Authorize(Policy = Permissions.ResponsibilityTerm.SignatureManage)]
    [ValidateCsrf]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadSignedDocument(
        Guid id, IFormFile file, [FromForm] DateOnly? signedOn, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return Problem(title: "No file was uploaded.", statusCode: StatusCodes.Status400BadRequest);
        }

        var (actorId, actorName) = await GetActorAsync();
        await using var stream = file.OpenReadStream();
        var result = await signedArtifactService.UploadAsync(id, stream, file.ContentType, signedOn, actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToSignatureResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    /// <summary>Streams the private signed PDF — same discipline as <see cref="GetContent"/> for the original (Phase 7 scope).</summary>
    [HttpGet("{id:guid}/signed-document/content")]
    [Authorize(Policy = Permissions.ResponsibilityTerm.Read)]
    public async Task<IActionResult> GetSignedDocumentContent(Guid id, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var content = await signedArtifactService.GetContentAsync(id, actorId, actorName, cancellationToken);

        return content is null ? NotFound() : File(content.Value.Content, content.Value.ContentType, content.Value.FileName);
    }

    private async Task<(Guid Id, string DisplayName)> GetActorAsync()
    {
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var actor = await userManager.FindByIdAsync(id.ToString());
        return (id, actor is null ? id.ToString() : $"{actor.FirstName} {actor.LastName}");
    }

    private async Task<ResponsibilityTermResponse> ToResponseAsync(ResponsibilityTermRecord t, CancellationToken cancellationToken)
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

    private static ResponsibilityTermDeliveryResponse ToDeliveryResponse(ResponsibilityTermDeliveryRecord d) => new(
        d.Id, d.RecipientEmailSnapshot, d.RecipientOverridden, d.Status, d.AttemptedAtUtc, d.SentAtUtc, d.FailureCode, d.SentByDisplayNameSnapshot, d.CreatedAtUtc);

    private static ResponsibilityTermSignatureResponse ToSignatureResponse(ResponsibilityTermSignedArtifactRecord a) => new(
        a.SignedOn, a.UploadedAtUtc, a.SizeBytes, a.Sha256, a.UploadedByDisplayNameSnapshot);

    private ObjectResult ToProblem(ServiceErrorCode code, string message)
    {
        var status = code switch
        {
            ServiceErrorCode.NotFound => StatusCodes.Status404NotFound,
            ServiceErrorCode.SnapshotIncomplete => StatusCodes.Status409Conflict,
            ServiceErrorCode.TermNotCurrent => StatusCodes.Status409Conflict,
            ServiceErrorCode.HandoverNotFinalized => StatusCodes.Status409Conflict,
            ServiceErrorCode.SignedArtifactAlreadyExists => StatusCodes.Status409Conflict,
            ServiceErrorCode.InvalidRecipient => StatusCodes.Status400BadRequest,
            ServiceErrorCode.InvalidFile => StatusCodes.Status400BadRequest,
            ServiceErrorCode.FileTooLarge => StatusCodes.Status400BadRequest,
            ServiceErrorCode.Validation => StatusCodes.Status400BadRequest,
            ServiceErrorCode.EmailSendFailed => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest,
        };

        return Problem(title: message, statusCode: status);
    }
}
