using EquipmentManagement.Domain.DataImports;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// The pt-PT wording shared by both imports' row messages. A message always
/// names a field by the header the workbook shows for it (ADR-070) — never by
/// its internal key, which only travels in <see cref="ImportIssue.Field"/>.
/// </summary>
public static class ImportMessages
{
    public const string DateFormatHint = "DD/MM/AAAA";

    public static string Required(string header) => $"O campo \"{header}\" é obrigatório.";

    public static string RequiredWithAllocation(string header) =>
        $"O campo \"{header}\" é obrigatório quando são indicados dados de alocação.";

    public static string RequiredForLaptops(string header) => $"O campo \"{header}\" é obrigatório para portáteis.";

    public static string FormulaNotAllowed(string header) => $"O campo \"{header}\" contém uma fórmula. As fórmulas não são aceites.";

    public static string TooLong(string header) => $"O valor indicado em \"{header}\" é demasiado longo.";

    public static string StoredAsNumber(string header) =>
        $"O valor de \"{header}\" foi guardado como número e pode ter perdido zeros à esquerda. Formate a coluna como Texto e introduza o valor novamente.";

    public static string InvalidChoice(string header, string value, IEnumerable<string> accepted) =>
        $"O valor \"{value}\" indicado em \"{header}\" não é válido. Valores aceites: {string.Join(", ", accepted)}.";

    public static string EquipmentTypeNotImportable(string equipmentTypeName) =>
        $"O tipo de equipamento \"{equipmentTypeName}\" não pode ser importado através deste ficheiro. Registe este equipamento diretamente na plataforma.";

    public static string InvalidDate(string header) =>
        $"O valor indicado em \"{header}\" não é uma data válida. Utilize o formato {DateFormatHint}.";
}
