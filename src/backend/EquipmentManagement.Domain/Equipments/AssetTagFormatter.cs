namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// Pure formatting for the human-readable AssetTag (docs/PROJECT_SPEC.md,
/// section 7, example "LT-0023"). Deliberately separated from the actual
/// sequence-number source (a Postgres sequence — see
/// <c>Infrastructure.Equipments.AssetTagGenerator</c> and the corresponding
/// ADR) so the formatting rule itself is unit-testable without a database.
/// </summary>
public static class AssetTagFormatter
{
    public static string Format(string normalizedPrefix, long sequenceValue) =>
        $"{normalizedPrefix}-{sequenceValue:D4}";
}
