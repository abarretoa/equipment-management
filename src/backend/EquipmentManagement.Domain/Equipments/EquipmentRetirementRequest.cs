namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// HR's request that Administration retire a specific, currently-eligible
/// piece of equipment. HR can never execute the actual retirement — only
/// Administration can, via the existing <c>EquipmentService.RetireAsync</c>
/// (see <c>EquipmentRetirementRequestService.ApproveAsync</c>). At most one
/// <see cref="EquipmentRetirementRequestStatus.Pending"/> row may exist per
/// <see cref="EquipmentId"/> at a time (see the partial unique index) — the
/// database-enforced backstop, mirroring the <c>MaintenanceRecord</c>/
/// <c>Handovers.EquipmentAssignment</c> pattern. This entity exists
/// specifically for this workflow — not a generic approval/workflow engine.
/// </summary>
public sealed class EquipmentRetirementRequest
{
    public Guid Id { get; set; }

    public Guid EquipmentId { get; set; }

    public Guid RequestedByUserId { get; set; }

    /// <summary>Frozen at request time, same discipline as <c>MaintenanceRecord.CreatedByDisplayNameSnapshot</c> — a later name change never rewrites this request's display.</summary>
    public required string RequestedByDisplayNameSnapshot { get; set; }

    public DateTime RequestedAtUtc { get; set; }

    public required string RequestReason { get; set; }

    public EquipmentRetirementRequestStatus Status { get; set; }

    public Guid? DecidedByUserId { get; set; }

    public string? DecidedByDisplayNameSnapshot { get; set; }

    public DateTime? DecidedAtUtc { get; set; }

    /// <summary>Required for a rejection; never set for an approval (the existing retirement operation carries its own metadata — see docs on <c>EquipmentService.RetireAsync</c>).</summary>
    public string? DecisionReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
