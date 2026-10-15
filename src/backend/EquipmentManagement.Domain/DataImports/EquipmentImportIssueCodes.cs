namespace EquipmentManagement.Domain.DataImports;

/// <summary>Stable, machine-readable Equipment-import issue codes (Phase 8 scope).</summary>
public static class EquipmentImportIssueCodes
{
    public const string MissingEquipmentType = "MissingEquipmentType";
    public const string UnknownEquipmentType = "UnknownEquipmentType";
    public const string InactiveEquipmentType = "InactiveEquipmentType";
    public const string UnsupportedEquipmentKind = "UnsupportedEquipmentKind";
    public const string DuplicateSerialInFile = "DuplicateSerialInFile";
    public const string DuplicateSerialExisting = "DuplicateSerialExisting";
    public const string NumericIdentifierRejected = "NumericIdentifierRejected";
    public const string MissingLaptopManufacturer = "MissingLaptopManufacturer";
    public const string MissingLaptopSerial = "MissingLaptopSerial";
    public const string MissingLaptopProcessor = "MissingLaptopProcessor";
    public const string MissingLaptopMemory = "MissingLaptopMemory";
    public const string MissingLaptopStorage = "MissingLaptopStorage";
    public const string LaptopFieldsNotAllowedForGeneric = "LaptopFieldsNotAllowedForGeneric";
    public const string FormulaNotAllowed = "FormulaNotAllowed";
    public const string CellTooLong = "CellTooLong";
}
