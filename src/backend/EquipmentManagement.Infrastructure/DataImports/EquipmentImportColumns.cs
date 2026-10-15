namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// The Equipment import's columns, in template order. The pt-PT headers
/// follow the labels of the Equipment form; each column still accepts the
/// English header of the pre-localization template (ADR-070). Every column
/// header is required, exactly as before — only the cells may be blank.
/// </summary>
public static class EquipmentImportColumns
{
    public static readonly ImportColumn EquipmentType = new("EquipmentType", "Tipo de equipamento", IsHeaderRequired: true, ["EquipmentType"]);
    public static readonly ImportColumn Manufacturer = new("Manufacturer", "Fabricante", IsHeaderRequired: true, ["Manufacturer"]);
    public static readonly ImportColumn Model = new("Model", "Modelo", IsHeaderRequired: true, ["Model"]);
    public static readonly ImportColumn SerialNumber = new("SerialNumber", "Número de série", IsHeaderRequired: true, ["SerialNumber"]);
    public static readonly ImportColumn Notes = new("Notes", "Observações", IsHeaderRequired: true, ["Notes"]);
    public static readonly ImportColumn Processor = new("Processor", "Processador", IsHeaderRequired: true, ["Processor"]);
    public static readonly ImportColumn Memory = new("Memory", "Memória RAM", IsHeaderRequired: true, ["Memory"]);
    public static readonly ImportColumn Storage = new("Storage", "Armazenamento", IsHeaderRequired: true, ["Storage"]);

    public static readonly IReadOnlyList<ImportColumn> All = [EquipmentType, Manufacturer, Model, SerialNumber, Notes, Processor, Memory, Storage];

    /// <summary>The pt-PT header for an internal field key — what a message about that field must call it.</summary>
    public static string HeaderFor(string field) => All.FirstOrDefault(c => c.Field == field)?.Header ?? field;
}
