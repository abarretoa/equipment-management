using System.Security.Claims;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Users;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// Administration-only internal-user management
/// (Phase 1 scope). Every action
/// is gated by a specific permission policy — never a role check — per
/// docs/ARCHITECTURE_DECISIONS.md, ADR-005. All business rules (duplicate email,
/// HR address requirement, last-Administration safeguard, concurrency) live in
/// <see cref="UserManagementService"/>, not here.
/// </summary>
[ApiController]
[Route("api/users")]
public sealed class UsersController(UserManagementService userManagementService, UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.User.Read)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? role,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await userManagementService.ListAsync(
            new UserListFilter(search, role, isActive), page, pageSize, cancellationToken);

        return Ok(new PagedResponse<UserResponse>(
            result.Items.Select(ToResponse).ToList(), result.Page, result.PageSize, result.TotalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.User.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var user = await userManagementService.GetByIdAsync(id, cancellationToken);
        return user is null ? NotFound() : Ok(ToResponse(user));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.User.Create)]
    [ValidateCsrf]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await userManagementService.CreateAsync(
            new CreateUserData(request.FirstName, request.LastName, request.Email, request.Phone, request.Role, request.Address),
            actorId, actorName, cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblem(result.ErrorCode, result.ErrorMessage!);
        }

        var created = result.Value!;
        return CreatedAtAction(nameof(GetById), new { id = created.User.Id },
            new CreatedUserResponse(ToResponse(created.User), created.TemporaryPassword));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.User.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await userManagementService.UpdateAsync(
            id, new UpdateUserData(request.FirstName, request.LastName, request.Phone, request.Address, request.ConcurrencyStamp),
            actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpPut("{id:guid}/role")]
    [Authorize(Policy = Permissions.User.AssignRole)]
    [ValidateCsrf]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangeUserRoleRequest request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await userManagementService.ChangeRoleAsync(
            id, new ChangeRoleData(request.Role, request.ConcurrencyStamp), actorId, actorName, cancellationToken);

        return result.Succeeded ? Ok(ToResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Permissions.User.Archive)]
    [ValidateCsrf]
    public Task<IActionResult> Activate(Guid id, [FromBody] ActivationRequest? request, CancellationToken cancellationToken) =>
        SetActiveAsync(id, active: true, request, cancellationToken);

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Permissions.User.Archive)]
    [ValidateCsrf]
    public Task<IActionResult> Deactivate(Guid id, [FromBody] ActivationRequest? request, CancellationToken cancellationToken) =>
        SetActiveAsync(id, active: false, request, cancellationToken);

    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Policy = Permissions.User.Update)]
    [ValidateCsrf]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await userManagementService.InitiateAdminPasswordResetAsync(id, actorId, actorName, cancellationToken);

        return result.Succeeded
            ? Ok(new ResetPasswordResponse(result.Value!))
            : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    private async Task<IActionResult> SetActiveAsync(Guid id, bool active, ActivationRequest? request, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await userManagementService.SetActiveAsync(
            id, active, new ActivationChangeData(request?.ConcurrencyStamp), actorId, actorName, cancellationToken);

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
            ServiceErrorCode.DuplicateEmail => StatusCodes.Status409Conflict,
            ServiceErrorCode.LastAdministrator => StatusCodes.Status409Conflict,
            ServiceErrorCode.Concurrency => StatusCodes.Status409Conflict,
            ServiceErrorCode.UnsupportedRole => StatusCodes.Status400BadRequest,
            ServiceErrorCode.AddressRequiredForRole => StatusCodes.Status400BadRequest,
            ServiceErrorCode.Validation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest,
        };

        return Problem(title: message, statusCode: status);
    }

    private static UserResponse ToResponse(UserRecord user) => new(
        user.Id, user.FirstName, user.LastName, user.Email, user.PhoneNumber, user.Address,
        user.Role, user.IsActive, user.MustChangePassword, user.ConcurrencyStamp);
}
