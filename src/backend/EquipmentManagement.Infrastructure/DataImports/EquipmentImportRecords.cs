using EquipmentManagement.Domain.DataImports;

namespace EquipmentManagement.Infrastructure.DataImports;

public sealed record EquipmentImportPreviewRecord(
    Guid Id,
    string FileName,
    string FileSha256,
    ImportSessionStatus Status,
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int ErrorRows,
    DateTime CreatedAtUtc,
    IReadOnlyList<EquipmentImportRowSnapshot> Rows);

public sealed record EquipmentImportConfirmationSummary(
    Guid ImportSessionId, ImportSessionStatus Status, int CreatedEquipment, DateTime CompletedAtUtc);
