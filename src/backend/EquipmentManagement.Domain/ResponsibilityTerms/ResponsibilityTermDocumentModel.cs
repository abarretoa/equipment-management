namespace EquipmentManagement.Domain.ResponsibilityTerms;

/// <summary>
/// The complete, immutable input to PDF rendering (Phase 6 scope).
/// Built once, from the handover's frozen historical snapshots (captured at
/// finalization, or — for a carrier term prepared after dispatch — at
/// generation) — never from
/// live Consultant/Equipment/Company data (docs/PROJECT_SPEC.md, section 11:
/// "O PDF histórico não pode mudar se o consultor ou equipamento for editado
/// posteriormente"). Deliberately framework- and database-free so mapping to
/// it is unit-testable without PostgreSQL, and so the PDF renderer
/// (<c>Infrastructure.Documents.IResponsibilityTermPdfGenerator</c>) can stay
/// a pure function of this model — no database queries, no HTTP context, no
/// external calls (Phase 6 scope).
/// </summary>
/// <param name="DeliveryDate">
/// <c>EquipmentHandover.DeliveredOn</c> — the actual date of physical
/// delivery, so null for a carrier term prepared before receipt. Rendered only
/// by Template V1; Template V2 never prints it: its single consultant-facing
/// "Data de receção dos equipamentos" field is always left blank for the
/// consultant to complete when signing, so a prepared term stays truthful and
/// one template keeps one meaning.
/// </param>
public sealed record ResponsibilityTermDocumentModel(
    string DocumentNumber,
    int Version,
    int TemplateVersion,
    DateOnly? DeliveryDate,
    string DeliveredByDisplayName,
    DateTime GeneratedAtUtc,
    string GeneratedByDisplayName,
    ResponsibilityTermConsultantInfo Consultant,
    ResponsibilityTermProfessionalContext? ProfessionalContext,
    IReadOnlyList<ResponsibilityTermEquipmentItem> EquipmentItems);

/// <summary>Historical Consultant identity — from the version's own <c>ResponsibilityTerm.ConsultantSnapshot</c>, never a live Consultant lookup.</summary>
public sealed record ResponsibilityTermConsultantInfo(string FullName, string Nif, string Address, string Phone, string Email);

/// <summary>
/// Present only when the consultant had an active allocation at finalization
/// time (docs/PROJECT_SPEC.md, section 6) — from the version's own <c>ResponsibilityTerm.AllocationSnapshot</c>.
/// Absence is not an error; the template must not render a blank/meaningless
/// section for it (Phase 6 scope).
/// </summary>
public sealed record ResponsibilityTermProfessionalContext(string WorkforceType, string EngagementType, string RoleOrFunction, string? CompanyName);

/// <summary>
/// One delivered equipment item's historical identity — from
/// the version's own <c>ResponsibilityTermItem.EquipmentSnapshot</c>, never a live Equipment
/// lookup. <see cref="LaptopSpecification"/> is present only for laptops,
/// <see cref="MobilePhoneSpecification"/> only for mobile phones — the
/// template must not render empty device-specific fields for the wrong kind.
/// <see cref="Kind"/> is the real <c>EquipmentTypeKind</c> discriminator
/// (frozen in the snapshot), never inferred from
/// <see cref="EquipmentTypeName"/>. <see cref="PhotoSet"/> is present exactly
/// when <see cref="Kind"/> requires condition photos — it is the *exact*
/// photo set this version froze (<c>ResponsibilityTermItem.EquipmentPhotoSetId</c>),
/// never
/// whichever set happens to be current when the Term is (re)generated.
/// </summary>
public sealed record ResponsibilityTermEquipmentItem(
    string AssetTag, string EquipmentTypeName, string? Manufacturer, string? Model, string? SerialNumber, string Kind,
    ResponsibilityTermLaptopSpecification? LaptopSpecification, ResponsibilityTermMobilePhoneSpecification? MobilePhoneSpecification,
    string? Notes, ResponsibilityTermPhotoSet? PhotoSet);

public sealed record ResponsibilityTermLaptopSpecification(string Processor, string Memory, string Storage);

/// <summary>PIN/PUK are deliberately absent — never persisted, never rendered, because they are secrets.</summary>
public sealed record ResponsibilityTermMobilePhoneSpecification(string Imei, string? MobileNumber, string? SimSerialNumber);

/// <summary>The exact 3 photo bytes frozen for this Term at generation time — never re-read from a live "current" query afterward. Captured once, embedded into the immutable PDF, and never touched again.</summary>
public sealed record ResponsibilityTermPhotoSet(DateTime CapturedAtUtc, IReadOnlyList<byte[]> Photos);
