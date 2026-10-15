using System.Security.Claims;
using EquipmentManagement.Api.Contracts.DataImports;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.DataImports;
using EquipmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// Consultant Excel import: template, preview, and confirmation (Phase 8 scope). Uploading a file only ever previews it — nothing is
/// persisted until an explicit, separate confirmation of the resulting
/// <see cref="ImportSession"/> (Phase 8 scope).
/// </summary>
[ApiController]
[Route("api/imports/consultants")]
public sealed class ConsultantImportsController(ConsultantImportService importService, UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet("template")]
    [Authorize(Policy = Permissions.Consultant.Import)]
    public IActionResult DownloadTemplate()
    {
        var bytes = ConsultantImportService.GenerateTemplate();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Modelo_Importacao_Consultores.xlsx");
    }

    [HttpPost("preview")]
    [Authorize(Policy = Permissions.Consultant.Import)]
    [ValidateCsrf]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Preview(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return Problem(title: "Não foi carregado nenhum ficheiro.", statusCode: StatusCodes.Status400BadRequest);
        }

        var (actorId, actorName) = await GetActorAsync();
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        var result = await importService.PreviewAsync(buffer.ToArray(), file.FileName, actorId, actorName, cancellationToken);
        return result.Succeeded ? Ok(ToResponse(result.Value!)) : ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpGet("{sessionId:guid}")]
    [Authorize(Policy = Permissions.Consultant.Import)]
    public async Task<IActionResult> Get(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await importService.GetAsync(sessionId, cancellationToken);
        return session is null ? NotFound() : Ok(ToResponse(session));
    }

    [HttpPost("{sessionId:guid}/confirm")]
    [Authorize(Policy = Permissions.Consultant.Import)]
    [ValidateCsrf]
    public async Task<IActionResult> Confirm(Guid sessionId, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var (result, conflictRows) = await importService.ConfirmAsync(sessionId, actorId, actorName, cancellationToken);

        if (result.Succeeded)
        {
            return Ok(new ConsultantImportConfirmResponse(
                result.Value!.ImportSessionId, result.Value.Status, result.Value.CreatedConsultants, result.Value.CreatedAllocations, result.Value.CompletedAtUtc));
        }

        if (conflictRows is not null)
        {
            return Conflict(new ConsultantImportStaleConflictResponse(result.ErrorMessage!, conflictRows.Select(ToRowResponse).ToList()));
        }

        return ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpPost("{sessionId:guid}/cancel")]
    [Authorize(Policy = Permissions.Consultant.Import)]
    [ValidateCsrf]
    public async Task<IActionResult> Cancel(Guid sessionId, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var result = await importService.CancelAsync(sessionId, actorId, actorName, cancellationToken);
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
            ServiceErrorCode.UnsupportedWorkbook => StatusCodes.Status400BadRequest,
            ServiceErrorCode.WorkbookTooLarge => StatusCodes.Status400BadRequest,
            ServiceErrorCode.InvalidWorkbookStructure => StatusCodes.Status400BadRequest,
            ServiceErrorCode.MissingWorksheet => StatusCodes.Status400BadRequest,
            ServiceErrorCode.MissingRequiredColumn => StatusCodes.Status400BadRequest,
            ServiceErrorCode.ImportPreviewHasErrors => StatusCodes.Status409Conflict,
            ServiceErrorCode.ImportSessionNotConfirmable => StatusCodes.Status409Conflict,
            ServiceErrorCode.ImportSessionExpired => StatusCodes.Status409Conflict,
            ServiceErrorCode.ImportPreviewStale => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        return Problem(title: message, statusCode: status);
    }

    private static ConsultantImportPreviewResponse ToResponse(ConsultantImportPreviewRecord record) => new(
        record.Id, record.FileName, record.FileSha256, record.Status, record.TotalRows, record.ValidRows,
        record.WarningRows, record.ErrorRows, record.CreatedAtUtc, record.Rows.Select(ToRowResponse).ToList());

    private static ConsultantImportRowResponse ToRowResponse(ConsultantImportRowSnapshot row) => new(
        row.RowNumber, row.Status, row.FullName, row.Nif, row.Email, row.Address, row.Phone, row.HasAllocation,
        row.WorkforceType, row.EngagementType, row.RoleOrFunction, row.CompanyName, row.AllocationStartDate, row.AllocationEndDate,
        row.Issues.Select(i => new ImportIssueResponse(i.Code, i.Field, i.Message, i.Severity)).ToList());
}
