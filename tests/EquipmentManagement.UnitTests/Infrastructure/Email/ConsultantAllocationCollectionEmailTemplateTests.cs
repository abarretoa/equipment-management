using EquipmentManagement.Infrastructure.Email;

namespace EquipmentManagement.UnitTests.Infrastructure.Email;

public class ConsultantAllocationCollectionEmailTemplateTests
{
    private static readonly DispatchEquipmentLine[] ThreeItems =
    [
        new("LT-0012", "Portátil", "Dell", "Latitude 5440", null, null),
        new("MN-0008", "Monitor", "Dell", "P2422H", null, null),
        new("HS-0014", "Headset", "Jabra", "Evolve2", null, null),
    ];

    [Fact]
    public void Build_WithACollectionWindow_StatesThePeriodOnce()
    {
        var (subject, body) = ConsultantAllocationCollectionEmailTemplate.Build(
            "Diana Demo", ThreeItems, new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 5));

        Assert.Equal("Recolha de equipamentos agendada", subject);
        Assert.Contains("Informamos que está prevista a recolha dos equipamentos abaixo entre 03/10/2026 e 05/10/2026.", body);
        Assert.Equal(1, CountOccurrences(body, "prevista"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2026-10-03")] // A window that ends the day it starts is a single day.
    public void Build_WithASingleDate_StatesTheDayOnce(string? toDate)
    {
        var (_, body) = ConsultantAllocationCollectionEmailTemplate.Build(
            "Diana Demo", ThreeItems, new DateOnly(2026, 10, 3), toDate is null ? null : DateOnly.Parse(toDate, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Contains("Informamos que está prevista a recolha dos equipamentos abaixo para o dia 03/10/2026.", body);
        Assert.Equal(1, CountOccurrences(body, "prevista"));
    }

    [Fact]
    public void Build_WithOneItem_UsesTheSingularWording()
    {
        var (subject, body) = ConsultantAllocationCollectionEmailTemplate.Build(
            "Diana Demo", ThreeItems[..1], new DateOnly(2026, 10, 3), null);

        Assert.Equal("Recolha de equipamento agendada", subject);
        Assert.Contains("Informamos que está prevista a recolha do equipamento abaixo para o dia 03/10/2026.", body);
    }

    [Fact]
    public void Build_ListsEveryItemExactlyOnce_AndKeepsTheRestOfTheApprovedCopy()
    {
        var (_, body) = ConsultantAllocationCollectionEmailTemplate.Build(
            "Diana Demo", ThreeItems, new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 5));

        var lines = body.Split('\n').Select(l => l.TrimEnd()).ToList();
        Assert.Equal(
            ["- Portátil · Dell Latitude 5440 · LT-0012", "- Monitor · Dell P2422H · MN-0008", "- Headset · Jabra Evolve2 · HS-0014"],
            lines.Where(l => l.StartsWith("- ", StringComparison.Ordinal)));

        Assert.StartsWith("Olá, Diana Demo,", body);
        Assert.Contains("Esperamos que te encontres bem", body);
        Assert.Contains("A transportadora irá deslocar-se para efetuar a recolha.", body);
        Assert.Contains(
            "Pedimos, por favor, que estejas disponível para a recolha no período indicado e que os equipamentos estejam devidamente acondicionados.",
            body);
        Assert.EndsWith("Obrigada pela colaboração!", body.TrimEnd());
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
