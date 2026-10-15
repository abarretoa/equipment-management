using System.Security.Claims;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Allocations;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Allocations;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Consultants;
using EquipmentManagement.Infrastructure.Handovers;
using EquipmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// Consultant profile management (docs/PROJECT_SPEC.md, sections 5, 9-11;
/// Phase 2 scope). Never exposes ASP.NET Core Identity concepts —
/// consultants have no login (docs/PROJECT_SPEC.md, section 4).
/// </summary>
[ApiController]
[Route("api/consultants")]
public sealed class ConsultantsController(
    ConsultantService consultantService,
    ConsultantAllocationService allocationService,
    EquipmentAssignmentQueryService assignmentQueryService,
    UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.Consultant.Read)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] string? nif,
        [FromQuery] WorkforceType? workforceType,
        [FromQuery] Guid? companyId,
        [FromQuery] bool? hasActiveAllocation,
        [FromQuery] bool? hasActiveCustody,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await consultantService.ListAsync(
            new ConsultantListFilter(search, isActive, nif, workforceType, companyId, hasActiveAllocation, hasActiveCustody), page, pageSize, cancellationToken);

        return Ok(new PagedResponse<ConsultantSummaryResponse>(
            result.Items.Select(ToSummaryResponse).ToList(), result.Page, result.PageSize, result.TotalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Consultant.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var consultant = await consultantService.GetByIdAsync(id, cancellationToken);
        return consultant is null ? NotFound() : Ok(await BuildDetailResponseAsync(consultant, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Consultant.Create)]
    [ValidateCsrf]
    public async Task<IActionResult> Create([FromBody] CreateConsultantRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await consultantService.CreateAsync(
            new CreateConsultantData(request.FullName, request.Nif, request.Email, request.Address, request.Phone),
            actorId, actorName, cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblem(result.ErrorCode, result.ErrorMessage!);
        }

        var response = await BuildDetailResponseAsync(result.Value!, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Consultant.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateConsultantRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await consultantService.UpdateAsync(
            id, new UpdateConsultantData(request.FullName, request.Nif, request.Email, request.Address, request.Phone),
            actorId, actorName, cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblem(result.ErrorCode, result.ErrorMessage!);
        }

        return Ok(await BuildDetailResponseAsync(result.Value!, cancellationToken));
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Permissions.Consultant.Archive)]
    [ValidateCsrf]
    public Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) => SetActiveAsync(id, true, cancellationToken);

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Permissions.Consultant.Archive)]
    [ValidateCsrf]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) => SetActiveAsync(id, false, cancellationToken);

    private async Task<IActionResult> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await consultantService.SetActiveAsync(id, active, actorId, actorName, cancellationToken);
        if (!result.Succeeded)
        {
            return ToProblem(result.ErrorCode, result.ErrorMessage!);
        }

        return Ok(await BuildDetailResponseAsync(result.Value!, cancellationToken));
    }

    private async Task<ConsultantDetailResponse> BuildDetailResponseAsync(ConsultantRecord consultant, CancellationToken cancellationToken)
    {
        var current = await allocationService.GetCurrentAsync(consultant.Id, cancellationToken);
        // History is bounded, not "every historical relationship by default"
        // (Phase 2 scope) — the first
        // page of most-recent allocations; the dedicated allocations endpoint
        // paginates further if a consultant genuinely has more.
        var history = await allocationService.ListForConsultantAsync(consultant.Id, page: 1, pageSize: 20, cancellationToken);

        // Current equipment custody — the Active-status EquipmentAssignment rows
        // for this consultant, the single source of truth (see the type's remarks).
        var currentEquipment = await assignmentQueryService.ListAsync(
            new EquipmentAssignmentListFilter(EquipmentId: null, ConsultantId: consultant.Id, Status: AssignmentStatus.Active),
            page: 1, pageSize: 100, cancellationToken);

        return new ConsultantDetailResponse(
            consultant.Id, consultant.FullName, consultant.Nif, consultant.Email, consultant.Address, consultant.Phone,
            consultant.IsActive, consultant.CreatedAtUtc, consultant.UpdatedAtUtc,
            current is null ? null : ToAllocationResponse(current),
            history.Items.Select(ToAllocationResponse).ToList(),
            currentEquipment.Items.Select(EquipmentAssignmentsController.ToAssignmentResponse).ToList());
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
            ServiceErrorCode.DuplicateNif => StatusCodes.Status409Conflict,
            ServiceErrorCode.InactiveReference => StatusCodes.Status409Conflict,
            ServiceErrorCode.Concurrency => StatusCodes.Status409Conflict,
            ServiceErrorCode.Validation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest,
        };

        return Problem(title: message, statusCode: status);
    }

    private static ConsultantSummaryResponse ToSummaryResponse(ConsultantSummaryRecord c) => new(
        c.Id, c.FullName, c.IsActive, c.CreatedAtUtc, c.CurrentWorkforceType, c.CurrentCompanyId,
        c.CurrentCompanyName, c.CurrentEngagementType, c.CurrentRoleOrFunction);

    private static ConsultantAllocationResponse ToAllocationResponse(ConsultantAllocationRecord a) => new(
        a.Id, a.ConsultantId, a.CompanyId, a.CompanyName, a.WorkforceType, a.EngagementType, a.RoleOrFunction,
        a.StartDate, a.EndDate, a.Status, a.IsEndOfEngagement, a.ExpectedCollectionFromDate, a.ExpectedCollectionToDate,
        a.CreatedAtUtc, a.UpdatedAtUtc);
}
