using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// Issues the CSRF token pair for the double-submit strategy used by every future
/// state-changing endpoint (docs/PROJECT_SPEC.md section 22: "proteção CSRF quando
/// aplicável"). The frontend calls this once on startup, then sends the returned
/// token back as the X-CSRF-TOKEN header on every POST/PUT/PATCH/DELETE; the
/// matching half is stored in a cookie ASP.NET Core sets automatically here.
/// Pure plumbing — no business logic, so it belongs in Phase 0.
/// </summary>
[ApiController]
[Route("api/antiforgery")]
public sealed class AntiforgeryController(IAntiforgery antiforgery) : ControllerBase
{
    /// <summary>
    /// Must stay reachable before login (the frontend needs a token to even send
    /// the login POST) — explicit now that the authorization fallback policy
    /// (Phase 10 hardening) requires authentication by default on anything
    /// without an attribute saying otherwise.
    /// </summary>
    [HttpGet("token")]
    [AllowAnonymous]
    public IActionResult GetToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }
}
