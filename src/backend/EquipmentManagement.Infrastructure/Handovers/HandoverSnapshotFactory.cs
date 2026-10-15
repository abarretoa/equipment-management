using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Consultants;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;

namespace EquipmentManagement.Infrastructure.Handovers;

/// <summary>
/// The single mapping from live Consultant/Allocation/Equipment rows to the
/// frozen snapshots. Shared by the two places that freeze live data:
/// <c>EquipmentHandoverService.FinalizeAsync</c> ("Confirmar entrega", the
/// Handover's delivery record) and, for a carrier term prepared after
/// "Registar envio", <c>ResponsibilityTermService.GenerateAsync</c> (that term
/// version's own snapshot), so both freeze exactly the same values.
/// </summary>
internal static class HandoverSnapshotFactory
{
    public static ConsultantSnapshot ForConsultant(Consultant consultant) => new()
    {
        FullName = consultant.FullName,
        Nif = consultant.Nif,
        Address = consultant.Address,
        Phone = consultant.Phone,
        Email = consultant.Email,
    };

    public static AllocationSnapshot ForAllocation(ConsultantAllocation allocation, string? companyName) => new()
    {
        WorkforceType = allocation.WorkforceType.ToString(),
        EngagementType = allocation.EngagementType.ToString(),
        RoleOrFunction = allocation.RoleOrFunction,
        CompanyName = companyName,
    };

    public static EquipmentSnapshot ForEquipment(
        Equipment equipment, EquipmentType type, LaptopSpecification? spec, MobilePhoneSpecification? mobileSpec) => new()
    {
        AssetTag = equipment.AssetTag,
        EquipmentTypeName = type.Name,
        Manufacturer = equipment.Manufacturer,
        Model = equipment.Model,
        SerialNumber = equipment.SerialNumber,
        Processor = spec?.Processor,
        Memory = spec?.Memory,
        Storage = spec?.Storage,
        Kind = type.Kind.ToString(),
        Imei = mobileSpec?.Imei,
        MobileNumber = mobileSpec?.MobileNumber,
        SimSerialNumber = mobileSpec?.SimSerialNumber,
        Notes = equipment.Notes,
    };

    // Value copies. An owned snapshot instance belongs to exactly one row, so
    // a Responsibility Term version and the Handover's delivery record never
    // share one instance; each gets its own copy of the same values.
    public static ConsultantSnapshot Copy(ConsultantSnapshot s) => new()
    {
        FullName = s.FullName,
        Nif = s.Nif,
        Address = s.Address,
        Phone = s.Phone,
        Email = s.Email,
    };

    public static AllocationSnapshot? Copy(AllocationSnapshot? s) => s is null ? null : new()
    {
        WorkforceType = s.WorkforceType,
        EngagementType = s.EngagementType,
        RoleOrFunction = s.RoleOrFunction,
        CompanyName = s.CompanyName,
    };

    public static EquipmentSnapshot Copy(EquipmentSnapshot s) => new()
    {
        AssetTag = s.AssetTag,
        EquipmentTypeName = s.EquipmentTypeName,
        Manufacturer = s.Manufacturer,
        Model = s.Model,
        SerialNumber = s.SerialNumber,
        Processor = s.Processor,
        Memory = s.Memory,
        Storage = s.Storage,
        Kind = s.Kind,
        Imei = s.Imei,
        MobileNumber = s.MobileNumber,
        SimSerialNumber = s.SimSerialNumber,
        Notes = s.Notes,
    };
}
