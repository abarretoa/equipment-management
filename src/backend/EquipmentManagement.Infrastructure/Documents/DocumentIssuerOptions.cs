namespace EquipmentManagement.Infrastructure.Documents;

/// <summary>
/// The issuing company's identity as it should appear on generated documents
/// (Phase 6 scope) — describes the template/issuer, not any
/// Handover, so it lives in configuration, never on <c>EquipmentHandover</c>
/// or any other domain entity. Deliberately has no default values anywhere in
/// code: a document issued under a fabricated or placeholder company identity
/// would itself be the misleading legal document this phase must avoid
/// (Phase 6 scope) — see <c>ResponsibilityTermService</c>, which
/// refuses to generate rather than embed a blank/fake issuer. <c>.env.example</c>
/// documents the shape with obviously-fake values for local development only.
/// </summary>
public sealed class DocumentIssuerOptions
{
    public const string SectionName = "Documents:Issuer";

    public string? LegalName { get; set; }

    public string? Address { get; set; }

    public string? TaxId { get; set; }

    /// <summary>True only when every field a generated document needs to identify its issuer is actually configured.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(LegalName) && !string.IsNullOrWhiteSpace(Address) && !string.IsNullOrWhiteSpace(TaxId);
}
