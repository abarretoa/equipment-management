using System.ComponentModel.DataAnnotations;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Consultants;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// Structural (database-free) validation for one Current Inventory import
/// row. Reuses the Domain rules verbatim — <see cref="ConsultantRules"/>,
/// <see cref="AllocationRules"/>, <see cref="EquipmentRules"/>,
/// <see cref="LaptopSpecificationRules"/>, <see cref="MobilePhoneSpecificationRules"/> —
/// so an imported row obeys exactly what the normal screens enforce. Two
/// passes, like <see cref="EquipmentImportRowValidator"/>: <see cref="ValidateStructural"/>
/// needs no database; <see cref="ValidateAgainstKind"/> runs once the
/// service has resolved the EquipmentType. Every message is pt-PT and names
/// the field by its workbook header (<see cref="CurrentInventoryImportColumns"/>).
/// </summary>
public static class CurrentInventoryImportRowValidator
{
    private static readonly EmailAddressAttribute EmailFormat = new();
    private static readonly PhoneAttribute PhoneFormat = new();

    public static CurrentInventoryImportRowFields ValidateStructural(CurrentInventoryImportRowInput input, DateOnly today, List<ImportIssue> issues)
    {
        foreach (var field in input.FormulaFields)
        {
            issues.Add(new ImportIssue(
                CurrentInventoryImportIssueCodes.FormulaNotAllowed, field,
                ImportMessages.FormulaNotAllowed(CurrentInventoryImportColumns.HeaderFor(field)), ImportIssueSeverity.Error));
        }

        // --- Equipment ---
        var equipmentTypeName = Clean(input.EquipmentTypeName) ?? string.Empty;
        if (equipmentTypeName.Length == 0)
        {
            issues.Add(Error(CurrentInventoryImportIssueCodes.MissingEquipmentType, CurrentInventoryImportColumns.EquipmentType,
                ImportMessages.Required(CurrentInventoryImportColumns.EquipmentType.Header)));
        }

        var serial = ReadIdentifier(input.SerialNumber, input.SerialNumberIsAmbiguousNumeric, CurrentInventoryImportColumns.SerialNumber, issues);
        var imei = ReadIdentifier(input.Imei, input.ImeiIsAmbiguousNumeric, CurrentInventoryImportColumns.Imei, issues);
        var mobileNumber = ReadIdentifier(input.MobileNumber, input.MobileNumberIsAmbiguousNumeric, CurrentInventoryImportColumns.MobileNumber, issues);
        var simSerial = ReadIdentifier(input.SimSerialNumber, input.SimSerialNumberIsAmbiguousNumeric, CurrentInventoryImportColumns.SimSerialNumber, issues);

        // --- Person / allocation / custody ---
        // ANY value in this block makes the row an assigned row. Only a fully
        // blank block is stock: partially filled person data is validated
        // (and rejected), never silently dropped.
        var hasConsultant =
            !string.IsNullOrWhiteSpace(input.FullName) || !string.IsNullOrWhiteSpace(input.Nif) || input.NifIsAmbiguousNumeric ||
            !string.IsNullOrWhiteSpace(input.Email) || !string.IsNullOrWhiteSpace(input.Phone) || input.PhoneIsAmbiguousNumeric ||
            !string.IsNullOrWhiteSpace(input.Address) || !string.IsNullOrWhiteSpace(input.WorkforceType) ||
            !string.IsNullOrWhiteSpace(input.EngagementType) || !string.IsNullOrWhiteSpace(input.RoleOrFunction) ||
            !string.IsNullOrWhiteSpace(input.CompanyName) ||
            input.AllocationStartDate is not null || input.AllocationStartDateInvalid ||
            input.AllocationEndDate is not null || input.AllocationEndDateInvalid ||
            input.AssignmentDate is not null || input.AssignmentDateInvalid;

        string? fullName = null, nif = null, email = null, phone = null, address = null, role = null, company = null;
        WorkforceType? workforceType = null;
        EngagementType? engagementType = null;
        DateOnly? startDate = null, endDate = null, assignmentDate = null;

        if (hasConsultant)
        {
            fullName = Clean(input.FullName);
            if (!ConsultantRules.IsValidFullName(fullName))
            {
                issues.Add(RequiredForAssigned(CurrentInventoryImportIssueCodes.MissingFullName, CurrentInventoryImportColumns.FullName));
            }

            if (input.NifIsAmbiguousNumeric)
            {
                issues.Add(StoredAsNumber(CurrentInventoryImportColumns.Nif));
            }
            else
            {
                var normalizedNif = ConsultantRules.NormalizeNif(input.Nif ?? string.Empty);
                if (ConsultantRules.IsValidNormalizedNif(normalizedNif))
                {
                    nif = normalizedNif;
                }
                else
                {
                    issues.Add(Error(CurrentInventoryImportIssueCodes.InvalidNif, CurrentInventoryImportColumns.Nif,
                        $"O campo \"{CurrentInventoryImportColumns.Nif.Header}\" é obrigatório para equipamento atribuído e deve conter um número de identificação fiscal válido."));
                }
            }

            email = Clean(input.Email);
            if (email is null)
            {
                issues.Add(RequiredForAssigned(CurrentInventoryImportIssueCodes.MissingEmail, CurrentInventoryImportColumns.Email));
            }
            else if (!EmailFormat.IsValid(email))
            {
                issues.Add(Error(CurrentInventoryImportIssueCodes.InvalidEmail, CurrentInventoryImportColumns.Email,
                    $"O valor indicado em \"{CurrentInventoryImportColumns.Email.Header}\" não é um endereço de email válido."));
            }

            if (input.PhoneIsAmbiguousNumeric)
            {
                issues.Add(StoredAsNumber(CurrentInventoryImportColumns.Phone));
            }
            else
            {
                phone = Clean(input.Phone);
                if (phone is null)
                {
                    issues.Add(RequiredForAssigned(CurrentInventoryImportIssueCodes.MissingPhone, CurrentInventoryImportColumns.Phone));
                }
                else if (!PhoneFormat.IsValid(phone))
                {
                    issues.Add(Error(CurrentInventoryImportIssueCodes.InvalidPhone, CurrentInventoryImportColumns.Phone,
                        $"O valor indicado em \"{CurrentInventoryImportColumns.Phone.Header}\" não é um número de telefone válido."));
                }
            }

            address = Clean(input.Address);
            if (address is null)
            {
                issues.Add(RequiredForAssigned(CurrentInventoryImportIssueCodes.MissingAddress, CurrentInventoryImportColumns.Address));
            }

            // Regime and Tipo de vínculo are always explicit — never inferred
            // from each other, the company, the email domain or anything else.
            if (string.IsNullOrWhiteSpace(input.WorkforceType))
            {
                issues.Add(RequiredForAssigned(CurrentInventoryImportIssueCodes.MissingWorkforceType, CurrentInventoryImportColumns.WorkforceType));
            }
            else if (ExcelDisplayValues.TryParseWorkforceType(input.WorkforceType, out var parsedWorkforceType))
            {
                workforceType = parsedWorkforceType;
            }
            else
            {
                issues.Add(Error(CurrentInventoryImportIssueCodes.InvalidWorkforceType, CurrentInventoryImportColumns.WorkforceType,
                    ImportMessages.InvalidChoice(CurrentInventoryImportColumns.WorkforceType.Header, input.WorkforceType.Trim(), ExcelDisplayValues.WorkforceTypeLabels)));
            }

            if (string.IsNullOrWhiteSpace(input.EngagementType))
            {
                issues.Add(RequiredForAssigned(CurrentInventoryImportIssueCodes.MissingEngagementType, CurrentInventoryImportColumns.EngagementType));
            }
            else if (ExcelDisplayValues.TryParseEngagementType(input.EngagementType, out var parsedEngagementType))
            {
                engagementType = parsedEngagementType;
            }
            else
            {
                issues.Add(Error(CurrentInventoryImportIssueCodes.InvalidEngagementType, CurrentInventoryImportColumns.EngagementType,
                    ImportMessages.InvalidChoice(CurrentInventoryImportColumns.EngagementType.Header, input.EngagementType.Trim(), ExcelDisplayValues.EngagementTypeLabels)));
            }

            role = Clean(input.RoleOrFunction);
            if (!AllocationRules.IsValidRoleOrFunction(role))
            {
                issues.Add(RequiredForAssigned(CurrentInventoryImportIssueCodes.MissingRoleOrFunction, CurrentInventoryImportColumns.RoleOrFunction));
            }

            company = Clean(input.CompanyName);
            if (workforceType == WorkforceType.External && company is null)
            {
                issues.Add(Error(CurrentInventoryImportIssueCodes.MissingCompany, CurrentInventoryImportColumns.Company,
                    $"O campo \"{CurrentInventoryImportColumns.Company.Header}\" é obrigatório quando o regime é {ExcelDisplayValues.Label(WorkforceType.External)}."));
            }
            else if (workforceType == WorkforceType.Internal && company is not null)
            {
                // AllocationRules.IsCompanyRequirementSatisfied: an Internal
                // allocation carries no company. Never silently dropped.
                issues.Add(Error(CurrentInventoryImportIssueCodes.CompanyNotAllowedForInternal, CurrentInventoryImportColumns.Company,
                    $"O campo \"{CurrentInventoryImportColumns.Company.Header}\" deve ficar vazio quando o regime é {ExcelDisplayValues.Label(WorkforceType.Internal)}."));
            }

            if (input.AllocationStartDateInvalid)
            {
                issues.Add(InvalidDate(CurrentInventoryImportColumns.AllocationStartDate));
            }
            else if (input.AllocationStartDate is null)
            {
                issues.Add(RequiredForAssigned(CurrentInventoryImportIssueCodes.MissingAllocationStartDate, CurrentInventoryImportColumns.AllocationStartDate));
            }
            else
            {
                startDate = input.AllocationStartDate;
            }

            if (input.AllocationEndDateInvalid)
            {
                issues.Add(InvalidDate(CurrentInventoryImportColumns.AllocationEndDate));
            }
            else
            {
                endDate = input.AllocationEndDate;
            }

            if (startDate is not null && !AllocationRules.IsValidDateRange(startDate.Value, endDate))
            {
                issues.Add(Error(CurrentInventoryImportIssueCodes.AllocationEndBeforeStart, CurrentInventoryImportColumns.AllocationEndDate,
                    $"A \"{CurrentInventoryImportColumns.AllocationEndDate.Header}\" não pode ser anterior à \"{CurrentInventoryImportColumns.AllocationStartDate.Header}\"."));
            }

            if (input.AssignmentDateInvalid)
            {
                issues.Add(InvalidDate(CurrentInventoryImportColumns.AssignmentDate));
            }
            else if (input.AssignmentDate is { } date && date > today)
            {
                issues.Add(Error(CurrentInventoryImportIssueCodes.AssignmentDateInFuture, CurrentInventoryImportColumns.AssignmentDate,
                    $"A \"{CurrentInventoryImportColumns.AssignmentDate.Header}\" não pode ser uma data futura: o ficheiro descreve quem tem o equipamento hoje."));
            }
            else
            {
                assignmentDate = input.AssignmentDate;
            }
        }

        return new CurrentInventoryImportRowFields(
            equipmentTypeName,
            Clean(input.Manufacturer),
            Clean(input.Model),
            serial,
            EquipmentRules.NormalizeSerialNumber(serial),
            Clean(input.Notes),
            Clean(input.Processor),
            Clean(input.Memory),
            Clean(input.Storage),
            imei,
            MobilePhoneSpecificationRules.NormalizeImei(imei),
            mobileNumber,
            simSerial,
            hasConsultant,
            fullName,
            nif,
            email,
            phone,
            address,
            workforceType,
            engagementType,
            role,
            company,
            startDate,
            endDate,
            assignmentDate);
    }

