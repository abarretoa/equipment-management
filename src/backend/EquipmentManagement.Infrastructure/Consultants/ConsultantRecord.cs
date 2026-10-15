using EquipmentManagement.Domain.Allocations;

namespace EquipmentManagement.Infrastructure.Consultants;

/// <summary>
/// The safe, service-layer projection of a consultant profile — never the EF
/// entity itself. Deliberately excludes PII when used as a list/summary
/// projection (see <see cref="ConsultantSummaryRecord"/>); this full record is
/// only for the single-consultant detail read.
/// </summary>
public sealed record ConsultantRecord(
    Guid Id,
    string FullName,
    string Nif,
    string Email,
    string Address,
    string Phone,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>
/// Least-privilege list projection: no NIF/Address/Phone/Email
/// (Phase 2 scope). Includes the
/// consultant's current allocation summary since list filtering by workforce
/// type/company operates on it.
/// </summary>
public sealed record ConsultantSummaryRecord(
    Guid Id,
    string FullName,
    bool IsActive,
    DateTime CreatedAtUtc,
    WorkforceType? CurrentWorkforceType,
    Guid? CurrentCompanyId,
    string? CurrentCompanyName,
    EngagementType? CurrentEngagementType,
    string? CurrentRoleOrFunction);

public sealed record CreateConsultantData(string FullName, string Nif, string Email, string Address, string Phone);

public sealed record UpdateConsultantData(string FullName, string Nif, string Email, string Address, string Phone);

/// <param name="HasActiveAllocation">
/// When true, only Consultants eligible to receive a new handover (at least
/// one Active allocation, see <c>HandoverRules.IsConsultantEligible</c>).
/// Used by the Handover creation picker only (Iteration 3). Null
/// leaves the list unfiltered, so no other screen changes.
/// </param>
/// <param name="HasActiveCustody">
/// When true, only Consultants who currently hold equipment (at least one
/// Active <c>EquipmentAssignment</c>) — the people a return can be started
/// for, whatever the state of their allocation (Iteration 4).
/// Null leaves the list unfiltered.
/// </param>
public sealed record ConsultantListFilter(
    string? Search,
    bool? IsActive,
    string? Nif,
    WorkforceType? WorkforceType,
    Guid? CompanyId,
    bool? HasActiveAllocation = null,
    bool? HasActiveCustody = null);
