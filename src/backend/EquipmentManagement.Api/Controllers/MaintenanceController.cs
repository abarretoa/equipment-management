using System.Security.Claims;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Maintenance;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Domain.Maintenance;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Maintenance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// The maintenance workflow (docs/PROJECT_SPEC.md, section 13; Phase 5 scope). Starting maintenance lives on <see cref="EquipmentController"/>
/// (<c>POST /api/equipment/{id}/maintenance</c>) since it acts on an Equipment
/// resource; everything about an already-started maintenance record lives here.
/// No generic status <c>PUT</c> exists — only explicit lifecycle operations.
/// </summary>
[ApiController]
[Route("api/maintenance")]
public sealed class MaintenanceController(MaintenanceService maintenanceService, UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.EquipmentMaintenance.Read)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? equipmentId, [FromQuery] MaintenanceStatus? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await maintenanceService.ListAsync(new MaintenanceListFilter(equipmentId, status), page, pageSize, cancellationToken);
        return Ok(new PagedResponse<MaintenanceSummaryResponse>(
            result.Items.Select(ToSummaryResponse).ToList(), result.Page, result.PageSize, result.TotalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.EquipmentMaintenance.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var maintenance = await maintenanceService.GetByIdAsync(id, cancellationToken);
        return maintenance is null ? NotFound() : Ok(ToDetailResponse(maintenance));
    }

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = Permissions.EquipmentMaintenance.Manage)]
    [ValidateCsrf]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteMaintenanceRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await maintenanceService.CompleteAsync(
            id, new CompleteMaintenanceData(request.Outcome, request.Notes), actorId, actorName, cancellationToken);

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
            ServiceErrorCode.MaintenanceNotEligible => StatusCodes.Status409Conflict,
            ServiceErrorCode.MaintenanceNotActive => StatusCodes.Status409Conflict,
            ServiceErrorCode.InvalidLifecycleTransition => StatusCodes.Status409Conflict,
            ServiceErrorCode.Concurrency => StatusCodes.Status409Conflict,
            ServiceErrorCode.Validation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest,
        };

        return Problem(title: message, statusCode: status);
    }

    private static MaintenanceSummaryResponse ToSummaryResponse(MaintenanceSummaryRecord m) => new(
        m.Id, m.EquipmentId, m.AssetTag, m.EquipmentTypeName, m.Manufacturer, m.Model, m.Status, m.SentAtUtc, m.CompletedAtUtc);

    private static MaintenanceDetailResponse ToDetailResponse(MaintenanceDetailRecord m) => new(
        m.Id, m.EquipmentId, m.AssetTag, m.EquipmentTypeName, m.DamageId, m.Status, m.Notes, m.Outcome, m.SentAtUtc, m.CompletedAtUtc,
        m.CreatedAtUtc, m.UpdatedAtUtc);
}
