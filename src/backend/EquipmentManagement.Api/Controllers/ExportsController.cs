using System.Security.Claims;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Consultants;
using EquipmentManagement.Infrastructure.DataImports;
using EquipmentManagement.Infrastructure.Equipments;
using EquipmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// Consultant/Equipment Excel export (Phase 8 scope).
/// Filters use the exact same meaning as the corresponding list endpoints —
/// no separate query language (Phase 8 scope). Both routes are
/// PII-sensitive (Consultant export especially: NIF/address/phone/email) and
/// require the dedicated Export permission, never a public/anonymous route.
/// </summary>
[ApiController]
[Route("api/exports")]
public sealed class ExportsController(
    ConsultantExportService consultantExportService, EquipmentExportService equipmentExportService, UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet("consultants")]
    [Authorize(Policy = Permissions.Consultant.Export)]
    public async Task<IActionResult> ExportConsultants(
        [FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] WorkforceType? workforceType,
        [FromQuery] Guid? companyId, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var bytes = await consultantExportService.ExportAsync(
            new ConsultantListFilter(search, isActive, null, workforceType, companyId), actorId, actorName, cancellationToken);

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", BuildFileName("Consultores"));
    }

    [HttpGet("equipment")]
    [Authorize(Policy = Permissions.Equipment.Export)]
    public async Task<IActionResult> ExportEquipment(
        [FromQuery] string? search, [FromQuery] EquipmentStatus? status, [FromQuery] Guid? equipmentTypeId,
        [FromQuery] string? manufacturer, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var bytes = await equipmentExportService.ExportAsync(
            new EquipmentListFilter(search, status, equipmentTypeId, manufacturer), actorId, actorName, cancellationToken);

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", BuildFileName("Equipamentos"));
    }

    private async Task<(Guid Id, string DisplayName)> GetActorAsync()
    {
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var actor = await userManager.FindByIdAsync(id.ToString());
        return (id, actor is null ? id.ToString() : $"{actor.FirstName} {actor.LastName}");
    }

    /// <summary>
    /// UTC date only — never PII in the filename (Phase 8 scope).
    /// pt-PT but ASCII-only, so the name survives any browser's
    /// Content-Disposition handling unchanged (ADR-070).
    /// </summary>
    private static string BuildFileName(string subject) => $"{subject}_{DateTime.UtcNow:yyyy-MM-dd}.xlsx";
}
