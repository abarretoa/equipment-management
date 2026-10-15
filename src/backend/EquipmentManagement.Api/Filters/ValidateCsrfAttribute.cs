using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.Api.Filters;

/// <summary>
/// CSRF validation for the double-submit strategy (ADR-004), applied to every
/// state-changing Auth/Users/etc. action. This project deliberately calls
/// <c>AddControllers()</c> (a pure JSON API, no Razor views) rather than
/// <c>AddControllersWithViews()</c>, so the built-in
/// <c>[ValidateAntiForgeryToken]</c> MVC attribute cannot be used as-is — it
/// depends on a filter type that only <c>AddControllersWithViews()</c> registers,
/// and that type is internal to ASP.NET Core so it cannot be registered directly
/// either. Calling <see cref="IAntiforgery.ValidateRequestAsync"/> ourselves is
/// the documented alternative for API-only projects, and additionally gives a
/// predictable ProblemDetails body on failure instead of an empty 400.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ValidateCsrfAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new BadRequestObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "CSRF token missing or invalid.",
            });
        }
    }
}
