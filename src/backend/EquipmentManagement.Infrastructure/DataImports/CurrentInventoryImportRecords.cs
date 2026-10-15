using EquipmentManagement.Domain.DataImports;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// What confirming the previewed workbook will do, computed from the rows'
/// planned actions. Consultant and allocation counts are per person (per
/// NIF), not per row — one person on three rows is one consultant. Rows with
/// errors are left out: they cannot be confirmed anyway, and counting them
/// would describe a plan that cannot happen.
/// </summary>
public sealed record CurrentInventoryImportSummary(
    int NewConsultants,
    int ReusedConsultants,
    int NewAllocations,
    int ReusedAllocations,
    int EquipmentToCreate,
    int EquipmentToAssign,
    int EquipmentToStayAvailable,
    int InternalConsultants,
    int ExternalConsultants);

public sealed record CurrentInventoryImportPreviewRecord(
    Guid Id,
    string FileName,
    string FileSha256,
    ImportSessionStatus Status,
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int ErrorRows,
    DateTime CreatedAtUtc,
    CurrentInventoryImportSummary Summary,
    IReadOnlyList<CurrentInventoryImportRowSnapshot> Rows);

public sealed record CurrentInventoryImportConfirmationSummary(
    Guid ImportSessionId,
    ImportSessionStatus Status,
    int CreatedConsultants,
    int ReusedConsultants,
    int CreatedAllocations,
    int ReusedAllocations,
    int CreatedEquipment,
    int CreatedAssignments,
    DateTime CompletedAtUtc);
