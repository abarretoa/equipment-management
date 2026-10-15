using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Infrastructure.DataImports;

namespace EquipmentManagement.UnitTests.Infrastructure.DataImports;

public class EquipmentImportRowValidatorTests
{
    private static EquipmentImportRowInput ValidGenericInput() => new(
        RowNumber: 2,
        EquipmentTypeName: "Mouse",
        Manufacturer: "Logitech",
        Model: "MX Master",
        SerialNumber: "ABC123",
        SerialNumberIsAmbiguousNumeric: false,
        Notes: null,
        Processor: null,
        Memory: null,
        Storage: null,
        FormulaFields: new HashSet<string>());

    [Fact]
    public void ValidateStructural_ValidRow_ProducesNoIssues()
    {
        var issues = new List<ImportIssue>();
        var fields = EquipmentImportRowValidator.ValidateStructural(ValidGenericInput(), issues);

        Assert.Empty(issues);
        Assert.Equal("Mouse", fields.EquipmentTypeName);
        Assert.Equal("ABC123", fields.NormalizedSerialNumber);
    }

    [Fact]
    public void ValidateStructural_MissingEquipmentType_ReportsError()
    {
        var input = ValidGenericInput() with { EquipmentTypeName = "  " };
        var issues = new List<ImportIssue>();
        EquipmentImportRowValidator.ValidateStructural(input, issues);

        Assert.Contains(issues, i => i.Code == EquipmentImportIssueCodes.MissingEquipmentType);
    }

    [Fact]
    public void ValidateStructural_AmbiguousNumericSerial_ReportsNumericIdentifierRejected()
    {
        var input = ValidGenericInput() with { SerialNumberIsAmbiguousNumeric = true };
        var issues = new List<ImportIssue>();
        var fields = EquipmentImportRowValidator.ValidateStructural(input, issues);

        Assert.Contains(issues, i => i.Code == EquipmentImportIssueCodes.NumericIdentifierRejected && i.Field == "SerialNumber");
        Assert.Null(fields.NormalizedSerialNumber);
    }

    [Fact]
    public void ValidateStructural_FormulaCell_ReportsFormulaNotAllowed()
    {
        var input = ValidGenericInput() with { FormulaFields = new HashSet<string> { "Manufacturer" } };
        var issues = new List<ImportIssue>();
        EquipmentImportRowValidator.ValidateStructural(input, issues);

        Assert.Contains(issues, i => i.Code == EquipmentImportIssueCodes.FormulaNotAllowed && i.Field == "Manufacturer");
    }

    [Fact]
    public void ValidateAgainstKind_Laptop_MissingAllRequiredFields_ReportsEachOne()
    {
        var fields = new EquipmentImportRowFields("Laptop", null, null, null, null, null, null, null, null);
        var issues = new List<ImportIssue>();
        EquipmentImportRowValidator.ValidateAgainstKind(EquipmentTypeKind.Laptop, fields, issues);

        Assert.Contains(issues, i => i.Code == EquipmentImportIssueCodes.MissingLaptopManufacturer);
        Assert.Contains(issues, i => i.Code == EquipmentImportIssueCodes.MissingLaptopSerial);
        Assert.Contains(issues, i => i.Code == EquipmentImportIssueCodes.MissingLaptopProcessor);
        Assert.Contains(issues, i => i.Code == EquipmentImportIssueCodes.MissingLaptopMemory);
        Assert.Contains(issues, i => i.Code == EquipmentImportIssueCodes.MissingLaptopStorage);
    }

    [Fact]
    public void ValidateAgainstKind_Laptop_AllFieldsPresent_ProducesNoIssues()
    {
        var fields = new EquipmentImportRowFields("Laptop", "Dell", "Latitude", "SN1", "SN1", null, "i5", "16 GB", "512 GB SSD");
        var issues = new List<ImportIssue>();
        EquipmentImportRowValidator.ValidateAgainstKind(EquipmentTypeKind.Laptop, fields, issues);

        Assert.Empty(issues);
    }

    [Fact]
    public void ValidateAgainstKind_Generic_WithLaptopFields_ReportsLaptopFieldsNotAllowed()
    {
        var fields = new EquipmentImportRowFields("Monitor", "Dell", null, null, null, null, "i5", null, null);
        var issues = new List<ImportIssue>();
        EquipmentImportRowValidator.ValidateAgainstKind(EquipmentTypeKind.Generic, fields, issues);

        Assert.Contains(issues, i => i.Code == EquipmentImportIssueCodes.LaptopFieldsNotAllowedForGeneric);
    }

    [Fact]
    public void ValidateAgainstKind_Generic_WithoutLaptopFields_ProducesNoIssues()
    {
        var fields = new EquipmentImportRowFields("Monitor", "Dell", "U2723QE", null, null, null, null, null, null);
        var issues = new List<ImportIssue>();
        EquipmentImportRowValidator.ValidateAgainstKind(EquipmentTypeKind.Generic, fields, issues);

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("Telemóvel")]
    [InlineData("Mobile Phone")]
    public void ValidateAgainstKind_MobilePhone_ReportsOnlyTheUnsupportedKind_NamingTheType(string typeName)
    {
        // Even with every other field filled in, the row cannot be imported: there is no IMEI column.
        var fields = new EquipmentImportRowFields(typeName, "Samsung", "Galaxy A05s", "R58W123", "R58W123", null, "x", "y", "z");
        var issues = new List<ImportIssue>();
        EquipmentImportRowValidator.ValidateAgainstKind(EquipmentTypeKind.MobilePhone, fields, issues);

        var issue = Assert.Single(issues);
        Assert.Equal(EquipmentImportIssueCodes.UnsupportedEquipmentKind, issue.Code);
        Assert.Equal("EquipmentType", issue.Field);
        Assert.Equal(ImportIssueSeverity.Error, issue.Severity);
        Assert.Equal(
            $"O tipo de equipamento \"{typeName}\" não pode ser importado através deste ficheiro. Registe este equipamento diretamente na plataforma.",
            issue.Message);
    }

    [Theory]
    [InlineData(EquipmentTypeKind.Generic, true)]
    [InlineData(EquipmentTypeKind.Laptop, true)]
    [InlineData(EquipmentTypeKind.MobilePhone, false)]
    public void IsImportable_OnlyMobilePhoneIsExcluded(EquipmentTypeKind kind, bool expected) =>
        Assert.Equal(expected, EquipmentImportRowValidator.IsImportable(kind));
}
