using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Consultants;
using EquipmentManagement.Domain.DataImports;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// Structural (database-free) validation for one Consultant-import row (Phase 8 scope). Reuses <see cref="ConsultantRules"/> and
/// <see cref="AllocationRules"/> verbatim — imported rows must obey the exact
/// same rules as records created through the normal API (Phase 8 scope). Database-dependent checks (NIF/Company existence, duplicate
/// detection) are layered on afterward by the Infrastructure import service —
/// see <c>ConsultantImportService</c>. Every message is pt-PT and names the
/// field by its workbook header (<see cref="ConsultantImportColumns"/>, ADR-070).
/// </summary>
public static class ConsultantImportRowValidator
{
    public static ConsultantImportRowFields ValidateStructural(ConsultantImportRowInput input, List<ImportIssue> issues)
    {
        foreach (var field in input.FormulaFields)
        {
            issues.Add(new ImportIssue(
                ConsultantImportIssueCodes.FormulaNotAllowed, field, ImportMessages.FormulaNotAllowed(ConsultantImportColumns.HeaderFor(field)), ImportIssueSeverity.Error));
        }

        var fullName = (input.FullName ?? string.Empty).Trim();
        if (!ConsultantRules.IsValidFullName(fullName))
        {
            issues.Add(Error(ConsultantImportIssueCodes.MissingFullName, ConsultantImportColumns.FullName, ImportMessages.Required(ConsultantImportColumns.FullName.Header)));
        }

        var normalizedNif = ConsultantRules.NormalizeNif(input.Nif ?? string.Empty);
        if (input.NifIsAmbiguousNumeric)
        {
            issues.Add(Error(ConsultantImportIssueCodes.NumericIdentifierRejected, ConsultantImportColumns.Nif, ImportMessages.StoredAsNumber(ConsultantImportColumns.Nif.Header)));
        }
        else if (!ConsultantRules.IsValidNormalizedNif(normalizedNif))
        {
            issues.Add(Error(
                ConsultantImportIssueCodes.InvalidNif, ConsultantImportColumns.Nif,
                $"O campo \"{ConsultantImportColumns.Nif.Header}\" é obrigatório e deve conter um número de identificação fiscal válido."));
        }

        var email = (input.Email ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            issues.Add(Error(ConsultantImportIssueCodes.MissingEmail, ConsultantImportColumns.Email, ImportMessages.Required(ConsultantImportColumns.Email.Header)));
        }

        var address = (input.Address ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            issues.Add(Error(ConsultantImportIssueCodes.MissingAddress, ConsultantImportColumns.Address, ImportMessages.Required(ConsultantImportColumns.Address.Header)));
        }

        var phone = (input.Phone ?? string.Empty).Trim();
        if (input.PhoneIsAmbiguousNumeric)
        {
            issues.Add(Error(ConsultantImportIssueCodes.NumericIdentifierRejected, ConsultantImportColumns.Phone, ImportMessages.StoredAsNumber(ConsultantImportColumns.Phone.Header)));
        }
        else if (string.IsNullOrWhiteSpace(phone))
        {
            issues.Add(Error(ConsultantImportIssueCodes.MissingPhone, ConsultantImportColumns.Phone, ImportMessages.Required(ConsultantImportColumns.Phone.Header)));
        }

        var hasAllocation =
            !string.IsNullOrWhiteSpace(input.WorkforceType) || !string.IsNullOrWhiteSpace(input.EngagementType) ||
            !string.IsNullOrWhiteSpace(input.RoleOrFunction) || !string.IsNullOrWhiteSpace(input.CompanyName) ||
            input.AllocationStartDate is not null || input.AllocationStartDateInvalid ||
            input.AllocationEndDate is not null || input.AllocationEndDateInvalid;

        WorkforceType? workforceType = null;
        EngagementType? engagementType = null;
        string? roleOrFunction = null;
        string? companyName = null;
        DateOnly? startDate = null;
        DateOnly? endDate = null;

        if (hasAllocation)
        {
            if (string.IsNullOrWhiteSpace(input.WorkforceType))
            {
                issues.Add(Error(
                    ConsultantImportIssueCodes.MissingWorkforceType, ConsultantImportColumns.WorkforceType,
                    ImportMessages.RequiredWithAllocation(ConsultantImportColumns.WorkforceType.Header)));
            }
            else if (ExcelDisplayValues.TryParseWorkforceType(input.WorkforceType, out var parsedWorkforceType))
            {
                workforceType = parsedWorkforceType;
            }
            else
            {
                issues.Add(Error(
                    ConsultantImportIssueCodes.InvalidWorkforceType, ConsultantImportColumns.WorkforceType,
                    ImportMessages.InvalidChoice(ConsultantImportColumns.WorkforceType.Header, input.WorkforceType.Trim(), ExcelDisplayValues.WorkforceTypeLabels)));
            }

            if (string.IsNullOrWhiteSpace(input.EngagementType))
            {
                issues.Add(Error(
                    ConsultantImportIssueCodes.MissingEngagementType, ConsultantImportColumns.EngagementType,
                    ImportMessages.RequiredWithAllocation(ConsultantImportColumns.EngagementType.Header)));
            }
            else if (ExcelDisplayValues.TryParseEngagementType(input.EngagementType, out var parsedEngagementType))
            {
                engagementType = parsedEngagementType;
            }
            else
            {
                issues.Add(Error(
                    ConsultantImportIssueCodes.InvalidEngagementType, ConsultantImportColumns.EngagementType,
                    ImportMessages.InvalidChoice(ConsultantImportColumns.EngagementType.Header, input.EngagementType.Trim(), ExcelDisplayValues.EngagementTypeLabels)));
            }

            roleOrFunction = string.IsNullOrWhiteSpace(input.RoleOrFunction) ? null : input.RoleOrFunction.Trim();
            if (!AllocationRules.IsValidRoleOrFunction(roleOrFunction))
            {
                issues.Add(Error(
                    ConsultantImportIssueCodes.MissingRoleOrFunction, ConsultantImportColumns.RoleOrFunction,
                    ImportMessages.RequiredWithAllocation(ConsultantImportColumns.RoleOrFunction.Header)));
            }

            companyName = string.IsNullOrWhiteSpace(input.CompanyName) ? null : input.CompanyName.Trim();
            if (workforceType is not null)
            {
                var companyPresenceOk = workforceType == WorkforceType.External ? companyName is not null : companyName is null;
                if (!companyPresenceOk)
                {
                    issues.Add(workforceType == WorkforceType.External
                        ? Error(
                            ConsultantImportIssueCodes.MissingCompany, ConsultantImportColumns.Company,
                            $"O campo \"{ConsultantImportColumns.Company.Header}\" é obrigatório quando o regime é {ExcelDisplayValues.Label(WorkforceType.External)}.")
                        : Error(
                            ConsultantImportIssueCodes.CompanyNotAllowedForInternal, ConsultantImportColumns.Company,
                            $"O campo \"{ConsultantImportColumns.Company.Header}\" deve ficar vazio quando o regime é {ExcelDisplayValues.Label(WorkforceType.Internal)}."));
                }
            }

            if (input.AllocationStartDateInvalid)
            {
                issues.Add(Error(
                    ConsultantImportIssueCodes.InvalidAllocationDate, ConsultantImportColumns.AllocationStartDate,
                    ImportMessages.InvalidDate(ConsultantImportColumns.AllocationStartDate.Header)));
            }
            else if (input.AllocationStartDate is null)
            {
                issues.Add(Error(
                    ConsultantImportIssueCodes.MissingAllocationStartDate, ConsultantImportColumns.AllocationStartDate,
                    ImportMessages.RequiredWithAllocation(ConsultantImportColumns.AllocationStartDate.Header)));
            }
            else
            {
                startDate = input.AllocationStartDate;
            }

            if (input.AllocationEndDateInvalid)
            {
                issues.Add(Error(
                    ConsultantImportIssueCodes.InvalidAllocationDate, ConsultantImportColumns.AllocationEndDate,
                    ImportMessages.InvalidDate(ConsultantImportColumns.AllocationEndDate.Header)));
            }
            else
            {
                endDate = input.AllocationEndDate;
            }

            if (startDate is not null && !AllocationRules.IsValidDateRange(startDate.Value, endDate))
            {
                issues.Add(Error(
                    ConsultantImportIssueCodes.InvalidAllocationDate, ConsultantImportColumns.AllocationEndDate,
                    $"A \"{ConsultantImportColumns.AllocationEndDate.Header}\" não pode ser anterior à \"{ConsultantImportColumns.AllocationStartDate.Header}\"."));
            }
        }

        return new ConsultantImportRowFields(
            fullName, normalizedNif, email, address, phone, hasAllocation, workforceType, engagementType,
            roleOrFunction, companyName, startDate, endDate);
    }

    private static ImportIssue Error(string code, ImportColumn column, string message) => new(code, column.Field, message, ImportIssueSeverity.Error);
}
