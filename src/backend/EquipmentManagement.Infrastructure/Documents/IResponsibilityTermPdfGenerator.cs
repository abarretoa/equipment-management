using EquipmentManagement.Domain.ResponsibilityTerms;

namespace EquipmentManagement.Infrastructure.Documents;

/// <summary>
/// Renders a <see cref="ResponsibilityTermDocumentModel"/> into PDF bytes
/// (Phase 6 scope). Deliberately the only thing this
/// interface does — no database access, no HTTP/authenticated-user state, no
/// external calls, no side effects: given the same model and issuer, it must
/// always produce the same bytes. <c>ResponsibilityTermService</c> is
/// responsible for building the model from historical snapshots; this
/// generator never queries anything itself.
/// </summary>
public interface IResponsibilityTermPdfGenerator
{
    /// <summary>
    /// Throws <see cref="NotSupportedException"/> if <see cref="ResponsibilityTermDocumentModel.TemplateVersion"/>
    /// names a version this generator has no rendering branch for — legal
    /// wording changes add a new branch (see ADR-048);
    /// they never change what an existing version number renders.
    /// </summary>
    byte[] Generate(ResponsibilityTermDocumentModel model, DocumentIssuerOptions issuer);
}
