namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// Pure validation for <see cref="LaptopSpecification"/>
/// (docs/PROJECT_SPEC.md, section 7: "para portáteis, as especificações
/// obrigatórias incluem processador, memória RAM e armazenamento").
/// </summary>
public static class LaptopSpecificationRules
{
    public static bool IsComplete(string? processor, string? memory, string? storage) =>
        !string.IsNullOrWhiteSpace(processor) &&
        !string.IsNullOrWhiteSpace(memory) &&
        !string.IsNullOrWhiteSpace(storage);
}
