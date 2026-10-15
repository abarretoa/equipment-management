using System.Security.Claims;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.Returns;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Domain.Handovers;
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
/// Read-only view over "who currently has (or has had) which equipment"
/// (docs/PROJECT_SPEC.md, section 2), plus the single write endpoint that
/// initiates a return for one assignment (docs/PROJECT_SPEC.md, section 12;
/// Phase 5 scope — a return is scoped to one assignment, never the
/// whole originating multi-item handover). Every other assignment row is
/// created exclusively by <c>EquipmentHandoverService.FinalizeAsync</c>, and
/// ending one is exclusively <c>EquipmentReturnService.ReceiveAsync</c>'s job.
/// </summary>
[ApiController]
[Route("api/equipment-assignments")]
public sealed class EquipmentAssignmentsController(
    EquipmentAssignmentQueryService assignmentQueryService, EquipmentReturnService returnService, UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.EquipmentAssignment.Read)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? equipmentId, [FromQuery] Guid? consultantId, [FromQuery] AssignmentStatus? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await assignmentQueryService.ListAsync(
            new EquipmentAssignmentListFilter(equipmentId, consultantId, status), page, pageSize, cancellationToken);

        return Ok(new PagedResponse<EquipmentAssignmentResponse>(
            result.Items.Select(ToAssignmentResponse).ToList(),
            result.Page, result.PageSize, result.TotalCount));
    }

    [HttpPost("{id:guid}/return")]
    [Authorize(Policy = Permissions.Equipment.Return)]
    [ValidateCsrf]
    public async Task<IActionResult> Return(Guid id, [FromBody] InitiateReturnRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await returnService.InitiateAsync(
            id, new InitiateReturnData(request.ExpectedReturnDate, request.Reason), actorId, actorName, cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblem(result.ErrorCode, result.ErrorMessage!);
        }

        return CreatedAtAction(
            nameof(EquipmentReturnsController.GetById), "EquipmentReturns", new { id = result.Value!.Id }, ToDetailResponse(result.Value));
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
            ServiceErrorCode.InvalidLifecycleTransition => StatusCodes.Status409Conflict,
            ServiceErrorCode.ReturnAlreadyInProgress => StatusCodes.Status409Conflict,
            ServiceErrorCode.Concurrency => StatusCodes.Status409Conflict,
            ServiceErrorCode.Validation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest,
        };

        return Problem(title: message, statusCode: status);
    }

    private static EquipmentReturnDetailResponse ToDetailResponse(EquipmentReturnDetailRecord r) => new(
        r.Id, r.EquipmentAssignmentId, r.EquipmentId, r.AssetTag, r.EquipmentTypeName, r.ConsultantId, r.ConsultantFullName, r.Status,
        r.Reason, r.ExpectedReturnDate, r.ReceivedOn, r.ReceivedAtUtc, r.ReceivedByDisplayNameSnapshot, r.CreatedByDisplayNameSnapshot,
        r.CreatedAtUtc, r.UpdatedAtUtc, r.CompletedAtUtc, r.CancelledAtUtc, r.CancelledByDisplayNameSnapshot, r.CancellationReason, null,
        r.ReturnGroupId, r.ReturnGroupSize);

    /// <summary>Shared with <see cref="ConsultantsController"/> for the consultant's current-equipment projection — same source-of-truth record, same mapping.</summary>
    internal static EquipmentAssignmentResponse ToAssignmentResponse(EquipmentAssignmentRecord a) => new(
        a.Id, a.EquipmentId, a.AssetTag, a.EquipmentTypeName, a.Manufacturer, a.Model, a.ConsultantId, a.ConsultantFullName,
        a.Status, a.EquipmentHandoverId, a.StartedAtUtc, a.EndedAtUtc, a.EquipmentStatus, a.Origin);
}
