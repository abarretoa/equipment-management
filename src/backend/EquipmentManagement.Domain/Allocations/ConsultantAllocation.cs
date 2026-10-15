namespace EquipmentManagement.Domain.Allocations;

/// <summary>
/// A historical professional engagement for one consultant during one period
/// (docs/PROJECT_SPEC.md, section 6). Never overwritten to represent a new
/// engagement — see ADR-021.
/// <c>Consultants.Consultant</c> and <c>Companies.Company</c> are referenced by
/// id only; this type has no navigation properties back to them by design (EF
/// Core relationships are configured in <c>ApplicationDbContext</c>, and
/// application code loads what it needs via explicit projections — see docs/PROJECT_SPEC.md, section 24).
/// </summary>
public sealed class ConsultantAllocation
{
    public Guid Id { get; set; }

    public Guid ConsultantId { get; set; }

    /// <summary>Required for <see cref="Allocations.WorkforceType.External"/>, null for <see cref="Allocations.WorkforceType.Internal"/>.</summary>
    public Guid? CompanyId { get; set; }

    public WorkforceType WorkforceType { get; set; }

    public EngagementType EngagementType { get; set; }

    public required string RoleOrFunction { get; set; }

    public DateOnly StartDate { get; set; }

    /// <summary>
    /// Null while ongoing/open-ended. A non-null value does not by itself mean
    /// the allocation is over — see <see cref="AllocationStatus"/>.
    /// </summary>
    public DateOnly? EndDate { get; set; }

    public AllocationStatus Status { get; set; }

    /// <summary>
    /// True only when HR explicitly flagged this ending as the end of the
    /// Consultant's engagement with the company (offboarding), as opposed to an
    /// ordinary allocation/role/client change with the Consultant remaining
    /// engaged. Never inferred from <see cref="EndDate"/> alone — every
    /// allocation end has an EndDate, but not every one is offboarding. Only
    /// meaningful once <see cref="Status"/> is <see cref="AllocationStatus.Ended"/>;
    /// false for an allocation still Active. Gates whether ending this
    /// allocation may trigger the equipment collection notification.
    /// </summary>
    public bool IsEndOfEngagement { get; set; }

    /// <summary>Set only when this ending was flagged <see cref="IsEndOfEngagement"/> and the Consultant had active equipment custody at the time — the collection window communicated to them.</summary>
    public DateOnly? ExpectedCollectionFromDate { get; set; }

    /// <summary>Optional upper bound of the collection window — null means a single expected date (<see cref="ExpectedCollectionFromDate"/> only).</summary>
    public DateOnly? ExpectedCollectionToDate { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
