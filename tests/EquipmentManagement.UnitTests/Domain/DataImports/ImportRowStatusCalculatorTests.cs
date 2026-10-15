using EquipmentManagement.Domain.DataImports;

namespace EquipmentManagement.UnitTests.Domain.DataImports;

public class ImportRowStatusCalculatorTests
{
    [Fact]
    public void Compute_NoIssues_ReturnsValid()
    {
        Assert.Equal(ImportRowStatus.Valid, ImportRowStatusCalculator.Compute([]));
    }

    [Fact]
    public void Compute_OnlyWarnings_ReturnsWarning()
    {
        var issues = new[] { new ImportIssue("Code", "Field", "Message", ImportIssueSeverity.Warning) };
        Assert.Equal(ImportRowStatus.Warning, ImportRowStatusCalculator.Compute(issues));
    }

    [Fact]
    public void Compute_AnyError_ReturnsError()
    {
        var issues = new[]
        {
            new ImportIssue("Code1", "Field1", "Message1", ImportIssueSeverity.Warning),
            new ImportIssue("Code2", "Field2", "Message2", ImportIssueSeverity.Error),
        };
        Assert.Equal(ImportRowStatus.Error, ImportRowStatusCalculator.Compute(issues));
    }
}
