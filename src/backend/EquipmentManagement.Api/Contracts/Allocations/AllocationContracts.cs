using System.ComponentModel.DataAnnotations;
using EquipmentManagement.Domain.Allocations;

namespace EquipmentManagement.Api.Contracts.Allocations;

public sealed record CreateConsultantAllocationRequest(
    [Required] WorkforceType WorkforceType,
    [Required] EngagementType EngagementType,
    [Required, MaxLength(200)] string RoleOrFunction,
    Guid? CompanyId,
    [Required] DateOnly StartDate,
    DateOnly? EndDate);

/// <summary>
/// The only correction this contract allows is RoleOrFunction, and only while
/// the allocation is still Active — see
/// <c>Infrastructure.Allocations.UpdateAllocationData</c>'s remarks for why
/// Company/WorkforceType/dates are deliberately absent here.
/// </summary>
public sealed record UpdateConsultantAllocationRequest([Required, MaxLength(200)] string RoleOrFunction);

/// <summary>
/// <see cref="IsEndOfEngagement"/> is a required, explicit tri-state choice —
/// ending an allocation is not always offboarding (see
/// <c>Infrastructure.Allocations.EndAllocationData</c>'s remarks), and the
/// caller must say "No" or "Yes" on purpose. It is deliberately <c>bool?</c>
/// rather than <c>bool</c> so that omitting the field (an older/incomplete
/// caller) is distinguishable from an explicit <c>false</c> and fails
/// validation instead of silently being treated as "No". <see
/// cref="ExpectedCollectionFromDate"/> is required only when
/// <see cref="IsEndOfEngagement"/> is true and the Consultant currently holds
/// active equipment; ignored otherwise. An end of engagement also initiates
/// the return of every piece of equipment in the Consultant's active custody
/// that has no return pending yet.
/// </summary>
public sealed record EndConsultantAllocationRequest(
    [Required] DateOnly EndDate, [Required] bool? IsEndOfEngagement, DateOnly? ExpectedCollectionFromDate, DateOnly? ExpectedCollectionToDate);

/// <summary>
/// The ended allocation (same fields as <see cref="ConsultantAllocationResponse"/>)
/// plus the compact result of the end-of-engagement return initiation:
/// <see cref="ReturnsCreatedCount"/> returns were initiated by this call, and
/// <see cref="ReturnsAlreadyPendingCount"/> were already pending and left
/// untouched. Both are zero when the ending is not an end of engagement or the
/// Consultant holds no equipment. <see cref="ReturnGroupId"/> is the group the
/// initiated returns belong to (null when none was initiated), so the client
/// can open that collection directly.
/// </summary>
public sealed record EndConsultantAllocationResponse(
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
    DateTime UpdatedAtUtc,
    int ReturnsCreatedCount,
    int ReturnsAlreadyPendingCount,
    Guid? ReturnGroupId = null);

public sealed record ConsultantAllocationResponse(
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
