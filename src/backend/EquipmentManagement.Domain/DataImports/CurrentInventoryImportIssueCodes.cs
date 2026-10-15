namespace EquipmentManagement.Domain.DataImports;

/// <summary>
/// Stable, machine-readable Current Inventory import issue codes — never a
/// raw database constraint name, and never shown to HR as the primary text
/// (each issue also carries its pt-PT message).
/// </summary>
public static class CurrentInventoryImportIssueCodes
{
    // --- Row / cell mechanics ---
    public const string FormulaNotAllowed = "FormulaNotAllowed";
    public const string CellTooLong = "CellTooLong";
    public const string NumericIdentifierRejected = "NumericIdentifierRejected";

    // --- Equipment ---
    public const string MissingEquipmentType = "MissingEquipmentType";
    public const string UnknownEquipmentType = "UnknownEquipmentType";
    public const string InactiveEquipmentType = "InactiveEquipmentType";
    public const string EquipmentTypeChanged = "EquipmentTypeChanged";
    public const string MissingManufacturer = "MissingManufacturer";
    public const string MissingLaptopSerial = "MissingLaptopSerial";
    public const string MissingLaptopProcessor = "MissingLaptopProcessor";
    public const string MissingLaptopMemory = "MissingLaptopMemory";
    public const string MissingLaptopStorage = "MissingLaptopStorage";
    public const string LaptopFieldsNotAllowed = "LaptopFieldsNotAllowed";
    public const string MissingImei = "MissingImei";
    public const string InvalidImei = "InvalidImei";
    public const string MobileFieldsNotAllowed = "MobileFieldsNotAllowed";
    public const string DuplicateSerialInFile = "DuplicateSerialInFile";
    public const string DuplicateSerialExisting = "DuplicateSerialExisting";
    public const string DuplicateImeiInFile = "DuplicateImeiInFile";
    public const string DuplicateImeiExisting = "DuplicateImeiExisting";

    // --- Consultant (assigned rows) ---
    public const string MissingFullName = "MissingFullName";
    public const string InvalidNif = "InvalidNif";
    public const string MissingEmail = "MissingEmail";
    public const string InvalidEmail = "InvalidEmail";
    public const string MissingPhone = "MissingPhone";
    public const string InvalidPhone = "InvalidPhone";
    public const string MissingAddress = "MissingAddress";
    public const string InconsistentConsultantInFile = "InconsistentConsultantInFile";
    public const string ExistingConsultantMismatch = "ExistingConsultantMismatch";
    public const string ExistingConsultantContactDiffers = "ExistingConsultantContactDiffers";
    public const string ConsultantArchived = "ConsultantArchived";
    public const string ConsultantChanged = "ConsultantChanged";

    // --- Allocation (assigned rows) ---
    public const string MissingWorkforceType = "MissingWorkforceType";
    public const string InvalidWorkforceType = "InvalidWorkforceType";
    public const string MissingEngagementType = "MissingEngagementType";
    public const string InvalidEngagementType = "InvalidEngagementType";
    public const string MissingRoleOrFunction = "MissingRoleOrFunction";
    public const string MissingCompany = "MissingCompany";
    public const string CompanyNotAllowedForInternal = "CompanyNotAllowedForInternal";
    public const string UnknownCompany = "UnknownCompany";
    public const string InactiveCompany = "InactiveCompany";
    public const string MissingAllocationStartDate = "MissingAllocationStartDate";
    public const string InvalidDate = "InvalidDate";
    public const string AllocationEndBeforeStart = "AllocationEndBeforeStart";
    public const string ExistingRegimeConflict = "ExistingRegimeConflict";
    public const string ExistingAllocationConflict = "ExistingAllocationConflict";
    public const string MultipleActiveAllocations = "MultipleActiveAllocations";
    public const string AllocationChanged = "AllocationChanged";

    // --- Custody ---
    public const string AssignmentDateInFuture = "AssignmentDateInFuture";
}
