using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EquipmentManagement.Infrastructure.Handovers;

/// <summary>
/// Loads the rows <see cref="HandoverRules.ResolveConsultantEligibility"/>
/// needs, and owns the pt-PT messages for an ineligible Consultant, so
/// handover creation, dispatch and finalization all enforce the same rule with
/// the same wording (Iteration 3). The Consultant list's
/// eligibility filter (<c>ConsultantService.ListAsync</c>,
/// <c>HasActiveAllocation</c>) is the same rule expressed as a translatable
/// predicate for the frontend picker; the backend check here stays
/// authoritative regardless of what the picker shows.
/// </summary>
internal static partial class HandoverConsultantEligibility
{
    public const string CreateMessage =
        "Não é possível criar uma entrega para este consultor. Registe primeiro uma alocação válida.";

    public const string OperationMessage =
        "Não é possível concluir esta operação porque o consultor não tem uma alocação válida.";

    /// <summary>Deliberately says only what is wrong and how to fix it — no allocation ids or other data.</summary>
    public const string InconsistentMessage =
        "Não é possível concluir esta operação porque o consultor tem mais do que uma alocação ativa. " +
        "Corrija as alocações do consultor antes de continuar.";

    /// <summary>
    /// Counts at most two Active rows: enough to tell 0 / exactly 1 / more than
    /// 1 apart without loading anything else.
    /// </summary>
    public static async Task<ConsultantHandoverEligibility> ResolveAsync(
        ApplicationDbContext dbContext, ILogger logger, Guid consultantId, CancellationToken cancellationToken)
    {
        var activeCount = await dbContext.ConsultantAllocations.AsNoTracking()
            .Where(a => a.ConsultantId == consultantId && a.Status == AllocationStatus.Active)
            .Take(2)
            .CountAsync(cancellationToken);

        return Resolve(logger, consultantId, activeCount);
    }

    /// <summary>Resolves an already-counted number of Active allocations, logging the inconsistent case so it can be investigated.</summary>
    public static ConsultantHandoverEligibility Resolve(ILogger logger, Guid consultantId, int activeAllocationCount)
    {
        var eligibility = HandoverRules.ResolveConsultantEligibility(activeAllocationCount);
        if (eligibility == ConsultantHandoverEligibility.MultipleActiveAllocations)
        {
            LogMultipleActiveAllocations(logger, consultantId);
        }

        return eligibility;
    }

    /// <summary>The failure for a non-<see cref="ConsultantHandoverEligibility.Eligible"/> outcome; <paramref name="noAllocationMessage"/> lets creation use its more contextual wording.</summary>
    public static (ServiceErrorCode Code, string Message) ToFailure(ConsultantHandoverEligibility eligibility, string noAllocationMessage = OperationMessage) =>
        eligibility == ConsultantHandoverEligibility.MultipleActiveAllocations
            ? (ServiceErrorCode.InconsistentAllocationState, InconsistentMessage)
            : (ServiceErrorCode.ConsultantNotEligible, noAllocationMessage);

    /// <summary>Same rule as <see cref="HandoverRules.ResolveConsultantEligibility"/>'s "has an Active allocation", for use inside a larger EF query.</summary>
    public static IQueryable<Guid> EligibleConsultantIds(ApplicationDbContext dbContext) =>
        dbContext.ConsultantAllocations.AsNoTracking()
            .Where(a => a.Status == AllocationStatus.Active)
            .Select(a => a.ConsultantId);

    /// <summary>Consultant id only — never names or other PII in logs.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Consultant {ConsultantId} has more than one Active allocation; handover operation refused instead of choosing one.")]
    private static partial void LogMultipleActiveAllocations(ILogger logger, Guid consultantId);
}
