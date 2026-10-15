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
/// Current Inventory import: template, preview, and confirmation of
/// Consultants + Allocations + Equipment + current custody from one workbook.
/// Uploading only previews — nothing is persisted until an explicit, separate
/// confirmation of the resulting <see cref="ImportSession"/>.
///
/// Requires ALL of <c>Consultant.Import</c>, <c>Equipment.Import</c> and
/// <c>Equipment.Assign</c> (stacked policies are AND-ed): it creates
/// Consultants/Allocations, creates Equipment, and establishes custody.
/// Administration and HR hold all three; no new permission or role exists
/// for this workflow. The Consultant and Equipment import endpoints are
/// unchanged and remain available on their own.
/// </summary>
[ApiController]
[Route("api/imports/current-inventory")]
[Authorize(Policy = Permissions.Consultant.Import)]
[Authorize(Policy = Permissions.Equipment.Import)]
[Authorize(Policy = Permissions.Equipment.Assign)]
public sealed class CurrentInventoryImportsController(CurrentInventoryImportService importService, UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet("template")]
    public async Task<IActionResult> DownloadTemplate(CancellationToken cancellationToken)
    {
        var bytes = await importService.GenerateTemplateAsync(cancellationToken);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", CurrentInventoryImportService.TemplateFileName);
    }

    [HttpPost("preview")]
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
    public async Task<IActionResult> Get(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await importService.GetAsync(sessionId, cancellationToken);
        return session is null ? NotFound() : Ok(ToResponse(session));
    }

    [HttpPost("{sessionId:guid}/confirm")]
    [ValidateCsrf]
    public async Task<IActionResult> Confirm(Guid sessionId, CancellationToken cancellationToken)
    {
        var (actorId, actorName) = await GetActorAsync();
        var (result, conflictRows) = await importService.ConfirmAsync(sessionId, actorId, actorName, cancellationToken);

        if (result.Succeeded)
        {
            var s = result.Value!;
            return Ok(new CurrentInventoryImportConfirmResponse(
                s.ImportSessionId, s.Status, s.CreatedConsultants, s.ReusedConsultants, s.CreatedAllocations, s.ReusedAllocations,
                s.CreatedEquipment, s.CreatedAssignments, s.CompletedAtUtc));
        }

        if (conflictRows is not null)
        {
            return Conflict(new CurrentInventoryImportStaleConflictResponse(result.ErrorMessage!, conflictRows.Select(ToRowResponse).ToList()));
        }

        return ToProblem(result.ErrorCode, result.ErrorMessage!);
    }

    [HttpPost("{sessionId:guid}/cancel")]
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

    private static CurrentInventoryImportPreviewResponse ToResponse(CurrentInventoryImportPreviewRecord record) => new(
        record.Id, record.FileName, record.FileSha256, record.Status, record.TotalRows, record.ValidRows,
        record.WarningRows, record.ErrorRows, record.CreatedAtUtc,
        new CurrentInventoryImportSummaryResponse(
            record.Summary.NewConsultants, record.Summary.ReusedConsultants, record.Summary.NewAllocations, record.Summary.ReusedAllocations,
            record.Summary.EquipmentToCreate, record.Summary.EquipmentToAssign, record.Summary.EquipmentToStayAvailable,
            record.Summary.InternalConsultants, record.Summary.ExternalConsultants),
        record.Rows.Select(ToRowResponse).ToList());

    private static CurrentInventoryImportRowResponse ToRowResponse(CurrentInventoryImportRowSnapshot row) => new(
        row.RowNumber, row.Status, row.EquipmentTypeName, row.EquipmentTypeKind, row.Manufacturer, row.Model, row.SerialNumber, row.Notes,
        row.Processor, row.Memory, row.Storage, row.Imei, row.MobileNumber, row.SimSerialNumber,
        row.HasConsultant, row.FullName, row.Nif, row.Email, row.Phone, row.Address, row.WorkforceType, row.EngagementType,
        row.RoleOrFunction, row.CompanyName, row.AllocationStartDate, row.AllocationEndDate, row.AssignmentDate,
        row.ConsultantAction, row.AllocationAction,
        row.Issues.Select(i => new ImportIssueResponse(i.Code, i.Field, i.Message, i.Severity)).ToList());
}
