namespace EquipmentManagement.Domain.Allocations;

/// <summary>
/// The contractual relationship ("vínculo") for a specific allocation —
/// prestador de serviço / contrato de trabalho. Lives on
/// <see cref="ConsultantAllocation"/>, not on <c>Consultants.Consultant</c>: see
/// docs/PROJECT_SPEC.md, section 5 ("modelo contratual" is explicitly listed
/// among the things that must belong to the allocation to preserve history when
/// it changes) and ADR-018. Persisted as text, not the numeric
/// value, so the stored data survives member reordering.
/// </summary>
public enum EngagementType
{
    ServiceProvider,
    EmploymentContract,
}
