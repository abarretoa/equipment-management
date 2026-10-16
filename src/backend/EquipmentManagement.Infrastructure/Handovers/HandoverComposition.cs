using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.Handovers;

/// <summary>
/// Shared guard for the operations that read a Handover's items before their
/// Serializable transaction ("Registar envio", "Confirmar entrega"): re-read
/// inside the transaction, the item set must still be exactly the one the
/// operation validated. An add or removal that committed in between is then
/// refused as a conflict instead of shipping or assigning a stale composition.
/// </summary>
internal static class HandoverComposition
{
    public const string ConflictMessage = "A composição desta entrega foi alterada por outra operação. Atualize a página e tente novamente.";

    /// <summary>Must be called inside the operation's Serializable transaction.</summary>
    public static async Task<bool> HasChangedAsync(
        ApplicationDbContext dbContext, Guid handoverId, IEnumerable<Guid> validatedItemIds, CancellationToken cancellationToken)
    {
        var current = await dbContext.EquipmentHandoverItems.AsNoTracking()
            .Where(i => i.EquipmentHandoverId == handoverId)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        return !current.ToHashSet().SetEquals(validatedItemIds);
    }
}
