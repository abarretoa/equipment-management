namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// Where an <see cref="EquipmentPhotoSet"/> came from. Exactly two write
/// paths ever create one: new Laptop/MobilePhone registration or the legacy
/// backfill action (both <see cref="InitialRegistration"/>), and Return
/// physical receipt (<see cref="Return"/>) — see
/// <see cref="EquipmentPhotoSet.EquipmentReturnId"/>'s invariant.
/// </summary>
public enum PhotoSetSource
{
    InitialRegistration,
    Return,
}
