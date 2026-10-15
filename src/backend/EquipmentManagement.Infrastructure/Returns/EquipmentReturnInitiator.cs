using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.Returns;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Persistence;

namespace EquipmentManagement.Infrastructure.Returns;

/// <summary>
/// The one place an <see cref="EquipmentReturn"/> is created and its equipment
/// moved to <see cref="EquipmentStatus.ReturnPending"/>, and the one place an
/// <see cref="EquipmentReturnGroup"/> is created. Shared by every way a return
/// starts — from an equipment's page, from the Returns area for one or more
/// items (<see cref="EquipmentReturnService"/>), and by ending a Consultant's
/// engagement (<c>ConsultantAllocationService.EndAsync</c>) — so all of them
/// apply the same invariants. It never saves, never opens a transaction and
/// never sends anything: the caller owns persistence, which is what lets
/// several returns be created atomically.
/// </summary>
public sealed class EquipmentReturnInitiator(ApplicationDbContext dbContext, AuditService auditService, TimeProvider timeProvider)
{
    /// <summary>Partial unique index on EquipmentAssignmentId WHERE Status &lt;&gt; 'Cancelled' (see ApplicationDbContext) — the DB-level backstop against two open returns for one assignment.</summary>
    public const string OneOpenReturnPerAssignmentIndexName = "IX_EquipmentReturns_EquipmentAssignmentId";

    /// <summary>Audit value for a return started by HR (from an equipment's page or the Returns area).</summary>
    public const string ManualTrigger = "Manual";

    /// <summary>Audit value for a return started by ending the Consultant's engagement.</summary>
    public const string EndOfEngagementTrigger = "EndOfEngagement";

    /// <summary>
    /// Adds the group for one initiation event to the context; nothing is
    /// saved. <paramref name="consultantAllocationId"/> is the allocation
    /// whose end-of-engagement ending is creating it, or null for a manual
    /// return. Only call this once at least one return will be staged into it:
    /// a group with no returns represents nothing.
    /// </summary>
    public EquipmentReturnGroup StageGroup(Guid consultantId, Guid? consultantAllocationId, Guid actorUserId, string actorDisplayName)
    {
        var group = new EquipmentReturnGroup
        {
            Id = Guid.NewGuid(),
            ConsultantId = consultantId,
            ConsultantAllocationId = consultantAllocationId,
            CreatedByUserId = actorUserId,
            CreatedByDisplayNameSnapshot = actorDisplayName,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        };

        dbContext.EquipmentReturnGroups.Add(group);
        return group;
    }

    /// <summary>
    /// Only an <c>Assigned</c> equipment backed by an <c>Active</c> assignment
    /// with no other non-Cancelled return can have a return initiated. On
    /// success the new return is added to the context as a member of
    /// <paramref name="group"/> and <paramref name="equipment"/> (which must
    /// be tracked) is moved to <c>ReturnPending</c>; nothing is saved. The
    /// assignment is never touched: custody ends only at physical receipt.
    /// </summary>
    public ServiceResult<EquipmentReturn> Stage(
        EquipmentReturnGroup group, EquipmentAssignment assignment, Equipment equipment, bool hasOpenReturn, InitiateReturnData data,
        Guid actorUserId, string actorDisplayName)
    {
        if (assignment.Status != AssignmentStatus.Active)
        {
            return ServiceResult.Fail<EquipmentReturn>(ServiceErrorCode.InvalidLifecycleTransition, "This assignment has already ended.");
        }

        if (!ReturnRules.CanInitiate(equipment.Status))
        {
            return ServiceResult.Fail<EquipmentReturn>(
                ServiceErrorCode.InvalidLifecycleTransition, $"Equipment in status '{equipment.Status}' cannot have a return initiated.");
        }

        // A Cancelled return releases the assignment for a new attempt; any
        // other status (Pending/Received/Completed) permanently occupies its
        // one-return lifetime — matches the partial unique index exactly
        // (see ApplicationDbContext's remarks and ADR-044).
        if (hasOpenReturn)
        {
            return ServiceResult.Fail<EquipmentReturn>(
                ServiceErrorCode.ReturnAlreadyInProgress, "A return is already in progress for this assignment.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var equipmentReturn = new EquipmentReturn
        {
            Id = Guid.NewGuid(),
            EquipmentAssignmentId = assignment.Id,
            EquipmentId = assignment.EquipmentId,
            ConsultantId = assignment.ConsultantId,
            ReturnGroupId = group.Id,
            Status = ReturnStatus.Pending,
            Reason = string.IsNullOrWhiteSpace(data.Reason) ? null : data.Reason.Trim(),
            ExpectedReturnDate = data.ExpectedReturnDate,
            CreatedByUserId = actorUserId,
            CreatedByDisplayNameSnapshot = actorDisplayName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        equipment.Status = EquipmentStatus.ReturnPending;
        equipment.UpdatedAtUtc = now;

        dbContext.EquipmentReturns.Add(equipmentReturn);

        return ServiceResult.Ok(equipmentReturn);
    }

    /// <summary>
    /// The audit trail of one initiation event: one entry for the group
    /// naming the returns it created, then each return's normal
    /// <see cref="AuditActions.EquipmentReturn.Created"/> entry, tagged with
    /// the group and with what triggered it. Identifiers and asset tags only —
    /// never the free-text reason or any Consultant PII.
    /// </summary>
    public async Task RecordCreatedAsync(
        EquipmentReturnGroup group, IReadOnlyList<(EquipmentReturn Return, Equipment Equipment)> created, string trigger,
        Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        await auditService.RecordAsync(new AuditEntry(
            AuditActions.EquipmentReturnGroup.Created,
            EntityType: "EquipmentReturnGroup",
            EntityId: group.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new
            {
                group.ConsultantId,
                group.ConsultantAllocationId,
                ReturnIds = created.Select(x => x.Return.Id).ToList(),
            },
            Metadata: new { Trigger = trigger }), cancellationToken);

        foreach (var (equipmentReturn, equipment) in created)
        {
            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentReturn.Created,
                EntityType: "EquipmentReturn",
                EntityId: equipmentReturn.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { equipment.AssetTag, EquipmentAssignmentId = equipmentReturn.EquipmentAssignmentId },
                Metadata: new { Trigger = trigger, ReturnGroupId = group.Id, group.ConsultantAllocationId }), cancellationToken);
        }
    }
}
