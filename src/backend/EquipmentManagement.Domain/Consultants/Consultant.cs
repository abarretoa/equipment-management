namespace EquipmentManagement.Domain.Consultants;

/// <summary>
/// A consultant tracked by the business — never an ASP.NET Core Identity user
/// (docs/PROJECT_SPEC.md, section 4; see ADR-017). Deliberately excludes
/// workforce type, role/function, company and engagement type: those describe a
/// point-in-time professional relationship and belong to
/// <c>Allocations.ConsultantAllocation</c>, not to the consultant's permanent
/// profile (docs/PROJECT_SPEC.md, section 5 — see ADR-018 on why <c>EngagementType</c> in particular lives on the allocation).
/// </summary>
public sealed class Consultant
{
    public Guid Id { get; set; }

    /// <summary>
    /// The full name as entered — never split into first/last. Portuguese and
    /// international names don't reliably decompose that way
    /// (Phase 2 scope).
    /// </summary>
    public required string FullName { get; set; }

    /// <summary>
    /// Normalized (digits only) tax identifier. See
    /// <see cref="ConsultantRules.NormalizeNif"/> for why this is
    /// structural-only validation, not a Portuguese-NIF checksum.
    /// </summary>
    public required string Nif { get; set; }

    public required string Email { get; set; }

    public required string Address { get; set; }

    public required string Phone { get; set; }

    /// <summary>
    /// Archived consultants keep every historical record intact; only new
    /// allocations are blocked (Phase 2 scope).
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
