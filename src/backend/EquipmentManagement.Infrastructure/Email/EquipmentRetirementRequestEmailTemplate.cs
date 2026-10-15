using EquipmentManagement.Infrastructure.Documents;
using EquipmentManagement.Infrastructure.Equipments;

namespace EquipmentManagement.Infrastructure.Email;

/// <summary>
/// The single, centralized source of the "new retirement request" notification
/// wording sent to Administration when HR submits a request. pt-PT only, same
/// discipline as <see cref="ResponsibilityTermEmailTemplate"/>. Notification
/// only — no approve/reject action, token, or magic link is ever embedded; the
/// CTA is a plain navigation URL the recipient must open and authenticate
/// against. Deliberately minimizes PII: no consultant NIF/address/phone/email,
/// no requester email — display name only.
///
/// The request timestamp is formatted with <see cref="DocumentCulture.FormatDateTimeUtc"/>
/// — the same explicit-UTC-label convention already used for
/// <c>ResponsibilityTermPdfGenerator</c>'s "Documento gerado em" line — rather
/// than converting to a guessed recipient timezone. The persisted value stays
/// UTC either way; this only changes how it is displayed, avoiding the exact
/// silent-mismatch confusion an unlabeled backend-rendered time would cause
/// against the frontend's browser-local display of the same instant.
/// </summary>
public static class EquipmentRetirementRequestEmailTemplate
{
    public static (string Subject, string Body) Build(
        string equipmentCode,
        string equipmentTypeName,
        string? manufacturer,
        string? model,
        LaptopSpecificationRecord? laptopSpecification,
        string requestReason,
        string requesterDisplayName,
        DateTime requestedAtUtc,
        string? reviewUrl)
    {
        var subject = $"Nova solicitação de abate: {equipmentCode}";

        var equipmentLines = new List<string> { $"ID/Código do equipamento: {equipmentCode}", $"Tipo: {equipmentTypeName}" };
        if (!string.IsNullOrWhiteSpace(manufacturer))
        {
            equipmentLines.Add($"Marca/Fabricante: {manufacturer}");
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            equipmentLines.Add($"Modelo: {model}");
        }

        if (laptopSpecification is not null)
        {
            equipmentLines.Add($"Processador: {laptopSpecification.Processor}");
            equipmentLines.Add($"Memória: {laptopSpecification.Memory}");
            equipmentLines.Add($"Armazenamento: {laptopSpecification.Storage}");
        }

        var ctaSection = reviewUrl is null
            ? ""
            : $"""


               Analisar solicitação:
               {reviewUrl}
               """;

        var body =
            $"""
             Nova solicitação de abate de equipamento

             Foi registada uma nova solicitação de abate e está a aguardar análise da Administração.

             Equipamento:
             {string.Join('\n', equipmentLines)}

             Motivo da solicitação:
             {requestReason}

             Solicitação de abate pedida por:
             {requesterDisplayName}

             Data da solicitação:
             {DocumentCulture.FormatDateTimeUtc(requestedAtUtc)}
             """ + ctaSection;

        return (subject, body);
    }
}
