using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// Structural (database-free) validation for one Equipment-import row (Phase 8 scope). Reuses <see cref="EquipmentRules"/> and
/// <see cref="LaptopSpecificationRules"/> verbatim — the same rules the
/// normal <c>EquipmentService.CreateAsync</c> API path enforces. Split into
/// two passes because Kind-dependent rules need the EquipmentType looked up
/// from the database (see <c>EquipmentImportService</c>):
/// <see cref="ValidateStructural"/> runs first (no database), then the
/// service resolves EquipmentType and calls <see cref="ValidateAgainstKind"/>.
/// Every message is pt-PT and names the field by its workbook header
/// (<see cref="EquipmentImportColumns"/>, ADR-070).
/// </summary>
public static class EquipmentImportRowValidator
{
    public static EquipmentImportRowFields ValidateStructural(EquipmentImportRowInput input, List<ImportIssue> issues)
    {
        foreach (var field in input.FormulaFields)
        {
            issues.Add(new ImportIssue(
                EquipmentImportIssueCodes.FormulaNotAllowed, field, ImportMessages.FormulaNotAllowed(EquipmentImportColumns.HeaderFor(field)), ImportIssueSeverity.Error));
        }

        var equipmentTypeName = (input.EquipmentTypeName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(equipmentTypeName))
        {
            issues.Add(Error(EquipmentImportIssueCodes.MissingEquipmentType, EquipmentImportColumns.EquipmentType, ImportMessages.Required(EquipmentImportColumns.EquipmentType.Header)));
        }

        string? normalizedSerial = null;
        if (input.SerialNumberIsAmbiguousNumeric)
        {
            issues.Add(Error(
                EquipmentImportIssueCodes.NumericIdentifierRejected, EquipmentImportColumns.SerialNumber, ImportMessages.StoredAsNumber(EquipmentImportColumns.SerialNumber.Header)));
        }
        else
        {
            normalizedSerial = EquipmentRules.NormalizeSerialNumber(input.SerialNumber);
        }

        return new EquipmentImportRowFields(
            equipmentTypeName,
            string.IsNullOrWhiteSpace(input.Manufacturer) ? null : input.Manufacturer.Trim(),
            string.IsNullOrWhiteSpace(input.Model) ? null : input.Model.Trim(),
            string.IsNullOrWhiteSpace(input.SerialNumber) || input.SerialNumberIsAmbiguousNumeric ? null : input.SerialNumber.Trim(),
            normalizedSerial,
            string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            string.IsNullOrWhiteSpace(input.Processor) ? null : input.Processor.Trim(),
            string.IsNullOrWhiteSpace(input.Memory) ? null : input.Memory.Trim(),
            string.IsNullOrWhiteSpace(input.Storage) ? null : input.Storage.Trim());
    }

    /// <summary>Mirrors <c>EquipmentService</c>'s private <c>ValidateAgainstKind</c> exactly, as pure (database-free) rules over the Domain's own <see cref="EquipmentRules"/>.</summary>
    public static void ValidateAgainstKind(EquipmentTypeKind kind, EquipmentImportRowFields fields, List<ImportIssue> issues)
    {
        if (!IsImportable(kind))
        {
            issues.Add(UnsupportedKind(fields.EquipmentTypeName));
            return;
        }

        if (kind == EquipmentTypeKind.Laptop)
        {
            if (EquipmentRules.IsBlank(fields.Manufacturer))
            {
                issues.Add(Error(EquipmentImportIssueCodes.MissingLaptopManufacturer, EquipmentImportColumns.Manufacturer, ImportMessages.RequiredForLaptops(EquipmentImportColumns.Manufacturer.Header)));
            }

            if (EquipmentRules.IsBlank(fields.SerialNumber))
            {
                issues.Add(Error(EquipmentImportIssueCodes.MissingLaptopSerial, EquipmentImportColumns.SerialNumber, ImportMessages.RequiredForLaptops(EquipmentImportColumns.SerialNumber.Header)));
            }

            if (EquipmentRules.IsBlank(fields.Processor))
            {
                issues.Add(Error(EquipmentImportIssueCodes.MissingLaptopProcessor, EquipmentImportColumns.Processor, ImportMessages.RequiredForLaptops(EquipmentImportColumns.Processor.Header)));
            }

            if (EquipmentRules.IsBlank(fields.Memory))
            {
                issues.Add(Error(EquipmentImportIssueCodes.MissingLaptopMemory, EquipmentImportColumns.Memory, ImportMessages.RequiredForLaptops(EquipmentImportColumns.Memory.Header)));
            }

            if (EquipmentRules.IsBlank(fields.Storage))
            {
                issues.Add(Error(EquipmentImportIssueCodes.MissingLaptopStorage, EquipmentImportColumns.Storage, ImportMessages.RequiredForLaptops(EquipmentImportColumns.Storage.Header)));
            }
        }
        else if (!EquipmentRules.IsBlank(fields.Processor) || !EquipmentRules.IsBlank(fields.Memory) || !EquipmentRules.IsBlank(fields.Storage))
        {
            issues.Add(Error(
                EquipmentImportIssueCodes.LaptopFieldsNotAllowedForGeneric, EquipmentImportColumns.Processor,
                $"Os campos \"{EquipmentImportColumns.Processor.Header}\", \"{EquipmentImportColumns.Memory.Header}\" e \"{EquipmentImportColumns.Storage.Header}\" só podem ser preenchidos para portáteis."));
        }
    }

    /// <summary>
    /// A MobilePhone needs an IMEI (<see cref="MobilePhoneSpecificationRules"/>),
    /// which the workbook has no column for — so <c>EquipmentService.CreateAsync</c>
    /// would always refuse the row at confirmation. Preview and confirmation
    /// both ask this, so such a row is an error from the preview onwards and
    /// never reaches confirmation.
    /// </summary>
    public static bool IsImportable(EquipmentTypeKind kind) => kind != EquipmentTypeKind.MobilePhone;

    public static ImportIssue UnsupportedKind(string equipmentTypeName) => Error(
        EquipmentImportIssueCodes.UnsupportedEquipmentKind, EquipmentImportColumns.EquipmentType, ImportMessages.EquipmentTypeNotImportable(equipmentTypeName));

    private static ImportIssue Error(string code, ImportColumn column, string message) => new(code, column.Field, message, ImportIssueSeverity.Error);
}
