using System.ComponentModel.DataAnnotations;
using EquipmentManagement.Api.Contracts.Allocations;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Domain.Allocations;

namespace EquipmentManagement.Api.Contracts.Consultants;

public sealed record CreateConsultantRequest(
    [Required, MaxLength(300)] string FullName,
    [Required] string Nif,
    [Required, EmailAddress] string Email,
    [Required, MaxLength(500)] string Address,
    [Required, Phone] string Phone);

public sealed record UpdateConsultantRequest(
    [Required, MaxLength(300)] string FullName,
    [Required] string Nif,
    [Required, EmailAddress] string Email,
    [Required, MaxLength(500)] string Address,
    [Required, Phone] string Phone);

/// <summary>
/// Least-privilege list projection — no NIF/Address/Phone/Email
/// (Phase 2 scope).
/// </summary>
public sealed record ConsultantSummaryResponse(
    Guid Id,
    string FullName,
    bool IsActive,
    DateTime CreatedAtUtc,
    WorkforceType? CurrentWorkforceType,
    Guid? CurrentCompanyId,
    string? CurrentCompanyName,
    EngagementType? CurrentEngagementType,
    string? CurrentRoleOrFunction);

/// <summary>
/// The full profile plus current allocation, allocation history, and current
/// equipment custody — only reachable via the single-consultant GET, gated the
/// same as the list (Phase 2 scope).
/// <see cref="CurrentAssignedEquipment"/> is resolved from the Active-status
/// <see cref="EquipmentManagement.Domain.Handovers.EquipmentAssignment"/> rows
/// for this consultant — the single source of truth for current custody, never
/// derived from Equipment.Status or historical Handover/Return records.
/// </summary>
public sealed record ConsultantDetailResponse(
    Guid Id,
    string FullName,
    string Nif,
    string Email,
    string Address,
    string Phone,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    ConsultantAllocationResponse? CurrentAllocation,
    IReadOnlyList<ConsultantAllocationResponse> AllocationHistory,
    IReadOnlyList<EquipmentAssignmentResponse> CurrentAssignedEquipment);
