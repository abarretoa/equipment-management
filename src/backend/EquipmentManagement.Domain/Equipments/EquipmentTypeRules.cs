namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// Pure validation/normalization rules for <see cref="EquipmentType"/>
/// (Phase 3 scope). Framework-free so
/// these are unit-testable without a database.
/// </summary>
public static class EquipmentTypeRules
{
    private const int MinPrefixLength = 2;
    private const int MaxPrefixLength = 6;

    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name);

    /// <summary>Same normalization strategy as Company (trim, collapse whitespace, upper-invariant) — see ADR-019.</summary>
    public static string NormalizeName(string name) =>
        string.Join(' ', name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    /// <summary>Trims and upper-invariants the AssetTag prefix (e.g. "lt" → "LT").</summary>
    public static string NormalizePrefix(string prefix) => prefix.Trim().ToUpperInvariant();

    /// <summary>
    /// A short, purely alphabetic prefix (docs/PROJECT_SPEC.md, section 7
    /// example: "LT"). Deliberately not required to be unique across types —
    /// the full AssetTag (prefix + sequence number) is unique regardless; a
    /// shared prefix is only a minor readability tradeoff for whoever chose it.
    /// </summary>
    public static bool IsValidPrefix(string normalizedPrefix) =>
        normalizedPrefix.Length is >= MinPrefixLength and <= MaxPrefixLength &&
        normalizedPrefix.All(c => c is >= 'A' and <= 'Z');
}
