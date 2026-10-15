using System.Security.Claims;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Allocations;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Allocations;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// Historical allocation management, nested under its owning consultant
/// (docs/PROJECT_SPEC.md, section 6; Phase 2 scope). There is no
/// generic PUT that can rewrite Company/WorkforceType/dates or touch an Ended
/// allocation — see <c>UpdateConsultantAllocationRequest</c>'s remarks and the
/// ADR on the no-historical-overwrite rule.
/// </summary>
[ApiController]
[Route("api/consultants/{consultantId:guid}/allocations")]
public sealed class ConsultantAllocationsController(
    ConsultantAllocationService allocationService, UserManager<ApplicationUser> userManager, IAuthorizationService authorizationService)
    : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.ConsultantAllocation.Read)]
    public async Task<IActionResult> List(
        Guid consultantId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await allocationService.ListForConsultantAsync(consultantId, page, pageSize, cancellationToken);
        return Ok(new PagedResponse<ConsultantAllocationResponse>(
            result.Items.Select(ToResponse).ToList(), result.Page, result.PageSize, result.TotalCount));
    }

    [HttpGet("{allocationId:guid}")]
    [Authorize(Policy = Permissions.ConsultantAllocation.Read)]
    public async Task<IActionResult> GetById(Guid consultantId, Guid allocationId, CancellationToken cancellationToken)
    {
        var allocation = await allocationService.GetByIdAsync(consultantId, allocationId, cancellationToken);
        return allocation is null ? NotFound() : Ok(ToResponse(allocation));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.ConsultantAllocation.Create)]
    [ValidateCsrf]
    public async Task<IActionResult> Create(
        Guid consultantId, [FromBody] CreateConsultantAllocationRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await allocationService.CreateAsync(
            consultantId,
            new CreateAllocationData(request.WorkforceType, request.EngagementType, request.RoleOrFunction, request.CompanyId, request.StartDate, request.EndDate),
            actorId, actorName, cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblem(result.ErrorCode, result.ErrorMessage!);
        }

        return CreatedAtAction(nameof(GetById), new { consultantId, allocationId = result.Value!.Id }, ToResponse(result.Value));
    }

    [HttpPut("{allocationId:guid}")]
    [Authorize(Policy = Permissions.ConsultantAllocation.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> Update(
        Guid consultantId, Guid allocationId, [FromBody] UpdateConsultantAllocationRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await allocationService.UpdateRoleOrFunctionAsync(
            consultantId, allocationId, new UpdateAllocationData(request.RoleOrFunction), actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpPost("{allocationId:guid}/end")]
    [Authorize(Policy = Permissions.ConsultantAllocation.End)]
    [ValidateCsrf]
    public async Task<IActionResult> End(
        Guid consultantId, Guid allocationId, [FromBody] EndConsultantAllocationRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();

        // An end of engagement initiates equipment returns, which the
        // single-equipment endpoint gates by Equipment.Return — the same
        // permission is required here for that part (see EndAllocationData).
        var mayInitiateReturns = (await authorizationService.AuthorizeAsync(User, Permissions.Equipment.Return)).Succeeded;

        var result = await allocationService.EndAsync(
            consultantId, allocationId,
            new EndAllocationData(
                request.EndDate, request.IsEndOfEngagement, mayInitiateReturns, request.ExpectedCollectionFromDate, request.ExpectedCollectionToDate),
            actorId, actorName, cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblem(result.ErrorCode, result.ErrorMessage!);
        }

        var a = result.Value!.Allocation;
        return Ok(new EndConsultantAllocationResponse(
            a.Id, a.ConsultantId, a.CompanyId, a.CompanyName, a.WorkforceType, a.EngagementType, a.RoleOrFunction,
            a.StartDate, a.EndDate, a.Status, a.IsEndOfEngagement, a.ExpectedCollectionFromDate, a.ExpectedCollectionToDate,
            a.CreatedAtUtc, a.UpdatedAtUtc, result.Value.ReturnsCreatedCount, result.Value.ReturnsAlreadyPendingCount, result.Value.ReturnGroupId));
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
            ServiceErrorCode.ActiveAllocationAlreadyExists => StatusCodes.Status409Conflict,
            ServiceErrorCode.AllocationNotActive => StatusCodes.Status409Conflict,
            ServiceErrorCode.Concurrency => StatusCodes.Status409Conflict,
            ServiceErrorCode.EquipmentRequiresAttention => StatusCodes.Status409Conflict,
            ServiceErrorCode.ReturnPermissionRequired => StatusCodes.Status403Forbidden,
            ServiceErrorCode.CollectionEstimateRequired => StatusCodes.Status400BadRequest,
            ServiceErrorCode.Validation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest,
        };

        return Problem(title: message, statusCode: status);
    }

    private static ConsultantAllocationResponse ToResponse(ConsultantAllocationRecord a) => new(
        a.Id, a.ConsultantId, a.CompanyId, a.CompanyName, a.WorkforceType, a.EngagementType, a.RoleOrFunction,
        a.StartDate, a.EndDate, a.Status, a.IsEndOfEngagement, a.ExpectedCollectionFromDate, a.ExpectedCollectionToDate,
        a.CreatedAtUtc, a.UpdatedAtUtc);
}
