using EquipmentManagement.Domain.Allocations;

namespace EquipmentManagement.Infrastructure.Allocations;

public sealed record ConsultantAllocationRecord(
    Guid Id,
    Guid ConsultantId,
    Guid? CompanyId,
    string? CompanyName,
    WorkforceType WorkforceType,
    EngagementType EngagementType,
    string RoleOrFunction,
    DateOnly StartDate,
    DateOnly? EndDate,
    AllocationStatus Status,
    bool IsEndOfEngagement,
    DateOnly? ExpectedCollectionFromDate,
    DateOnly? ExpectedCollectionToDate,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateAllocationData(
    WorkforceType WorkforceType,
    EngagementType EngagementType,
    string RoleOrFunction,
    Guid? CompanyId,
    DateOnly StartDate,
    DateOnly? EndDate);

/// <summary>
/// The only field a "correction" may touch on an existing allocation, and only
/// while it is still <see cref="AllocationStatus.Active"/>
/// (Phase 2 scope) — a real
/// change of company, workforce type, or dates always means ending this
/// allocation and creating a new one.
/// </summary>
public sealed record UpdateAllocationData(string RoleOrFunction);

/// <summary>
/// <see cref="IsEndOfEngagement"/> is a required, explicit tri-state choice on
/// every call — ending an allocation is not always offboarding (a Consultant
/// can end one allocation while starting another), so the collection workflow
/// must never fire just because an <see cref="EndDate"/> was supplied. It is
/// <c>bool?</c>, not <c>bool</c>, precisely so that <c>null</c> (never chosen)
/// is rejected by <see cref="ConsultantAllocationService.EndAsync"/> instead of
/// silently behaving like an explicit "No" — see that method's validation.
/// <see cref="ExpectedCollectionFromDate"/> is required only when
/// <see cref="IsEndOfEngagement"/> is true and the Consultant currently holds
/// active equipment; ignored otherwise.
/// <see cref="ActorMayInitiateReturns"/> is whether the caller holds
/// <c>Equipment.Return</c>: an end of engagement initiates returns, and must
/// not let a caller do through it what the single-equipment return endpoint
/// would refuse them.
/// </summary>
public sealed record EndAllocationData(
    DateOnly EndDate, bool? IsEndOfEngagement, bool ActorMayInitiateReturns,
    DateOnly? ExpectedCollectionFromDate = null, DateOnly? ExpectedCollectionToDate = null);

/// <summary>
/// The ended allocation plus what the end-of-engagement operation did with the
/// Consultant's equipment: how many returns it initiated, and how many were
/// already pending and left untouched. Both are zero when the ending was not
/// an end of engagement, or the Consultant held no equipment.
/// <paramref name="ReturnGroupId"/> is the group the initiated returns belong
/// to, or null when none was initiated.
/// </summary>
public sealed record EndAllocationResult(
    ConsultantAllocationRecord Allocation, int ReturnsCreatedCount, int ReturnsAlreadyPendingCount, Guid? ReturnGroupId);
