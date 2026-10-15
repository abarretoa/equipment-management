using EquipmentManagement.Infrastructure.Documents;

namespace EquipmentManagement.Infrastructure.Email;

/// <summary>
/// The product's equipment-dispatch notification copy — plain string
/// interpolation, pt-PT only, same pattern as
/// <c>ResponsibilityTermEmailTemplate</c>/<c>EquipmentRetirementRequestEmailTemplate</c>.
/// Deliberately excludes PIN/PUK/passwords/tokens/NIF/address — only
/// equipment identification useful to the Consultant.
/// </summary>
public static class EquipmentDispatchEmailTemplate
{
    public static (string Subject, string Body) Build(
        string consultantFullName, IReadOnlyList<DispatchEquipmentLine> items, DateOnly expectedFromDate, DateOnly? expectedToDate)
    {
        var subject = items.Count == 1
            ? "Entrega de equipamento agendada"
            : "Entrega de equipamentos agendada";

        var period = FormatPeriod(expectedFromDate, expectedToDate);
        var equipmentLabel = items.Count == 1 ? "do equipamento abaixo" : "dos equipamentos abaixo";
        var equipmentLines = string.Join("\n", items.Select(FormatLine));

        var body =
            $"""
             Olá, {consultantFullName},

             Esperamos que te encontres bem 😊

             Informamos que está prevista a entrega {equipmentLabel} {period}.

             {equipmentLines}

             Pedimos, por favor, que estejas disponível para a entrega no período indicado.

             Obrigada pela colaboração!
             """;

        return (subject, body);
    }

    /// <summary>Completes "está prevista a entrega … {period}" — the sentence already says "prevista", so the period must not repeat it.</summary>
    private static string FormatPeriod(DateOnly fromDate, DateOnly? toDate) => toDate is { } to && to != fromDate
        ? $"entre {DocumentCulture.FormatDate(fromDate)} e {DocumentCulture.FormatDate(to)}"
        : $"para o dia {DocumentCulture.FormatDate(fromDate)}";

    private static string FormatLine(DispatchEquipmentLine item)
    {
        var descriptor = string.Join(" ", new[] { item.Manufacturer, item.Model }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var line = $"- {item.EquipmentTypeName} · {(string.IsNullOrWhiteSpace(descriptor) ? item.AssetTag : descriptor)} · {item.AssetTag}";

        if (!string.IsNullOrWhiteSpace(item.SerialNumber))
        {
            line += $" (Nº de Série: {item.SerialNumber})";
        }
        else if (!string.IsNullOrWhiteSpace(item.Imei))
        {
            line += $" (IMEI: {item.Imei})";
        }

        return line;
    }
}

/// <summary>One equipment line for the dispatch/collection notification emails — identification only, never IMEI's SIM/PIN/PUK siblings or any consultant PII.</summary>
public sealed record DispatchEquipmentLine(
    string AssetTag, string EquipmentTypeName, string? Manufacturer, string? Model, string? SerialNumber, string? Imei);
