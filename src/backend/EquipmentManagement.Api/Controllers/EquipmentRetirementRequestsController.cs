using System.Security.Claims;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Equipments;
using EquipmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// "Solicitações de abate" — HR requests equipment retirement, Administration
/// reviews and decides (approve executes the existing retirement operation;
/// reject leaves the equipment lifecycle untouched). See
/// <see cref="EquipmentRetirementRequestService"/>'s remarks for the full
/// workflow and its transactional guarantees.
/// </summary>
[ApiController]
[Route("api/equipment-retirement-requests")]
public sealed class EquipmentRetirementRequestsController(
    EquipmentRetirementRequestService requestService, UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.EquipmentRetirementRequest.Read)]
    public async Task<IActionResult> List(
        [FromQuery] EquipmentRetirementRequestStatus? status = EquipmentRetirementRequestStatus.Pending,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await requestService.ListAsync(new EquipmentRetirementRequestListFilter(status), page, pageSize, cancellationToken);
        return Ok(new PagedResponse<EquipmentRetirementRequestResponse>(
            result.Items.Select(ToResponse).ToList(), result.Page, result.PageSize, result.TotalCount));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.EquipmentRetirementRequest.Create)]
    [ValidateCsrf]
    public async Task<IActionResult> Create([FromBody] CreateEquipmentRetirementRequestRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await requestService.CreateAsync(request.EquipmentId, request.Reason, actorId, actorName, cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblem(result.ErrorCode, result.ErrorMessage!);
        }

        return Created($"/api/equipment-retirement-requests/{result.Value!.Id}", ToResponse(result.Value));
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = Permissions.EquipmentRetirementRequest.Decide)]
    [ValidateCsrf]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await requestService.ApproveAsync(id, actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = Permissions.EquipmentRetirementRequest.Decide)]
    [ValidateCsrf]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectEquipmentRetirementRequestRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await requestService.RejectAsync(id, request.Reason, actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
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
            ServiceErrorCode.RetirementRequestAlreadyPending => StatusCodes.Status409Conflict,
            ServiceErrorCode.RetirementRequestNotPending => StatusCodes.Status409Conflict,
            ServiceErrorCode.Concurrency => StatusCodes.Status409Conflict,
            ServiceErrorCode.Validation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest,
        };

        return Problem(title: message, statusCode: status);
    }

    private static EquipmentRetirementRequestResponse ToResponse(EquipmentRetirementRequestRecord r) => new(
        r.Id, r.EquipmentId, r.AssetTag, r.EquipmentTypeName, r.Manufacturer, r.Model, r.EquipmentStatus,
        r.RequestedByDisplayName, r.RequestedAtUtc, r.RequestReason, r.Status, r.DecidedByDisplayName, r.DecidedAtUtc, r.DecisionReason);
}
