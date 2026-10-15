using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.Equipments;

/// <summary>
/// Assembles one Equipment's operational history (Iteration 1) as a read-only projection over existing domain rows — never a new
/// persisted timeline table, and never the Audit Log (why: the
/// Audit Log is a generic technical trail, not a curated business timeline,
/// and several of its rows would leak more than this feature should show).
/// Every timestamp comes from a real persisted column; nothing here is
/// inferred or fabricated. Consultant identity/company/role for an
/// <see cref="EquipmentAssignment"/> come from the originating
/// <see cref="EquipmentHandover"/>'s <c>ConsultantSnapshot</c>/
/// <c>AllocationSnapshot</c> — frozen at finalization — never a live
/// Consultant/ConsultantAllocation lookup, so a consultant's later company or
/// role change never rewrites old history.
/// </summary>
public sealed class EquipmentHistoryService(ApplicationDbContext dbContext)
{
    public async Task<IReadOnlyList<EquipmentHistoryEventRecord>?> GetHistoryAsync(Guid equipmentId, CancellationToken cancellationToken = default)
    {
        var equipment = await dbContext.Equipment.AsNoTracking()
            .Where(e => e.Id == equipmentId)
            .Select(e => new { e.CreatedAtUtc, e.UpdatedAtUtc, e.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (equipment is null)
        {
            return null;
        }

        var events = new List<EquipmentHistoryEventRecord>
        {
            new(
                EquipmentHistoryEventType.Registered, equipment.CreatedAtUtc,
                ConsultantId: null, ConsultantName: null, CompanyName: null, Role: null,
                HandoverId: null, ReturnId: null, MaintenanceId: null,
                Condition: null, Outcome: null, Description: null),
        };

        var assignments = await dbContext.EquipmentAssignments.AsNoTracking()
            .Where(a => a.EquipmentId == equipmentId)
            .OrderBy(a => a.StartedAtUtc)
            .ToListAsync(cancellationToken);

        var handoverIds = assignments.Where(a => a.EquipmentHandoverId is not null).Select(a => a.EquipmentHandoverId!.Value).Distinct().ToList();
        var handoverSnapshots = await dbContext.EquipmentHandovers.AsNoTracking()
            .Where(h => handoverIds.Contains(h.Id))
            .Select(h => new { h.Id, h.ConsultantSnapshot, h.AllocationSnapshot })
            .ToDictionaryAsync(h => h.Id, cancellationToken);

        // A Current Inventory import assignment has no Handover, hence no
        // frozen snapshot: only the consultant's name is shown, read from the
        // Consultant itself, and company/role stay empty rather than being
        // taken from a later, possibly different, allocation.
        var importedConsultantIds = assignments.Where(a => a.EquipmentHandoverId is null).Select(a => a.ConsultantId).Distinct().ToList();
        var importedConsultantNames = importedConsultantIds.Count == 0
            ? []
            : await dbContext.Consultants.AsNoTracking()
                .Where(c => importedConsultantIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.FullName, cancellationToken);

        foreach (var assignment in assignments)
        {
            string? consultantName;
            string? companyName = null;
            string? role = null;
            if (assignment.EquipmentHandoverId is { } handoverId)
            {
                handoverSnapshots.TryGetValue(handoverId, out var handover);
                consultantName = handover?.ConsultantSnapshot?.FullName;
                companyName = handover?.AllocationSnapshot?.CompanyName;
                role = handover?.AllocationSnapshot?.RoleOrFunction;
            }
            else
            {
                importedConsultantNames.TryGetValue(assignment.ConsultantId, out consultantName);
            }

            events.Add(new EquipmentHistoryEventRecord(
                EquipmentHistoryEventType.Assigned, assignment.StartedAtUtc,
                assignment.ConsultantId, consultantName, companyName, role,
                assignment.EquipmentHandoverId, ReturnId: null, MaintenanceId: null,
                Condition: null, Outcome: null, Description: null, AssignmentOrigin: assignment.Origin));

            if (assignment.EndedAtUtc is { } endedAtUtc)
            {
                events.Add(new EquipmentHistoryEventRecord(
                    EquipmentHistoryEventType.Returned, endedAtUtc,
                    assignment.ConsultantId, consultantName, companyName, role,
                    assignment.EquipmentHandoverId, ReturnId: null, MaintenanceId: null,
                    Condition: null, Outcome: null, Description: null, AssignmentOrigin: assignment.Origin));
            }
        }

        var returns = await dbContext.EquipmentReturns.AsNoTracking()
            .Where(r => r.EquipmentId == equipmentId)
            .Select(r => new { r.Id })
            .ToListAsync(cancellationToken);
        var returnIds = returns.Select(r => r.Id).ToList();

        var finalizedInspections = await dbContext.ReturnInspections.AsNoTracking()
            .Where(i => returnIds.Contains(i.EquipmentReturnId) && i.FinalizedAtUtc != null)
            .Select(i => new { i.EquipmentReturnId, i.Condition, FinalizedAtUtc = i.FinalizedAtUtc!.Value })
            .ToListAsync(cancellationToken);

        foreach (var inspection in finalizedInspections)
        {
            events.Add(new EquipmentHistoryEventRecord(
                EquipmentHistoryEventType.ReturnInspectionCompleted, inspection.FinalizedAtUtc,
                ConsultantId: null, ConsultantName: null, CompanyName: null, Role: null,
                HandoverId: null, ReturnId: inspection.EquipmentReturnId, MaintenanceId: null,
                inspection.Condition, Outcome: null, Description: null));
        }

        var damages = await dbContext.Damages.AsNoTracking()
            .Where(d => d.EquipmentId == equipmentId)
            .Select(d => new { d.EquipmentReturnId, d.ReportedAtUtc, d.Description })
            .ToListAsync(cancellationToken);

        foreach (var damage in damages)
        {
            events.Add(new EquipmentHistoryEventRecord(
                EquipmentHistoryEventType.DamageReported, damage.ReportedAtUtc,
                ConsultantId: null, ConsultantName: null, CompanyName: null, Role: null,
                HandoverId: null, ReturnId: damage.EquipmentReturnId, MaintenanceId: null,
                Condition: null, Outcome: null, damage.Description));
        }

        var maintenanceRecords = await dbContext.MaintenanceRecords.AsNoTracking()
            .Where(m => m.EquipmentId == equipmentId)
            .Select(m => new { m.Id, m.SentAtUtc, m.CompletedAtUtc, m.Outcome })
            .ToListAsync(cancellationToken);

        foreach (var maintenance in maintenanceRecords)
        {
            events.Add(new EquipmentHistoryEventRecord(
                EquipmentHistoryEventType.MaintenanceStarted, maintenance.SentAtUtc,
                ConsultantId: null, ConsultantName: null, CompanyName: null, Role: null,
                HandoverId: null, ReturnId: null, maintenance.Id,
                Condition: null, Outcome: null, Description: null));

            if (maintenance.CompletedAtUtc is { } completedAtUtc)
            {
                events.Add(new EquipmentHistoryEventRecord(
                    EquipmentHistoryEventType.MaintenanceCompleted, completedAtUtc,
                    ConsultantId: null, ConsultantName: null, CompanyName: null, Role: null,
                    HandoverId: null, ReturnId: null, maintenance.Id,
                    Condition: null, maintenance.Outcome, Description: null));
            }
        }

        var retirementRequests = await dbContext.EquipmentRetirementRequests.AsNoTracking()
            .Where(r => r.EquipmentId == equipmentId)
            .Select(r => new
            {
                r.Id,
                r.RequestedAtUtc,
                r.RequestedByDisplayNameSnapshot,
                r.RequestReason,
                r.Status,
                r.DecidedAtUtc,
                r.DecidedByDisplayNameSnapshot,
                r.DecisionReason,
            })
            .ToListAsync(cancellationToken);

        foreach (var request in retirementRequests)
        {
            events.Add(new EquipmentHistoryEventRecord(
                EquipmentHistoryEventType.RetirementRequested, request.RequestedAtUtc,
                ConsultantId: null, ConsultantName: null, CompanyName: null, Role: null,
                HandoverId: null, ReturnId: null, MaintenanceId: null,
                Condition: null, Outcome: null, Description: request.RequestReason,
                RetirementRequestId: request.Id, RequestedByName: request.RequestedByDisplayNameSnapshot));

            if (request.Status == EquipmentRetirementRequestStatus.Rejected && request.DecidedAtUtc is { } decidedAtUtc)
            {
                events.Add(new EquipmentHistoryEventRecord(
                    EquipmentHistoryEventType.RetirementRequestRejected, decidedAtUtc,
                    ConsultantId: null, ConsultantName: null, CompanyName: null, Role: null,
                    HandoverId: null, ReturnId: null, MaintenanceId: null,
                    Condition: null, Outcome: null, Description: request.DecisionReason,
                    RetirementRequestId: request.Id, RequestedByName: request.DecidedByDisplayNameSnapshot));
            }
        }

        // No dedicated RetiredAtUtc column exists (Iteration 1
        // introduces no schema change for this feature); Retire
        // is the only write path that can move Status to Retired, and it
        // always stamps UpdatedAtUtc in the same call, so this is the closest
        // truthful timestamp without inventing a new persisted field.
        if (equipment.Status == EquipmentStatus.Retired)
        {
            events.Add(new EquipmentHistoryEventRecord(
                EquipmentHistoryEventType.Retired, equipment.UpdatedAtUtc,
                ConsultantId: null, ConsultantName: null, CompanyName: null, Role: null,
                HandoverId: null, ReturnId: null, MaintenanceId: null,
                Condition: null, Outcome: null, Description: null));
        }

        // Newest-first; ties broken by a stable, deterministic key
        // (event type ordinal, then a synthetic per-event GUID stand-in would
        // be arbitrary — the underlying ids used as secondary sort inputs are
        // already unique per row) so repeated calls always return the same order.
        return events
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => (int)e.EventType)
            .ThenBy(e => e.HandoverId ?? e.ReturnId ?? e.MaintenanceId ?? Guid.Empty)
            .ToList();
    }
}
