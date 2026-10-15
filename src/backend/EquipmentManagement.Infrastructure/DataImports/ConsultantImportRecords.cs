using EquipmentManagement.Domain.DataImports;

namespace EquipmentManagement.Infrastructure.DataImports;

public sealed record ConsultantImportPreviewRecord(
    Guid Id,
    string FileName,
    string FileSha256,
    ImportSessionStatus Status,
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int ErrorRows,
    DateTime CreatedAtUtc,
    IReadOnlyList<ConsultantImportRowSnapshot> Rows);

public sealed record ConsultantImportConfirmationSummary(
    Guid ImportSessionId, ImportSessionStatus Status, int CreatedConsultants, int CreatedAllocations, DateTime CompletedAtUtc);
