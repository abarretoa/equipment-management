namespace EquipmentManagement.Domain.DataImports;

/// <summary>Stable, machine-readable Consultant-import issue codes (Phase 8 scope) — never a raw database constraint name.</summary>
public static class ConsultantImportIssueCodes
{
    public const string MissingFullName = "MissingFullName";
    public const string InvalidNif = "InvalidNif";
    public const string NumericIdentifierRejected = "NumericIdentifierRejected";
    public const string MissingEmail = "MissingEmail";
    public const string MissingAddress = "MissingAddress";
    public const string MissingPhone = "MissingPhone";
    public const string DuplicateNifInFile = "DuplicateNifInFile";
    public const string DuplicateNifExisting = "DuplicateNifExisting";
    public const string MissingCompany = "MissingCompany";
    public const string UnknownCompany = "UnknownCompany";
    public const string InactiveCompany = "InactiveCompany";
    public const string CompanyNotAllowedForInternal = "CompanyNotAllowedForInternal";
    public const string MissingWorkforceType = "MissingWorkforceType";
    public const string InvalidWorkforceType = "InvalidWorkforceType";
    public const string MissingEngagementType = "MissingEngagementType";
    public const string InvalidEngagementType = "InvalidEngagementType";
    public const string MissingRoleOrFunction = "MissingRoleOrFunction";
    public const string MissingAllocationStartDate = "MissingAllocationStartDate";
    public const string InvalidAllocationDate = "InvalidAllocationDate";
    public const string FormulaNotAllowed = "FormulaNotAllowed";
    public const string CellTooLong = "CellTooLong";
}
