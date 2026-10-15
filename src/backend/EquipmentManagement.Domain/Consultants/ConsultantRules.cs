namespace EquipmentManagement.Domain.Consultants;

/// <summary>
/// Pure validation/normalization rules for <see cref="Consultant"/>
/// (Phase 2 scope). Framework-free so
/// these are unit-testable without a database.
/// </summary>
public static class ConsultantRules
{
    private const int MinNifDigits = 5;
    private const int MaxNifDigits = 20;

    public static bool IsValidFullName(string? fullName) => !string.IsNullOrWhiteSpace(fullName);

    /// <summary>Strips everything but digits and trims — a NIF has no case sensitivity to normalize.</summary>
    public static string NormalizeNif(string nif) => new([.. nif.Where(char.IsDigit)]);

    /// <summary>
    /// Deliberately structural-only: digits, reasonable length, nothing more.
    /// Not a Portuguese-NIF checksum validator and not fixed at 9 digits —
    /// docs/PROJECT_SPEC.md, section 14, explicitly leaves the foreign-NIF /
    /// exception policy to each deployment (section 32). Enforcing a
    /// strict Portuguese-only rule now could wrongly reject a legitimate foreign
    /// consultant before that policy exists. See ADR-023.
    /// </summary>
    public static bool IsValidNormalizedNif(string normalizedNif) =>
        normalizedNif.Length is >= MinNifDigits and <= MaxNifDigits;
}
