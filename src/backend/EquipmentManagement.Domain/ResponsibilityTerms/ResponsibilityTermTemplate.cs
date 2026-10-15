namespace EquipmentManagement.Domain.ResponsibilityTerms;

/// <summary>
/// The versioned legal-wording/layout identifier (docs/PROJECT_SPEC.md,
/// section 11: "a versão gerada deve guardar... versão do template"; Phase 6 scope). Every generated <see cref="ResponsibilityTerm"/>
/// permanently records which version produced it — a future wording change
/// becomes <see cref="CurrentVersion"/> = 2 in this file, never a silent
/// rewrite of what version 1 means; the code path that renders version 1 must
/// remain available for as long as any <c>ResponsibilityTerm</c> with
/// <c>TemplateVersion == 1</c> might need to be re-rendered or re-verified.
/// This is deliberately a plain constant, not a database-editable template —
/// Phase 6 scope: "Do not build a full CMS/template editor."
/// </summary>
public static class ResponsibilityTermTemplate
{
    /// <summary>
    /// The template version every new generation uses. Bump only by adding a
    /// new rendering branch in <c>IResponsibilityTermPdfGenerator</c> — never
    /// by changing what an existing version number means. Version 2 adds the
    /// client identity (logo/accent-coloured headings, from the client profile), the
    /// device-specific Laptop/Mobile Phone document structures, and the
    /// current-photo-set section — version 1 remains available, unchanged,
    /// for any historical term generated under it.
    /// </summary>
    public const int CurrentVersion = 2;
}
