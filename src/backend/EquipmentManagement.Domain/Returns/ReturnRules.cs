using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.Domain.Returns;

/// <summary>
/// Pure business rules for the return/return-inspection workflow
/// (docs/PROJECT_SPEC.md, section 12; Phase 5 scope). Framework- and
/// database-free so they're unit-testable in isolation — <c>Infrastructure.Returns.EquipmentReturnService</c>
/// supplies the actual rows to check these against.
/// </summary>
public static class ReturnRules
{
    /// <summary>Only <c>Assigned</c> equipment backed by an <c>Active</c> assignment can have a return initiated.</summary>
    public static bool CanInitiate(EquipmentStatus equipmentStatus) => equipmentStatus == EquipmentStatus.Assigned;

    /// <summary>
    /// What ending a Consultant's engagement does with one piece of equipment
    /// still in their custody (Iteration 4).
    /// <paramref name="openReturnStatus"/> is the status of the assignment's
    /// one non-Cancelled return, or null when it has none. Any combination
    /// other than the two valid ones is inconsistent data and is never
    /// repaired or skipped here: the caller must reject the whole operation.
    /// </summary>
    public static EndOfEngagementReturnDisposition ClassifyForEndOfEngagement(EquipmentStatus equipmentStatus, ReturnStatus? openReturnStatus)
    {
        if (openReturnStatus is null)
        {
            return CanInitiate(equipmentStatus) ? EndOfEngagementReturnDisposition.Initiate : EndOfEngagementReturnDisposition.RequiresAttention;
        }

        return equipmentStatus == EquipmentStatus.ReturnPending && openReturnStatus == ReturnStatus.Pending
            ? EndOfEngagementReturnDisposition.AlreadyPending
            : EndOfEngagementReturnDisposition.RequiresAttention;
    }

    /// <summary>
    /// The collection status of a return group, from the statuses of its
    /// returns. "Received" means physically received — a return that is
    /// <see cref="ReturnStatus.Received"/> or <see cref="ReturnStatus.Completed"/>
    /// — and never depends on what happened to the equipment afterwards
    /// (under inspection, damaged, in maintenance or available again).
    /// Cancelled returns are no longer expected, so they are ignored unless
    /// nothing else is left.
    /// </summary>
    public static ReturnGroupCollectionStatus DeriveGroupCollectionStatus(IEnumerable<ReturnStatus> returnStatuses)
    {
        var expected = 0;
        var received = 0;
        foreach (var status in returnStatuses)
        {
            if (status == ReturnStatus.Cancelled)
            {
                continue;
            }

            expected++;
            if (status is ReturnStatus.Received or ReturnStatus.Completed)
            {
                received++;
            }
        }

        if (expected == 0)
        {
            return ReturnGroupCollectionStatus.Cancelled;
        }

        if (received == 0)
        {
            return ReturnGroupCollectionStatus.Awaiting;
        }

        return received == expected ? ReturnGroupCollectionStatus.Collected : ReturnGroupCollectionStatus.Partial;
    }

    /// <summary>
    /// The one status a row of the operational Returns list is filtered
    /// under, so that a group can never appear under two filters at once.
    /// Counts are of the row's returns (all of a group's, or the single
    /// ungrouped return); cancelled ones are passed only through
    /// <paramref name="notCancelledCount"/> not including them.
    /// <list type="bullet">
    /// <item>something is still awaiting physical receipt (collection status
    /// Awaiting or Partial) → <see cref="ReturnStatus.Pending"/>;</item>
    /// <item>everything expected was received but at least one inspection is
    /// still open → <see cref="ReturnStatus.Received"/>;</item>
    /// <item>everything expected was received and inspected →
    /// <see cref="ReturnStatus.Completed"/>;</item>
    /// <item>nothing is expected any more → <see cref="ReturnStatus.Cancelled"/>.</item>
    /// </list>
    /// For a single return this is simply its own status.
    /// </summary>
    public static ReturnStatus DeriveListFilterStatus(int notCancelledCount, int pendingCount, int receivedCount)
    {
        if (notCancelledCount == 0)
        {
            return ReturnStatus.Cancelled;
        }

        if (pendingCount > 0)
        {
            return ReturnStatus.Pending;
        }

        return receivedCount > 0 ? ReturnStatus.Received : ReturnStatus.Completed;
    }

    /// <summary>Physical receipt can only be recorded once, while the return is still <see cref="ReturnStatus.Pending"/>.</summary>
    public static bool CanReceive(ReturnStatus status) => status == ReturnStatus.Pending;

    /// <summary>
    /// A return can only be cancelled before physical receipt — once
    /// <see cref="ReturnStatus.Received"/> or <see cref="ReturnStatus.Completed"/>,
    /// cancellation must be impossible (Phase 5 cancellation amendment).
    /// </summary>
    public static bool CanCancel(ReturnStatus status) => status == ReturnStatus.Pending;

    /// <summary>The return inspection can only be recorded/updated once the equipment has been physically received.</summary>
    public static bool CanRecordInspection(ReturnStatus status) => status == ReturnStatus.Received;

    /// <summary>Only a <see cref="ReturnStatus.Received"/> return (not yet finalized) can be finalized.</summary>
    public static bool CanFinalize(ReturnStatus status) => status == ReturnStatus.Received;

    /// <summary>The receipt date must be known and cannot be in the future.</summary>
    public static bool IsValidReceivedOn(DateOnly receivedOn, DateOnly today) => receivedOn <= today;

    /// <summary>
    /// Laptop/MobilePhone equipment requires exactly 3 new condition photos at
    /// the moment of physical receipt (<c>EquipmentReturnService.ReceiveAsync</c>)
    /// — the official condition-photo refresh. The former inspection-time
    /// photo minimum (docs/PROJECT_SPEC.md, section 12; invariant #8) was
    /// removed: requiring a second, duplicate 3-photo set during inspection
    /// would ask HR to photograph the same equipment twice for one Return.
    /// <see cref="Returns.ReturnInspectionPhoto"/> remains available as
    /// optional additional evidence, never mandatory.
    /// </summary>
    public const int RequiredReceivePhotoCount = 3;

    /// <summary>Damage evidence requires both a non-blank description and at least one photo (docs/PROJECT_SPEC.md, invariant #9).</summary>
    public static bool HasRequiredDamageEvidence(string? damageDescription, int damagePhotoCount) =>
        !string.IsNullOrWhiteSpace(damageDescription) && damagePhotoCount >= 1;
}
