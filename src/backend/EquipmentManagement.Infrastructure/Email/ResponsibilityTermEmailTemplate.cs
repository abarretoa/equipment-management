namespace EquipmentManagement.Infrastructure.Email;

/// <summary>
/// The single, centralized source of the Responsibility Term delivery
/// email's wording — the product's communication style. No
/// controller/service scatters its own copy of this text. pt-PT only,
/// explicit culture is not needed here (no dates are formatted). Deliberately
/// minimizes PII: no NIF, address, or phone in the body — only a concise
/// equipment identifier, never full specifications.
/// </summary>
public static class ResponsibilityTermEmailTemplate
{
    public static (string Subject, string Body) Build(string documentNumber, string consultantFullName, IReadOnlyList<string> equipmentDescriptions)
    {
        var subject = $"Termo de Responsabilidade de Equipamentos: {documentNumber}";
        var equipamento = string.Join("\n", equipmentDescriptions);

        var body =
            $"""
             Olá, {consultantFullName},

             Esperamos que te encontres bem 😊

             Enviamos em anexo o Termo de Responsabilidade relativo à entrega do equipamento {equipamento}, para tua assinatura.

             Pedimos, por favor, que nos devolvas o documento devidamente assinado após a sua validação.

             Obrigada!
             """;

        return (subject, body);
    }
}
