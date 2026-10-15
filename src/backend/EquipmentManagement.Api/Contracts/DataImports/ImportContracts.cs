using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.Api.Contracts.DataImports;

public sealed record ImportIssueResponse(string Code, string Field, string Message, ImportIssueSeverity Severity);

/// <summary>
/// Never <c>CompanyId</c> — this is a data-minimized operational preview, not
/// a raw entity dump (Phase 8 scope). Values shown are the
/// normalized values that will actually be persisted on confirmation.
/// </summary>
public sealed record ConsultantImportRowResponse(
    int RowNumber,
    ImportRowStatus Status,
    string FullName,
    string Nif,
    string Email,
    string Address,
    string Phone,
    bool HasAllocation,
    WorkforceType? WorkforceType,
    EngagementType? EngagementType,
    string? RoleOrFunction,
    string? Company,
    DateOnly? AllocationStartDate,
    DateOnly? AllocationEndDate,
    IReadOnlyList<ImportIssueResponse> Issues);

public sealed record ConsultantImportPreviewResponse(
    Guid ImportSessionId,
    string FileName,
    string FileSha256,
    ImportSessionStatus Status,
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int ErrorRows,
    DateTime CreatedAtUtc,
    IReadOnlyList<ConsultantImportRowResponse> Rows);

public sealed record ConsultantImportConfirmResponse(
    Guid ImportSessionId, ImportSessionStatus Status, int CreatedConsultants, int CreatedAllocations, DateTime CompletedAtUtc);

public sealed record ConsultantImportStaleConflictResponse(string Title, IReadOnlyList<ConsultantImportRowResponse> Rows);

public sealed record EquipmentImportRowResponse(
    int RowNumber,
    ImportRowStatus Status,
    string EquipmentType,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? Notes,
    string? Processor,
    string? Memory,
    string? Storage,
    IReadOnlyList<ImportIssueResponse> Issues);

public sealed record EquipmentImportPreviewResponse(
    Guid ImportSessionId,
    string FileName,
    string FileSha256,
    ImportSessionStatus Status,
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int ErrorRows,
    DateTime CreatedAtUtc,
    IReadOnlyList<EquipmentImportRowResponse> Rows);

public sealed record EquipmentImportConfirmResponse(Guid ImportSessionId, ImportSessionStatus Status, int CreatedEquipment, DateTime CompletedAtUtc);

public sealed record EquipmentImportStaleConflictResponse(string Title, IReadOnlyList<EquipmentImportRowResponse> Rows);

/// <summary>
/// One Current Inventory row — one piece of Equipment and, when
/// <see cref="WillBeAssigned"/>, the person who currently holds it. Same
/// data-minimization as the other previews: no internal ids (consultant,
/// allocation, company, type), only the normalized values that will be
/// persisted and the planned create/reuse decisions. PIN/PUK do not exist here.
/// </summary>
public sealed record CurrentInventoryImportRowResponse(
    int RowNumber,
    ImportRowStatus Status,
    string EquipmentType,
    EquipmentTypeKind? EquipmentTypeKind,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? Notes,
    string? Processor,
    string? Memory,
    string? Storage,
    string? Imei,
    string? MobileNumber,
    string? SimSerialNumber,
    bool WillBeAssigned,
    string? FullName,
    string? Nif,
    string? Email,
    string? Phone,
    string? Address,
    WorkforceType? WorkforceType,
    EngagementType? EngagementType,
    string? RoleOrFunction,
    string? Company,
    DateOnly? AllocationStartDate,
    DateOnly? AllocationEndDate,
    DateOnly? AssignmentDate,
    CurrentInventoryConsultantAction ConsultantAction,
    CurrentInventoryAllocationAction AllocationAction,
    IReadOnlyList<ImportIssueResponse> Issues);

/// <summary>What confirmation will do — consultant/allocation counts are per person (NIF), not per row; rows with errors are not counted.</summary>
public sealed record CurrentInventoryImportSummaryResponse(
    int NewConsultants,
    int ReusedConsultants,
    int NewAllocations,
    int ReusedAllocations,
    int EquipmentToCreate,
    int EquipmentToAssign,
    int EquipmentToStayAvailable,
    int InternalConsultants,
    int ExternalConsultants);

public sealed record CurrentInventoryImportPreviewResponse(
    Guid ImportSessionId,
    string FileName,
    string FileSha256,
    ImportSessionStatus Status,
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int ErrorRows,
    DateTime CreatedAtUtc,
    CurrentInventoryImportSummaryResponse Summary,
    IReadOnlyList<CurrentInventoryImportRowResponse> Rows);

public sealed record CurrentInventoryImportConfirmResponse(
    Guid ImportSessionId,
    ImportSessionStatus Status,
    int CreatedConsultants,
    int ReusedConsultants,
    int CreatedAllocations,
    int ReusedAllocations,
    int CreatedEquipment,
    int CreatedAssignments,
    DateTime CompletedAtUtc);

public sealed record CurrentInventoryImportStaleConflictResponse(string Title, IReadOnlyList<CurrentInventoryImportRowResponse> Rows);
