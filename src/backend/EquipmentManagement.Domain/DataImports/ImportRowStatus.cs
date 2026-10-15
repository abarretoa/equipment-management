namespace EquipmentManagement.Domain.DataImports;

/// <summary>A row's overall preview outcome, derived from its <see cref="ImportIssue"/> list — Error if any issue is Error-severity, else Warning if any issue exists, else Valid.</summary>
public enum ImportRowStatus
{
    Valid,
    Warning,
    Error,
}

/// <summary>
/// Only two severities (Phase 8 scope). Errors block confirmation
/// of the whole session (Phase 8 scope's all-or-nothing model);
/// warnings represent non-blocking normalization only, never uncertain
/// business interpretation — nothing here ever "guesses."
/// </summary>
public enum ImportIssueSeverity
{
    Warning,
    Error,
}

/// <summary>One row-level diagnostic. <see cref="Code"/> is a stable, machine-readable identifier — see <c>ConsultantImportIssueCodes</c>/<c>EquipmentImportIssueCodes</c>.</summary>
public sealed record ImportIssue(string Code, string Field, string Message, ImportIssueSeverity Severity);