    /// <summary>
    /// The Kind-dependent rules — the same ones <c>EquipmentService.CreateAsync</c>
    /// enforces (see its <c>ValidateAgainstKind</c>), decided by
    /// <see cref="EquipmentTypeKind"/>, never by the type's (localized) name.
    /// Unlike the Equipment-only import, MobilePhone is fully supported: the
    /// IMEI column exists here.
    /// </summary>
    public static void ValidateAgainstKind(EquipmentTypeKind kind, CurrentInventoryImportRowFields fields, List<ImportIssue> issues)
    {
        if (EquipmentRules.RequiresManufacturer(kind) && EquipmentRules.IsBlank(fields.Manufacturer))
        {
            issues.Add(Error(CurrentInventoryImportIssueCodes.MissingManufacturer, CurrentInventoryImportColumns.Manufacturer,
                $"O campo \"{CurrentInventoryImportColumns.Manufacturer.Header}\" é obrigatório para {KindPlural(kind)}."));
        }

        if (kind == EquipmentTypeKind.Laptop)
        {
            if (EquipmentRules.RequiresSerialNumber(kind) && EquipmentRules.IsBlank(fields.SerialNumber))
            {
                issues.Add(Error(CurrentInventoryImportIssueCodes.MissingLaptopSerial, CurrentInventoryImportColumns.SerialNumber,
                    ImportMessages.RequiredForLaptops(CurrentInventoryImportColumns.SerialNumber.Header)));
            }

            if (EquipmentRules.IsBlank(fields.Processor))
            {
                issues.Add(Error(CurrentInventoryImportIssueCodes.MissingLaptopProcessor, CurrentInventoryImportColumns.Processor,
                    ImportMessages.RequiredForLaptops(CurrentInventoryImportColumns.Processor.Header)));
            }

            if (EquipmentRules.IsBlank(fields.Memory))
            {
                issues.Add(Error(CurrentInventoryImportIssueCodes.MissingLaptopMemory, CurrentInventoryImportColumns.Memory,
                    ImportMessages.RequiredForLaptops(CurrentInventoryImportColumns.Memory.Header)));
            }

            if (EquipmentRules.IsBlank(fields.Storage))
            {
                issues.Add(Error(CurrentInventoryImportIssueCodes.MissingLaptopStorage, CurrentInventoryImportColumns.Storage,
                    ImportMessages.RequiredForLaptops(CurrentInventoryImportColumns.Storage.Header)));
            }
        }
        else if (fields.Processor is not null || fields.Memory is not null || fields.Storage is not null)
        {
            issues.Add(Error(CurrentInventoryImportIssueCodes.LaptopFieldsNotAllowed, CurrentInventoryImportColumns.Processor,
                $"Os campos \"{CurrentInventoryImportColumns.Processor.Header}\", \"{CurrentInventoryImportColumns.Memory.Header}\" e \"{CurrentInventoryImportColumns.Storage.Header}\" só podem ser preenchidos para portáteis."));
        }

        if (kind == EquipmentTypeKind.MobilePhone)
        {
            if (!MobilePhoneSpecificationRules.IsComplete(fields.Imei))
            {
                issues.Add(Error(CurrentInventoryImportIssueCodes.MissingImei, CurrentInventoryImportColumns.Imei,
                    $"O campo \"{CurrentInventoryImportColumns.Imei.Header}\" é obrigatório para telemóveis."));
            }
            else if (!MobilePhoneSpecificationRules.IsValidImei(fields.Imei))
            {
                issues.Add(Error(CurrentInventoryImportIssueCodes.InvalidImei, CurrentInventoryImportColumns.Imei,
                    $"O campo \"{CurrentInventoryImportColumns.Imei.Header}\" deve conter exatamente 15 dígitos."));
            }
        }
        else if (fields.Imei is not null || fields.MobileNumber is not null || fields.SimSerialNumber is not null)
        {
            issues.Add(Error(CurrentInventoryImportIssueCodes.MobileFieldsNotAllowed, CurrentInventoryImportColumns.Imei,
                $"Os campos \"{CurrentInventoryImportColumns.Imei.Header}\", \"{CurrentInventoryImportColumns.MobileNumber.Header}\" e \"{CurrentInventoryImportColumns.SimSerialNumber.Header}\" só podem ser preenchidos para telemóveis."));
        }
    }

