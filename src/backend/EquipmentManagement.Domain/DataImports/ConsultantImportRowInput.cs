using EquipmentManagement.Domain.Allocations;

namespace EquipmentManagement.Domain.DataImports;

/// <summary>
/// One Consultant-import row's already-extracted cell values — mechanical
/// Excel-specific extraction (date parsing, formula detection, numeric-vs-text
/// ambiguity) happens in the Infrastructure Excel reader; this type is a
/// plain data holder so <c>ConsultantImportRowValidator</c> (Infrastructure) stays
/// framework-free and unit-testable without ClosedXML.
/// </summary>
public sealed record ConsultantImportRowInput(
    int RowNumber,
    string? FullName,
    string? Nif,
    bool NifIsAmbiguousNumeric,
    string? Email,
    string? Address,
    string? Phone,
    bool PhoneIsAmbiguousNumeric,
    string? WorkforceType,
    string? EngagementType,
    string? RoleOrFunction,
    string? CompanyName,
    DateOnly? AllocationStartDate,
    bool AllocationStartDateInvalid,
    DateOnly? AllocationEndDate,
    bool AllocationEndDateInvalid,
    IReadOnlySet<string> FormulaFields);

/// <summary>Structurally-normalized field values for one row — assembled by <c>ConsultantImportRowValidator</c> (Infrastructure), before any database-dependent check is applied.</summary>
public sealed record ConsultantImportRowFields(
    string FullName,
    string Nif,
    string Email,
    string Address,
    string Phone,
    bool HasAllocation,
    WorkforceType? WorkforceType,
    EngagementType? EngagementType,
    string? RoleOrFunction,
    string? CompanyName,
    DateOnly? AllocationStartDate,
    DateOnly? AllocationEndDate);

/// <summary>The finalized preview row — persisted in <see cref="ImportSession.RowsJson"/> and returned in the preview response.</summary>
public sealed record ConsultantImportRowSnapshot(
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
    string? CompanyName,
    Guid? CompanyId,
    DateOnly? AllocationStartDate,
    DateOnly? AllocationEndDate,
    IReadOnlyList<ImportIssue> Issues);
