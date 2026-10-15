namespace EquipmentManagement.Domain.ResponsibilityTerms;

/// <summary>
/// Pure formatting for the human-readable, legally-identifying document number
/// (e.g. "TR-2026-000123" — Phase 6 scope). Deliberately separated
/// from the actual sequence-number source (a single, global Postgres sequence —
/// see <c>Infrastructure.ResponsibilityTerms.DocumentNumberGenerator</c>) so
/// the formatting rule itself is unit-testable without a database, mirroring
/// <c>Domain.Equipments.AssetTagFormatter</c>'s exact pattern (ADR-026).
/// </summary>
public static class DocumentNumberFormatter
{
    /// <summary>
    /// The year segment is the calendar year at generation time, not a
    /// per-year-reset counter — the sequence itself never resets, so numbers
    /// are not "gapless within a year." A numbering gap is harmless; a
    /// duplicate document number is not — the same trade-off already made for
    /// AssetTag (ADR-026).
    /// </summary>
    public static string Format(int year, long sequenceValue) => $"TR-{year:D4}-{sequenceValue:D6}";
}
