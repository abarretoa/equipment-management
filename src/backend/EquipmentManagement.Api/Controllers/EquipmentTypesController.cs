using System.Security.Claims;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Equipments;
using EquipmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// Equipment-type reference data (docs/PROJECT_SPEC.md, section 7) — creatable
/// by HR or Administration without a code change. Archive is Administration-only
/// (removes a type from normal operational circulation), mirroring the same
/// split already used for Company/Consultant.
/// </summary>
[ApiController]
[Route("api/equipment-types")]
public sealed class EquipmentTypesController(EquipmentTypeService equipmentTypeService, UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.EquipmentType.Read)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await equipmentTypeService.ListAsync(new EquipmentTypeListFilter(search, isActive), page, pageSize, cancellationToken);
        return Ok(new PagedResponse<EquipmentTypeResponse>(result.Items.Select(ToResponse).ToList(), result.Page, result.PageSize, result.TotalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.EquipmentType.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var type = await equipmentTypeService.GetByIdAsync(id, cancellationToken);
        return type is null ? NotFound() : Ok(ToResponse(type));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.EquipmentType.Create)]
    [ValidateCsrf]
    public async Task<IActionResult> Create([FromBody] CreateEquipmentTypeRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await equipmentTypeService.CreateAsync(
            new CreateEquipmentTypeData(request.Name, request.Kind, request.AssetTagPrefix), actorId, actorName, cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblem(result.ErrorCode, result.ErrorMessage!);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, ToResponse(result.Value));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.EquipmentType.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEquipmentTypeRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await equipmentTypeService.UpdateAsync(
            id, new UpdateEquipmentTypeData(request.Name, request.AssetTagPrefix), actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Permissions.EquipmentType.Archive)]
    [ValidateCsrf]
    public Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) => SetActiveAsync(id, true, cancellationToken);

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Permissions.EquipmentType.Archive)]
    [ValidateCsrf]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) => SetActiveAsync(id, false, cancellationToken);

    private async Task<IActionResult> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await equipmentTypeService.SetActiveAsync(id, active, actorId, actorName, cancellationToken);
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
            ServiceErrorCode.DuplicateName => StatusCodes.Status409Conflict,
            ServiceErrorCode.Concurrency => StatusCodes.Status409Conflict,
            ServiceErrorCode.Validation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest,
        };

        return Problem(title: message, statusCode: status);
    }

    private static EquipmentTypeResponse ToResponse(EquipmentTypeRecord type) =>
        new(type.Id, type.Name, type.Kind, type.AssetTagPrefix, type.IsActive, type.CreatedAtUtc, type.UpdatedAtUtc);
}
