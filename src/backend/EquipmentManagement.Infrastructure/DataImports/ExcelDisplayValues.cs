using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// The pt-PT text an Excel workbook shows for an internal enum value, and the
/// way back for the values an import accepts (ADR-070). The labels are the
/// ones the application's own screens already use for the same values; the
/// enum members themselves are untouched. Nothing here is a general-purpose
/// translation layer — it covers exactly the enum values that reach a
/// generated or uploaded workbook.
/// </summary>
public static class ExcelDisplayValues
{
    public const string Yes = "Sim";
    public const string No = "Não";

    /// <summary>Template dropdown order.</summary>
    public static readonly IReadOnlyList<string> WorkforceTypeLabels = [Label(WorkforceType.Internal), Label(WorkforceType.External)];

    /// <summary>Template dropdown order.</summary>
    public static readonly IReadOnlyList<string> EngagementTypeLabels = [Label(EngagementType.ServiceProvider), Label(EngagementType.EmploymentContract)];

    public static string Label(WorkforceType value) => value switch
    {
        WorkforceType.Internal => "Interno",
        WorkforceType.External => "Externo",
        _ => value.ToString(),
    };

    public static string Label(EngagementType value) => value switch
    {
        EngagementType.ServiceProvider => "Prestador de serviço",
        EngagementType.EmploymentContract => "Contrato de trabalho",
        _ => value.ToString(),
    };

    public static string Label(AllocationStatus value) => value switch
    {
        AllocationStatus.Active => "Ativa",
        AllocationStatus.Ended => "Terminada",
        _ => value.ToString(),
    };

    public static string Label(EquipmentTypeKind value) => value switch
    {
        EquipmentTypeKind.Generic => "Genérico",
        EquipmentTypeKind.Laptop => "Portátil",
        EquipmentTypeKind.MobilePhone => "Telemóvel",
        _ => value.ToString(),
    };

    public static string Label(EquipmentStatus value) => value switch
    {
        EquipmentStatus.Available => "Disponível",
        EquipmentStatus.InTransit => "Em trânsito",
        EquipmentStatus.Assigned => "Atribuído",
        EquipmentStatus.ReturnPending => "Devolução pendente",
        EquipmentStatus.UnderInspection => "Em inspeção",
        EquipmentStatus.Damaged => "Avariado",
        EquipmentStatus.UnderMaintenance => "Em manutenção",
        EquipmentStatus.Retired => "Abatido",
        _ => value.ToString(),
    };

    public static string Label(bool value) => value ? Yes : No;

    /// <summary>
    /// Accepts the pt-PT label or the enum member's English name — the only
    /// spelling accepted before the workbook was localized — trimmed and
    /// case-insensitive. Anything else, including a number, is not a value.
    /// </summary>
    public static bool TryParseWorkforceType(string? text, out WorkforceType value) =>
        TryParse(text, Label, out value);

    /// <inheritdoc cref="TryParseWorkforceType"/>
    public static bool TryParseEngagementType(string? text, out EngagementType value) =>
        TryParse(text, Label, out value);

    private static bool TryParse<TEnum>(string? text, Func<TEnum, string> label, out TEnum value)
        where TEnum : struct, Enum
    {
        var candidate = ImportColumn.NormalizeHeader(text);
        foreach (var member in Enum.GetValues<TEnum>())
        {
            if (candidate == ImportColumn.NormalizeHeader(label(member)) ||
                string.Equals(candidate, member.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                value = member;
                return true;
            }
        }

        value = default;
        return false;
    }
}
