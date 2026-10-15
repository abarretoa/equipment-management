using System.Security.Claims;
using EquipmentManagement.Api.Contracts.Profile;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// "Meu perfil": the signed-in user reads and edits their own name, phone and
/// address (ADR-073). Any authenticated internal user — Administration or HR —
/// may use it; there is no permission policy because this is not an
/// administrative capability. The target user is always resolved from the auth
/// cookie's <see cref="ClaimTypes.NameIdentifier"/>: no route parameter or body
/// field can name another account, so there is nothing to IDOR. Password changes
/// stay on <c>POST /api/auth/change-password</c>.
/// </summary>
[ApiController]
[Route("api/profile")]
[Authorize]
public sealed class ProfileController(UserManagementService userManagementService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var user = await userManagementService.GetByIdAsync(GetUserId(), cancellationToken);
        return user is null ? Unauthorized() : Ok(ToResponse(user));
    }

    [HttpPut]
    [ValidateCsrf]
    public async Task<IActionResult> Update([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await userManagementService.UpdateOwnProfileAsync(
            GetUserId(),
            new UpdateOwnProfileData(request.FirstName, request.LastName, request.Phone, request.Address),
            cancellationToken);

        if (result.Succeeded)
        {
            return Ok(ToResponse(result.Value!));
        }

        switch (result.ErrorCode)
        {
            case ServiceErrorCode.NotFound:
                return Unauthorized();
            case ServiceErrorCode.AddressRequiredForRole:
                // Field-keyed, in the same ValidationProblemDetails shape as the
                // request's own [Required]/[Phone]/[MaxLength] failures, so the
                // client can show it on the Address field.
                ModelState.AddModelError(nameof(UpdateProfileRequest.Address), result.ErrorMessage!);
                return ValidationProblem();
            case ServiceErrorCode.Concurrency:
                return Problem(title: result.ErrorMessage, statusCode: StatusCodes.Status409Conflict);
            default:
                return Problem(title: result.ErrorMessage, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static ProfileResponse ToResponse(UserRecord user) => new(
        user.FirstName, user.LastName, user.Email, user.PhoneNumber, user.Address, user.Role);
}
