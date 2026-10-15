using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Infrastructure.DataImports;

namespace EquipmentManagement.UnitTests.Infrastructure.DataImports;

public class ConsultantImportRowValidatorTests
{
    private static ConsultantImportRowInput ValidInput() => new(
        RowNumber: 2,
        FullName: "  Maria Exemplo  ",
        Nif: "123456789",
        NifIsAmbiguousNumeric: false,
        Email: "maria@example.invalid",
        Address: "Rua Exemplo 1",
        Phone: "+351900000000",
        PhoneIsAmbiguousNumeric: false,
        WorkforceType: null,
        EngagementType: null,
        RoleOrFunction: null,
        CompanyName: null,
        AllocationStartDate: null,
        AllocationStartDateInvalid: false,
        AllocationEndDate: null,
        AllocationEndDateInvalid: false,
        FormulaFields: new HashSet<string>());

    [Fact]
    public void ValidateStructural_ValidConsultantOnlyRow_ProducesNoIssues()
    {
        var issues = new List<ImportIssue>();
        var fields = ConsultantImportRowValidator.ValidateStructural(ValidInput(), issues);

        Assert.Empty(issues);
        Assert.Equal("Maria Exemplo", fields.FullName);
        Assert.Equal("123456789", fields.Nif);
        Assert.False(fields.HasAllocation);
    }

    [Fact]
    public void ValidateStructural_MissingFullName_ReportsError()
    {
        var input = ValidInput() with { FullName = "   " };
        var issues = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(input, issues);

        Assert.Contains(issues, i => i.Code == ConsultantImportIssueCodes.MissingFullName && i.Severity == ImportIssueSeverity.Error);
    }

    [Fact]
    public void ValidateStructural_InvalidNif_ReportsError()
    {
        var input = ValidInput() with { Nif = "ab" };
        var issues = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(input, issues);

        Assert.Contains(issues, i => i.Code == ConsultantImportIssueCodes.InvalidNif);
    }

    [Fact]
    public void ValidateStructural_AmbiguousNumericNif_ReportsNumericIdentifierRejected()
    {
        var input = ValidInput() with { NifIsAmbiguousNumeric = true };
        var issues = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(input, issues);

        Assert.Contains(issues, i => i.Code == ConsultantImportIssueCodes.NumericIdentifierRejected && i.Field == "NIF");
    }

    [Fact]
    public void ValidateStructural_AmbiguousNumericPhone_ReportsNumericIdentifierRejected()
    {
        // Phone gets the exact same leading-zero/scientific-notation protection as NIF/SerialNumber (Phase 8 scope).
        var input = ValidInput() with { PhoneIsAmbiguousNumeric = true };
        var issues = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(input, issues);

        Assert.Contains(issues, i => i.Code == ConsultantImportIssueCodes.NumericIdentifierRejected && i.Field == "Phone");
        Assert.DoesNotContain(issues, i => i.Code == ConsultantImportIssueCodes.MissingPhone);
    }

    [Fact]
    public void ValidateStructural_FormulaCell_ReportsFormulaNotAllowed()
    {
        var input = ValidInput() with { FormulaFields = new HashSet<string> { "FullName" } };
        var issues = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(input, issues);

        Assert.Contains(issues, i => i.Code == ConsultantImportIssueCodes.FormulaNotAllowed && i.Field == "FullName");
    }

    [Fact]
    public void ValidateStructural_PartialAllocationBlock_RequiresTheWholeBlock()
    {
        // Only RoleOrFunction supplied — the block is "present" so WorkforceType/EngagementType/StartDate become required.
        var input = ValidInput() with { RoleOrFunction = "Consultant" };
        var issues = new List<ImportIssue>();
        var fields = ConsultantImportRowValidator.ValidateStructural(input, issues);

        Assert.True(fields.HasAllocation);
        Assert.Contains(issues, i => i.Code == ConsultantImportIssueCodes.MissingWorkforceType);
        Assert.Contains(issues, i => i.Code == ConsultantImportIssueCodes.MissingEngagementType);
        Assert.Contains(issues, i => i.Code == ConsultantImportIssueCodes.MissingAllocationStartDate);
    }

