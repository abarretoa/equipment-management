namespace EquipmentManagement.Domain.DataImports;

/// <summary>Derives a row's overall status from its accumulated issues — Error if any issue is Error-severity, else Warning if any issue exists, else Valid.</summary>
public static class ImportRowStatusCalculator
{
    public static ImportRowStatus Compute(IReadOnlyCollection<ImportIssue> issues)
    {
        if (issues.Count == 0)
        {
            return ImportRowStatus.Valid;
        }

        return issues.Any(i => i.Severity == ImportIssueSeverity.Error) ? ImportRowStatus.Error : ImportRowStatus.Warning;
    }
}