    /// <summary>Trimmed, inner whitespace kept; blank becomes null.</summary>
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ReadIdentifier(string? value, bool ambiguousNumeric, ImportColumn column, List<ImportIssue> issues)
    {
        if (ambiguousNumeric)
        {
            issues.Add(StoredAsNumber(column));
            return null;
        }

        return Clean(value);
    }

    private static string KindPlural(EquipmentTypeKind kind) => kind == EquipmentTypeKind.MobilePhone ? "telemóveis" : "portáteis";

    private static ImportIssue StoredAsNumber(ImportColumn column) =>
        Error(CurrentInventoryImportIssueCodes.NumericIdentifierRejected, column, ImportMessages.StoredAsNumber(column.Header));

    private static ImportIssue InvalidDate(ImportColumn column) =>
        Error(CurrentInventoryImportIssueCodes.InvalidDate, column, ImportMessages.InvalidDate(column.Header));

    private static ImportIssue RequiredForAssigned(string code, ImportColumn column) =>
        Error(code, column, $"O campo \"{column.Header}\" é obrigatório quando o equipamento está atribuído a uma pessoa (há dados de consultor nesta linha).");

    private static ImportIssue Error(string code, ImportColumn column, string message) => new(code, column.Field, message, ImportIssueSeverity.Error);
}
