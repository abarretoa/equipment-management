using EquipmentManagement.Domain.Handovers;

namespace EquipmentManagement.Infrastructure.Handovers;

public static class EquipmentDispatchQueries
{
    /// <summary>
    /// Dispatches that have not been cancelled ("Anular envio"). Every
    /// operational question — is this handover dispatched, is its composition
    /// locked, may it be confirmed or have a term prepared — is about the
    /// active dispatch only; cancelled attempts are history.
    /// </summary>
    public static IQueryable<EquipmentDispatch> Active(this IQueryable<EquipmentDispatch> dispatches) =>
        dispatches.Where(d => d.CancelledAtUtc == null);
}
