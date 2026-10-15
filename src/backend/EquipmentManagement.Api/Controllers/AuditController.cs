using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// Read-only audit query API, Administration only (docs/PROJECT_SPEC.md, section
/// 15: "a auditoria será visível apenas para Diretoria/Administração"). HR gets a
/// 403 from the <see cref="Permissions.Audit.Read"/> policy, the same as any other
/// caller without that permission — there is no HR-specific check anywhere here,
/// consistent with ADR-005 (permission claims, not role checks in controllers).
/// No write/update/delete action exists on this controller by design.
/// </summary>
[ApiController]
[Route("api/audit")]
[Authorize(Policy = Permissions.Audit.Read)]
public sealed class AuditController(AuditQueryService auditQueryService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        // A value that binds without a timezone (a bare "2026-10-11", or a
        // zone-less "2026-10-11T00:00:00") is ambiguous and would reach the
        // timestamptz query as an Unspecified DateTime, which the database
        // driver rejects. Refuse it here, before querying: only instants with
        // an explicit offset or "Z" are accepted (malformed values are already
        // rejected by model binding with the same 400 shape).
        RejectZoneLessDate(nameof(fromUtc), fromUtc);
        RejectZoneLessDate(nameof(toUtc), toUtc);
        if (!ModelState.IsValid)
        {
            return ValidationProblem();
        }

        var result = await auditQueryService.QueryAsync(
            new AuditQueryFilter(actorUserId, action, entityType, entityId, fromUtc, toUtc), page, pageSize, cancellationToken);

        return Ok(new PagedResponse<AuditLogResponse>(
            result.Items.Select(ToResponse).ToList(), result.Page, result.PageSize, result.TotalCount));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var log = await auditQueryService.GetByIdAsync(id, cancellationToken);
        return log is null ? NotFound() : Ok(ToResponse(log));
    }

    private void RejectZoneLessDate(string field, DateTime? value)
    {
        if (value is { Kind: not DateTimeKind.Utc })
        {
            ModelState.AddModelError(field, $"{field} deve ser uma data/hora válida com fuso horário explícito (por exemplo 2026-10-10T23:00:00.000Z).");
        }
    }

    private static AuditLogResponse ToResponse(AuditLog log) => new(
        log.Id, log.ActorUserId, log.ActorDisplayNameSnapshot, log.Action, log.EntityType, log.EntityId,
        log.OccurredAtUtc, log.CorrelationId, log.IpAddress, log.OldValuesJson, log.NewValuesJson, log.MetadataJson);
}
