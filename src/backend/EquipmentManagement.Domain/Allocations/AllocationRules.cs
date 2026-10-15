namespace EquipmentManagement.Domain.Allocations;

/// <summary>
/// Pure business rules for <see cref="ConsultantAllocation"/>
/// (docs/PROJECT_SPEC.md, sections 6, 21; Phase 2 scope).
/// Framework- and database-free so they're unit-testable in isolation — the
/// Infrastructure service layer supplies the actual rows to check these
/// against.
/// </summary>
public static class AllocationRules
{
    public static bool IsValidRoleOrFunction(string? roleOrFunction) => !string.IsNullOrWhiteSpace(roleOrFunction);

    /// <summary>
    /// External allocations require a company; internal ones must not carry one
    /// (docs/PROJECT_SPEC.md, section 6: "consultor externo exige empresa;
    /// consultor interno não exige empresa externa"). Deliberately "must not
    /// carry one" rather than merely "may omit" for Internal — accepting a
    /// stray CompanyId on an Internal allocation would silently create
    /// meaningless data no read path expects.
    /// </summary>
    public static bool IsCompanyRequirementSatisfied(WorkforceType workforceType, Guid? companyId) =>
        workforceType switch
        {
            WorkforceType.External => companyId is not null,
            WorkforceType.Internal => companyId is null,
            _ => false,
        };

    /// <summary>EndDate, when present, cannot precede StartDate (Phase 2 scope).</summary>
    public static bool IsValidDateRange(DateOnly startDate, DateOnly? endDate) => endDate is null || endDate >= startDate;

    /// <summary>
    /// A Consultant has at most ONE <see cref="AllocationStatus.Active"/>
    /// allocation at a time (docs/PROJECT_SPEC.md, section 6; confirmed
    /// before merging Iteration 3). Decided by lifecycle
    /// status only, never by dates: an Active allocation with a future
    /// StartDate is still the Consultant's one Active allocation, and a new
    /// one can only be registered after it is explicitly ended. Replaces the
    /// earlier date-overlap rule, which allowed a second, future-dated Active
    /// allocation once the current one had a known end date. Backed at the
    /// database level by a partial unique index on ConsultantId WHERE
    /// Status = 'Active'.
    /// </summary>
    public static bool CanCreateActiveAllocation(int existingActiveAllocationCount) => existingActiveAllocationCount == 0;
}
