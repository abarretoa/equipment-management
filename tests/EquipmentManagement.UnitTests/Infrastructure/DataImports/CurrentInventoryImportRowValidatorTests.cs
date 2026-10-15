using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Infrastructure.DataImports;

namespace EquipmentManagement.UnitTests.Infrastructure.DataImports;

public class CurrentInventoryImportRowValidatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static CurrentInventoryImportRowInput Stock(string type = "Monitor") => new(
        2, type, "Dell", "P2422H", "SN-1", false, null, null, null, null, null, false, null, false, null, false,
        null, null, false, null, null, false, null, null, null, null, null, null, false, null, false, null, false,
        new HashSet<string>());

    private static CurrentInventoryImportRowInput Assigned(string regime = "Interno", string? company = null) => Stock() with
    {
        FullName = "Maria Exemplo",
        Nif = "123456789",
        Email = "maria@example.invalid",
        Phone = "+351900000000",
        Address = "Rua Exemplo 1, Lisboa",
        WorkforceType = regime,
        EngagementType = "Contrato de trabalho",
        RoleOrFunction = "Consultora",
        CompanyName = company,
        AllocationStartDate = new DateOnly(2025, 1, 6),
    };

    private static (CurrentInventoryImportRowFields Fields, List<ImportIssue> Issues) Validate(CurrentInventoryImportRowInput input)
    {
        var issues = new List<ImportIssue>();
        var fields = CurrentInventoryImportRowValidator.ValidateStructural(input, Today, issues);
        return (fields, issues);
    }

    [Fact]
    public void FullyBlankPersonBlock_IsStock_WithNoIssues()
    {
        var (fields, issues) = Validate(Stock());

        Assert.False(fields.HasConsultant);
        Assert.Empty(issues);
    }

    [Fact]
    public void CompleteInternalRow_IsAssigned_WithParsedRegimeAndVinculo()
    {
        var (fields, issues) = Validate(Assigned());

        Assert.Empty(issues);
        Assert.True(fields.HasConsultant);
        Assert.Equal(WorkforceType.Internal, fields.WorkforceType);
        Assert.Equal(EngagementType.EmploymentContract, fields.EngagementType);
        Assert.Null(fields.CompanyName);
    }

    [Theory]
    [InlineData("FullName")]
    [InlineData("Email")]
    [InlineData("AssignmentDate")]
    [InlineData("Company")]
    public void AnySinglePersonCell_MakesTheRowAssigned_AndRequiresTheRest(string onlyField)
    {
        var input = onlyField switch
        {
            "FullName" => Stock() with { FullName = "João Exemplo" },
            "Email" => Stock() with { Email = "joao@example.invalid" },
            "AssignmentDate" => Stock() with { AssignmentDate = new DateOnly(2025, 1, 1) },
            _ => Stock() with { CompanyName = "Cliente A" },
        };

        var (fields, issues) = Validate(input);

        Assert.True(fields.HasConsultant);
        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.InvalidNif);
        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.MissingWorkforceType);
        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.MissingEngagementType);
        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.MissingAllocationStartDate);
    }

    [Fact]
    public void Regime_IsNeverInferred_FromVinculoOrCompany()
    {
        var (fields, issues) = Validate(Assigned() with { WorkforceType = null, CompanyName = "Cliente A" });

        Assert.Null(fields.WorkforceType);
        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.MissingWorkforceType);
    }

    [Fact]
    public void Internal_WithCompany_IsRejected_External_WithoutCompany_IsRejected()
    {
        Assert.Contains(Validate(Assigned("Interno", "Cliente A")).Issues, i => i.Code == CurrentInventoryImportIssueCodes.CompanyNotAllowedForInternal);
        Assert.Contains(Validate(Assigned("Externo")).Issues, i => i.Code == CurrentInventoryImportIssueCodes.MissingCompany);
        Assert.Empty(Validate(Assigned("Externo", "Cliente A") with { EngagementType = "Prestador de serviço" }).Issues);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("Funcionário")]
    public void Regime_AcceptsOnlyTheListedValues(string value)
    {
        var issue = Assert.Single(Validate(Assigned() with { WorkforceType = value }).Issues);

        Assert.Equal(CurrentInventoryImportIssueCodes.InvalidWorkforceType, issue.Code);
        Assert.Contains("Interno, Externo", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EndBeforeStart_AndFutureAssignmentDate_AreRejected()
    {
        var (_, issues) = Validate(Assigned() with { AllocationEndDate = new DateOnly(2024, 1, 1), AssignmentDate = Today.AddDays(1) });

        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.AllocationEndBeforeStart);
        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.AssignmentDateInFuture);
    }

    [Fact]
    public void AssignmentDateToday_IsAccepted()
    {
        var (fields, issues) = Validate(Assigned() with { AssignmentDate = Today });

        Assert.Empty(issues);
        Assert.Equal(Today, fields.AssignmentDate);
    }

    [Fact]
    public void InvalidEmailOrPhone_IsRejected()
    {
        var (_, issues) = Validate(Assigned() with { Email = "not-an-email", Phone = "abc" });

        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.InvalidEmail);
        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.InvalidPhone);
    }

    [Fact]
    public void IdentifiersStoredAsNumbers_AreRejected()
    {
        var (_, issues) = Validate(Assigned() with { NifIsAmbiguousNumeric = true, Nif = null, ImeiIsAmbiguousNumeric = true });

        Assert.Equal(2, issues.Count(i => i.Code == CurrentInventoryImportIssueCodes.NumericIdentifierRejected));
    }

    [Fact]
    public void Nif_IsNormalizedToDigits_AndImeiAndSerialAreNormalized()
    {
        var (fields, _) = Validate(Assigned() with { Nif = "123 456 789", Imei = "35-693803-564380-9", SerialNumber = " sn-1 " });

        Assert.Equal("123456789", fields.Nif);
        Assert.Equal("356938035643809", fields.NormalizedImei);
        Assert.Equal("SN-1", fields.NormalizedSerialNumber);
    }

    [Fact]
    public void Laptop_RequiresManufacturerSerialAndSpecs()
    {
        var issues = new List<ImportIssue>();
        var fields = CurrentInventoryImportRowValidator.ValidateStructural(Stock("Portátil") with { Manufacturer = null, SerialNumber = null }, Today, issues);

        CurrentInventoryImportRowValidator.ValidateAgainstKind(EquipmentTypeKind.Laptop, fields, issues);

        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.MissingManufacturer);
        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.MissingLaptopSerial);
        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.MissingLaptopProcessor);
    }

    [Fact]
    public void MobilePhone_RequiresOnlyImeiAndManufacturer_AmongItsFields()
    {
        var issues = new List<ImportIssue>();
        var fields = CurrentInventoryImportRowValidator.ValidateStructural(Stock("Telemóvel") with { SerialNumber = null, Imei = "356938035643809" }, Today, issues);

        CurrentInventoryImportRowValidator.ValidateAgainstKind(EquipmentTypeKind.MobilePhone, fields, issues);
        Assert.Empty(issues);

        var withoutImei = CurrentInventoryImportRowValidator.ValidateStructural(Stock("Telemóvel"), Today, issues);
        CurrentInventoryImportRowValidator.ValidateAgainstKind(EquipmentTypeKind.MobilePhone, withoutImei, issues);
        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.MissingImei && i.Message == "O campo \"IMEI\" é obrigatório para telemóveis.");
    }

    [Theory]
    [InlineData("35693803564380", CurrentInventoryImportIssueCodes.InvalidImei)]
    [InlineData("3569380356438099", CurrentInventoryImportIssueCodes.InvalidImei)]
    [InlineData("ABC123", CurrentInventoryImportIssueCodes.InvalidImei)]
    [InlineData("35.693803/564380-9", CurrentInventoryImportIssueCodes.InvalidImei)]
    [InlineData(" - - ", CurrentInventoryImportIssueCodes.MissingImei)]
    public void MobilePhone_ImeiMustBe15Digits(string imei, string expectedCode)
    {
        var issues = new List<ImportIssue>();
        var fields = CurrentInventoryImportRowValidator.ValidateStructural(Stock("Telemóvel") with { SerialNumber = null, Imei = imei }, Today, issues);

        CurrentInventoryImportRowValidator.ValidateAgainstKind(EquipmentTypeKind.MobilePhone, fields, issues);

        var issue = Assert.Single(issues);
        Assert.Equal(expectedCode, issue.Code);
        if (expectedCode == CurrentInventoryImportIssueCodes.InvalidImei)
        {
            Assert.Equal("O campo \"IMEI\" deve conter exatamente 15 dígitos.", issue.Message);
        }
    }

    [Theory]
    [InlineData("356 938 035 643 809")]
    [InlineData("35-693803-564380-9")]
    public void MobilePhone_SeparatedImei_IsValid(string imei)
    {
        var issues = new List<ImportIssue>();
        var fields = CurrentInventoryImportRowValidator.ValidateStructural(Stock("Telemóvel") with { SerialNumber = null, Imei = imei }, Today, issues);

        CurrentInventoryImportRowValidator.ValidateAgainstKind(EquipmentTypeKind.MobilePhone, fields, issues);

        Assert.Empty(issues);
        Assert.Equal("356938035643809", fields.NormalizedImei);
    }

    [Fact]
    public void KindSpecificFields_OnOtherKinds_AreRejected()
    {
        var issues = new List<ImportIssue>();
        var fields = CurrentInventoryImportRowValidator.ValidateStructural(Stock() with { Processor = "i5", MobileNumber = "+351910000000" }, Today, issues);

        CurrentInventoryImportRowValidator.ValidateAgainstKind(EquipmentTypeKind.Generic, fields, issues);

        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.LaptopFieldsNotAllowed);
        Assert.Contains(issues, i => i.Code == CurrentInventoryImportIssueCodes.MobileFieldsNotAllowed);
    }

    [Fact]
    public void Formulas_AreRejected_WithThePtPtHeader()
    {
        var (_, issues) = Validate(Stock() with { FormulaFields = new HashSet<string> { CurrentInventoryImportColumns.SerialNumber.Field } });

        var issue = Assert.Single(issues);
        Assert.Equal(CurrentInventoryImportIssueCodes.FormulaNotAllowed, issue.Code);
        Assert.Contains("Número de série", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryMessage_IsPtPt_NeverTheInternalFieldName()
    {
        var (_, issues) = Validate(Stock() with { FullName = "x", Email = "bad", WorkforceType = "?" });

        // NIF / Email / IMEI are spelled the same as field key and pt-PT header; every other key must never leak.
        var leakable = issues.Where(i => CurrentInventoryImportColumns.HeaderFor(i.Field) != i.Field).ToList();
        Assert.NotEmpty(leakable);
        foreach (var issue in leakable)
        {
            Assert.DoesNotContain(issue.Field, issue.Message, StringComparison.Ordinal);
            Assert.Contains(CurrentInventoryImportColumns.HeaderFor(issue.Field), issue.Message, StringComparison.Ordinal);
        }
    }
}
