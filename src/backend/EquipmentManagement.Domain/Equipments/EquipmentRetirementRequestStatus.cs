namespace EquipmentManagement.Domain.Equipments;

/// <summary>The minimal retirement-request lifecycle: HR requests, Administration decides. No multi-level approval, no cancellation-by-requester.</summary>
public enum EquipmentRetirementRequestStatus
{
    Pending,
    Approved,
    Rejected,
}
