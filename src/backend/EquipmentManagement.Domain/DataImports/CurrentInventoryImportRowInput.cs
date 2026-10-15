using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.Domain.DataImports;

/// <summary>
/// One Current Inventory import row's already-extracted cell values — one
/// row is one piece of Equipment, optionally with the Consultant who
/// currently holds it. Same mechanical/pure split as
/// <see cref="ConsultantImportRowInput"/>: ClosedXML-specific extraction
/// happens in Infrastructure, this is a plain data holder. Deliberately has
/// no PIN/PUK fields — those are never read, stored or shown.
/// </summary>
public sealed record CurrentInventoryImportRowInput(
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
    string? Imei,
    bool ImeiIsAmbiguousNumeric,
    string? MobileNumber,
    bool MobileNumberIsAmbiguousNumeric,
    string? SimSerialNumber,
    bool SimSerialNumberIsAmbiguousNumeric,
    string? FullName,
    string? Nif,
    bool NifIsAmbiguousNumeric,
    string? Email,
    string? Phone,
    bool PhoneIsAmbiguousNumeric,
    string? Address,
    string? WorkforceType,
    string? EngagementType,
    string? RoleOrFunction,
    string? CompanyName,
    DateOnly? AllocationStartDate,
    bool AllocationStartDateInvalid,
    DateOnly? AllocationEndDate,
    bool AllocationEndDateInvalid,
    DateOnly? AssignmentDate,
    bool AssignmentDateInvalid,
    IReadOnlySet<string> FormulaFields);

/// <summary>
/// Structurally-normalized values for one row, before any database lookup.
/// <see cref="HasConsultant"/> is true as soon as ANY person, allocation or
/// custody cell holds something — a row is only stock (unassigned) when all
/// of them are blank, so partially-filled person data is never silently
/// read as stock.
/// </summary>
public sealed record CurrentInventoryImportRowFields(
    string EquipmentTypeName,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? NormalizedSerialNumber,
    string? Notes,
    string? Processor,
    string? Memory,
    string? Storage,
    string? Imei,
    string? NormalizedImei,
    string? MobileNumber,
    string? SimSerialNumber,
    bool HasConsultant,
    string? FullName,
    string? Nif,
    string? Email,
    string? Phone,
    string? Address,
    WorkforceType? WorkforceType,
    EngagementType? EngagementType,
    string? RoleOrFunction,
    string? CompanyName,
    DateOnly? AllocationStartDate,
    DateOnly? AllocationEndDate,
    DateOnly? AssignmentDate);

/// <summary>What confirmation will do with the row's Consultant.</summary>
public enum CurrentInventoryConsultantAction
{
    /// <summary>Stock row: no Consultant.</summary>
    None,

    /// <summary>No Consultant has this NIF yet: one is created (once per NIF, however many rows carry it).</summary>
    Create,

    /// <summary>The Consultant with this NIF already exists and is reused — never updated.</summary>
    Reuse,
}

/// <summary>What confirmation will do with the row's Consultant Allocation.</summary>
public enum CurrentInventoryAllocationAction
{
    /// <summary>Stock row: no Allocation.</summary>
    None,

    /// <summary>The Consultant has no Active allocation: the workbook's allocation becomes it.</summary>
    Create,

    /// <summary>The Consultant's one Active allocation matches the workbook and is reused unchanged.</summary>
    Reuse,
}

/// <summary>
/// The finalized preview row — persisted in <see cref="ImportSession.RowsJson"/>
/// and returned in the preview response. The resolved ids and planned actions
/// are what confirmation re-checks against the live database (a preview is
/// never treated as a lock).
/// </summary>
public sealed record CurrentInventoryImportRowSnapshot(
    int RowNumber,
    ImportRowStatus Status,
    string EquipmentTypeName,
    Guid? EquipmentTypeId,
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
    bool HasConsultant,
    string? FullName,
    string? Nif,
    string? Email,
    string? Phone,
    string? Address,
    WorkforceType? WorkforceType,
    EngagementType? EngagementType,
    string? RoleOrFunction,
    string? CompanyName,
    Guid? CompanyId,
    DateOnly? AllocationStartDate,
    DateOnly? AllocationEndDate,
    DateOnly? AssignmentDate,
    CurrentInventoryConsultantAction ConsultantAction,
    Guid? ExistingConsultantId,
    CurrentInventoryAllocationAction AllocationAction,
    Guid? ExistingAllocationId,
    IReadOnlyList<ImportIssue> Issues);
