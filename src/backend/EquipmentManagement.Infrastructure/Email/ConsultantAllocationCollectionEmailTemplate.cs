namespace EquipmentManagement.Infrastructure.Email;

/// <summary>
/// The product's equipment-collection notification copy, sent after
/// HR ends a <c>ConsultantAllocation</c> as end-of-engagement with the
/// Consultant still holding active equipment. Deliberately neutral —
/// never mentions the internal reason the engagement ended (dismissal,
/// resignation, contract end, etc.) — and never includes the Consultant's
/// stored address/NIF or any other unnecessary PII, same discipline as
/// <see cref="EquipmentDispatchEmailTemplate"/>.
/// </summary>
public static class ConsultantAllocationCollectionEmailTemplate
{
    public static (string Subject, string Body) Build(
        string consultantFullName, IReadOnlyList<DispatchEquipmentLine> items, DateOnly expectedFromDate, DateOnly? expectedToDate)
    {
        var subject = items.Count == 1
            ? "Recolha de equipamento agendada"
            : "Recolha de equipamentos agendada";

        var period = FormatPeriod(expectedFromDate, expectedToDate);
        var equipmentLabel = items.Count == 1 ? "do equipamento abaixo" : "dos equipamentos abaixo";
        var equipmentLines = string.Join("\n", items.Select(FormatLine));

        var body =
            $"""
             Olá, {consultantFullName},

             Esperamos que te encontres bem 😊

             Informamos que está prevista a recolha {equipmentLabel} {period}.

             {equipmentLines}

             A transportadora irá deslocar-se para efetuar a recolha.

             Pedimos, por favor, que estejas disponível para a recolha no período indicado e que os equipamentos estejam devidamente acondicionados.

             Obrigada pela colaboração!
             """;

        return (subject, body);
    }

    /// <summary>Completes "está prevista a recolha … {period}" — the sentence already says "prevista", so the period must not repeat it.</summary>
    private static string FormatPeriod(DateOnly fromDate, DateOnly? toDate) => toDate is { } to && to != fromDate
        ? $"entre {Documents.DocumentCulture.FormatDate(fromDate)} e {Documents.DocumentCulture.FormatDate(to)}"
        : $"para o dia {Documents.DocumentCulture.FormatDate(fromDate)}";

    /// <summary>Deliberately narrower than the dispatch email's line format — ID/Type/Manufacturer/Model only, no serial number/IMEI (not required by the client copy for collection).</summary>
    private static string FormatLine(DispatchEquipmentLine item)
    {
        var descriptor = string.Join(" ", new[] { item.Manufacturer, item.Model }.Where(v => !string.IsNullOrWhiteSpace(v)));
        return $"- {item.EquipmentTypeName} · {(string.IsNullOrWhiteSpace(descriptor) ? item.AssetTag : descriptor)} · {item.AssetTag}";
    }
}