    [Fact]
    public void ValidateStructural_ExternalWithoutCompany_ReportsMissingCompany()
    {
        var input = ValidInput() with
        {
            WorkforceType = "External",
            EngagementType = "ServiceProvider",
            RoleOrFunction = "Consultant",
            AllocationStartDate = new DateOnly(2026, 1, 15),
        };
        var issues = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(input, issues);

        Assert.Contains(issues, i => i.Code == ConsultantImportIssueCodes.MissingCompany);
    }

    [Fact]
    public void ValidateStructural_InternalWithCompany_ReportsCompanyNotAllowed()
    {
        var input = ValidInput() with
        {
            WorkforceType = "Internal",
            EngagementType = "EmploymentContract",
            RoleOrFunction = "Consultant",
            CompanyName = "Example Client",
            AllocationStartDate = new DateOnly(2026, 1, 15),
        };
        var issues = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(input, issues);

        Assert.Contains(issues, i => i.Code == ConsultantImportIssueCodes.CompanyNotAllowedForInternal);
    }

    [Theory]
    [InlineData("internal", WorkforceType.Internal)]
    [InlineData("EXTERNAL", WorkforceType.External)]
    [InlineData(" External ", WorkforceType.External)]
    public void ValidateStructural_WorkforceType_CaseInsensitiveAndTrimmed(string raw, WorkforceType expected)
    {
        var input = ValidInput() with
        {
            WorkforceType = raw,
            EngagementType = "ServiceProvider",
            RoleOrFunction = "Consultant",
            CompanyName = expected == WorkforceType.External ? "Example Client" : null,
            AllocationStartDate = new DateOnly(2026, 1, 15),
        };
        var issues = new List<ImportIssue>();
        var fields = ConsultantImportRowValidator.ValidateStructural(input, issues);

        Assert.Equal(expected, fields.WorkforceType);
    }

    [Fact]
    public void ValidateStructural_UnrecognizedWorkforceType_ReportsInvalidWorkforceType()
    {
        // "Interno" used to be the example here: it is now the canonical pt-PT value (ADR-070), so an unknown word stands in.
        var input = ValidInput() with { WorkforceType = "Banana", EngagementType = "ServiceProvider", RoleOrFunction = "Consultant", AllocationStartDate = new DateOnly(2026, 1, 15) };
        var issues = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(input, issues);

        Assert.Contains(issues, i => i.Code == ConsultantImportIssueCodes.InvalidWorkforceType);
    }

    [Fact]
    public void ValidateStructural_InvalidAllocationDate_ReportsInvalidAllocationDate()
    {
        var input = ValidInput() with
        {
            WorkforceType = "Internal",
            EngagementType = "ServiceProvider",
            RoleOrFunction = "Consultant",
            AllocationStartDateInvalid = true,
        };
        var issues = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(input, issues);

        Assert.Contains(issues, i => i.Code == ConsultantImportIssueCodes.InvalidAllocationDate && i.Field == "AllocationStartDate");
    }

    [Fact]
    public void ValidateStructural_EndDateBeforeStartDate_ReportsInvalidAllocationDate()
    {
        var input = ValidInput() with
        {
            WorkforceType = "Internal",
            EngagementType = "ServiceProvider",
            RoleOrFunction = "Consultant",
            AllocationStartDate = new DateOnly(2026, 6, 1),
            AllocationEndDate = new DateOnly(2026, 1, 1),
        };
        var issues = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(input, issues);

        Assert.Contains(issues, i => i.Code == ConsultantImportIssueCodes.InvalidAllocationDate && i.Field == "AllocationEndDate");
    }

    [Fact]
    public void ValidateStructural_BlankEndDate_IsOpenEndedAndValid()
    {
        var input = ValidInput() with
        {
            WorkforceType = "Internal",
            EngagementType = "ServiceProvider",
            RoleOrFunction = "Consultant",
            AllocationStartDate = new DateOnly(2026, 1, 15),
        };
        var issues = new List<ImportIssue>();
        var fields = ConsultantImportRowValidator.ValidateStructural(input, issues);

        Assert.Empty(issues);
        Assert.Null(fields.AllocationEndDate);
    }
}
