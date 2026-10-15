namespace EquipmentManagement.Domain.Companies;

/// <summary>
/// Pure validation/normalization rules for <see cref="Company"/>
/// (Phase 2 scope). Framework-free so
/// these are unit-testable without a database.
/// </summary>
public static class CompanyRules
{
    /// <summary>
    /// Trims and upper-invariants the name for case-insensitive duplicate
    /// comparison. Deliberately simple (no fuzzy matching, no removing legal
    /// suffixes like "Lda"/"SA") — see ADR-019 for why
    /// a stricter normalized-equality match is the chosen strategy, not
    /// approximate matching.
    /// </summary>
    public static string NormalizeName(string name) =>
        string.Join(' ', name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name);
}
