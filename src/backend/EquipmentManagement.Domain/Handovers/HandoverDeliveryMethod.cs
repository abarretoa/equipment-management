namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// How a handover's equipment physically reaches the Consultant (Iteration
/// 3). Chosen explicitly by HR and persisted on
/// <see cref="EquipmentHandover.DeliveryMethod"/>; never inferred from free
/// text or from whether an <see cref="EquipmentDispatch"/> happens to exist.
/// It decides which operational steps apply before "Confirmar entrega"
/// (finalization): see <see cref="HandoverRules.RequiresDispatchBeforeFinalize"/>
/// and <see cref="HandoverRules.AllowsDispatch"/>.
/// </summary>
public enum HandoverDeliveryMethod
{
    /// <summary>"Entrega presencial": handed over in person. No shipping event exists, so no dispatch is registered.</summary>
    InPerson,

    /// <summary>"Envio por transportadora": shipped. A dispatch ("Registar envio") must be registered before the delivery can be confirmed.</summary>
    Carrier,
}
