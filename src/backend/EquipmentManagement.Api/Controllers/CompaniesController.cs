using System.Security.Claims;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Companies;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Companies;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// Normalized external-company reference data
/// (Phase 2 scope). HR can read, create and update companies; archiving is
/// Administration-only (ADR-024).
/// </summary>
[ApiController]
[Route("api/companies")]
public sealed class CompaniesController(CompanyService companyService, UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.Company.Read)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await companyService.ListAsync(new CompanyListFilter(search, isActive), page, pageSize, cancellationToken);
        return Ok(new PagedResponse<CompanyResponse>(result.Items.Select(ToResponse).ToList(), result.Page, result.PageSize, result.TotalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Company.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var company = await companyService.GetByIdAsync(id, cancellationToken);
        return company is null ? NotFound() : Ok(ToResponse(company));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Company.Create)]
    [ValidateCsrf]
    public async Task<IActionResult> Create([FromBody] CreateCompanyRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await companyService.CreateAsync(new CreateCompanyData(request.Name), actorId, actorName, cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblem(result.ErrorCode, result.ErrorMessage!);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, ToResponse(result.Value));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Company.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCompanyRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await companyService.UpdateAsync(id, new UpdateCompanyData(request.Name), actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Permissions.Company.Archive)]
    [ValidateCsrf]
    public Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) => SetActiveAsync(id, true, cancellationToken);

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Permissions.Company.Archive)]
    [ValidateCsrf]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) => SetActiveAsync(id, false, cancellationToken);

    private async Task<IActionResult> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await companyService.SetActiveAsync(id, active, actorId, actorName, cancellationToken);
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

    private static CompanyResponse ToResponse(CompanyRecord company) =>
        new(company.Id, company.Name, company.IsActive, company.CreatedAtUtc, company.UpdatedAtUtc);
}
