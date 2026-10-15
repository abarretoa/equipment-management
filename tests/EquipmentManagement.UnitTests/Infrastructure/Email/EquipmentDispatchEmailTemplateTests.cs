using EquipmentManagement.Infrastructure.Email;

namespace EquipmentManagement.UnitTests.Infrastructure.Email;

public class EquipmentDispatchEmailTemplateTests
{
    private static readonly DispatchEquipmentLine[] ThreeItems =
    [
        new("LT-0012", "Portátil", "Dell", "Latitude 5440", "SN12345", null),
        new("TM-0003", "Telemóvel", "Samsung", "Galaxy A05s", null, "356938035643809"),
        new("HS-0014", "Headset", null, null, null, null),
    ];

    [Fact]
    public void Build_WithADeliveryWindow_StatesThePeriodOnce()
    {
        var (subject, body) = EquipmentDispatchEmailTemplate.Build(
            "Diana Demo", ThreeItems, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3));

        Assert.Equal("Entrega de equipamentos agendada", subject);
        Assert.Contains("Informamos que está prevista a entrega dos equipamentos abaixo entre 01/10/2026 e 03/10/2026.", body);
        Assert.Equal(1, CountOccurrences(body, "prevista"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2026-10-01")] // A window that ends the day it starts is a single day.
    public void Build_WithASingleDate_StatesTheDayOnce(string? toDate)
    {
        var (_, body) = EquipmentDispatchEmailTemplate.Build(
            "Diana Demo", ThreeItems, new DateOnly(2026, 10, 1), toDate is null ? null : DateOnly.Parse(toDate, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Contains("Informamos que está prevista a entrega dos equipamentos abaixo para o dia 01/10/2026.", body);
        Assert.Equal(1, CountOccurrences(body, "prevista"));
    }

    [Fact]
    public void Build_WithOneItem_UsesTheSingularWording()
    {
        var (subject, body) = EquipmentDispatchEmailTemplate.Build("Diana Demo", ThreeItems[..1], new DateOnly(2026, 10, 1), null);

        Assert.Equal("Entrega de equipamento agendada", subject);
        Assert.Contains("Informamos que está prevista a entrega do equipamento abaixo para o dia 01/10/2026.", body);
        Assert.Equal(1, CountOccurrences(body, "prevista"));
    }

    [Fact]
    public void Build_ListsEveryItemExactlyOnce_AndKeepsTheRestOfTheApprovedCopy()
    {
        var (_, body) = EquipmentDispatchEmailTemplate.Build(
            "Diana Demo", ThreeItems, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3));

        // The equipment list is untouched: serial number, else IMEI, else neither;
        // and the asset tag stands in when there is no manufacturer/model.
        var lines = body.Split('\n').Select(l => l.TrimEnd()).ToList();
        Assert.Equal(
            [
                "- Portátil · Dell Latitude 5440 · LT-0012 (Nº de Série: SN12345)",
                "- Telemóvel · Samsung Galaxy A05s · TM-0003 (IMEI: 356938035643809)",
                "- Headset · HS-0014 · HS-0014",
            ],
            lines.Where(l => l.StartsWith("- ", StringComparison.Ordinal)));

        // The whole body, so nothing else in the approved copy can drift unnoticed.
        Assert.Equal(
            string.Join("\n",
                "Olá, Diana Demo,",
                "",
                "Esperamos que te encontres bem 😊",
                "",
                "Informamos que está prevista a entrega dos equipamentos abaixo entre 01/10/2026 e 03/10/2026.",
                "",
                "- Portátil · Dell Latitude 5440 · LT-0012 (Nº de Série: SN12345)",
                "- Telemóvel · Samsung Galaxy A05s · TM-0003 (IMEI: 356938035643809)",
                "- Headset · HS-0014 · HS-0014",
                "",
                "Pedimos, por favor, que estejas disponível para a entrega no período indicado.",
                "",
                "Obrigada pela colaboração!"),
            body.ReplaceLineEndings("\n"));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
