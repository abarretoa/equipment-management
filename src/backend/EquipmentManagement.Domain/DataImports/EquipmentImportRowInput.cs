namespace EquipmentManagement.Domain.DataImports;

/// <summary>Already-extracted cell values for one Equipment-import row — same mechanical/pure split as <see cref="ConsultantImportRowInput"/>.</summary>
public sealed record EquipmentImportRowInput(
    int RowNumber,
    string? EquipmentTypeName,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    bool SerialNumberIsAmbiguousNumeric,
    string? Notes,
    string? Processor,
    string? Memory,
    string? Storage,
    IReadOnlySet<string> FormulaFields);

/// <summary>Structurally-normalized field values, before EquipmentType lookup/Kind-dependent rules are applied.</summary>
public sealed record EquipmentImportRowFields(
    string EquipmentTypeName,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? NormalizedSerialNumber,
    string? Notes,
    string? Processor,
    string? Memory,
    string? Storage);

/// <summary>The finalized preview row — persisted in <see cref="ImportSession.RowsJson"/> and returned in the preview response.</summary>
public sealed record EquipmentImportRowSnapshot(
    int RowNumber,
    ImportRowStatus Status,
    string EquipmentTypeName,
    Guid? EquipmentTypeId,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? Notes,
    string? Processor,
    string? Memory,
    string? Storage,
    IReadOnlyList<ImportIssue> Issues);
