using EquipmentManagement.Domain.Users;

namespace EquipmentManagement.UnitTests.Domain.Users;

public class PermissionsTests
{
    [Fact]
    public void All_ContainsNoDuplicates()
    {
        Assert.Equal(Permissions.All.Count, Permissions.All.Distinct().Count());
    }

    [Fact]
    public void All_ContainsEveryDeclaredPermission()
    {
        string[] declared =
        [
            Permissions.User.Read,
            Permissions.User.Create,
            Permissions.User.Update,
            Permissions.User.Archive,
            Permissions.User.AssignRole,
            Permissions.Company.Read,
            Permissions.Company.Create,
            Permissions.Company.Update,
            Permissions.Company.Archive,
            Permissions.Consultant.Read,
            Permissions.Consultant.Create,
            Permissions.Consultant.Update,
            Permissions.Consultant.Archive,
            Permissions.Consultant.Import,
            Permissions.Consultant.Export,
            Permissions.ConsultantAllocation.Read,
            Permissions.ConsultantAllocation.Create,
            Permissions.ConsultantAllocation.Update,
            Permissions.ConsultantAllocation.End,
            Permissions.EquipmentType.Read,
            Permissions.EquipmentType.Create,
            Permissions.EquipmentType.Update,
            Permissions.EquipmentType.Archive,
            Permissions.Equipment.Read,
            Permissions.Equipment.Create,
            Permissions.Equipment.Update,
            Permissions.Equipment.Retire,
            Permissions.Equipment.Assign,
            Permissions.Equipment.Return,
            Permissions.Equipment.Import,
            Permissions.Equipment.Export,
            Permissions.Equipment.PhotosManage,
            Permissions.Equipment.PhotosReplace,
            Permissions.EquipmentHandover.Read,
            Permissions.EquipmentHandover.Create,
            Permissions.EquipmentHandover.Update,
            Permissions.EquipmentHandover.Dispatch,
            Permissions.EquipmentHandover.CancelDispatch,
            Permissions.EquipmentAssignment.Read,
            Permissions.EquipmentReturn.Read,
            Permissions.EquipmentReturn.Update,
            Permissions.EquipmentMaintenance.Read,
            Permissions.EquipmentMaintenance.Manage,
            Permissions.EquipmentRetirementRequest.Create,
            Permissions.EquipmentRetirementRequest.Read,
            Permissions.EquipmentRetirementRequest.Decide,
            Permissions.ResponsibilityTerm.Read,
            Permissions.ResponsibilityTerm.Generate,
            Permissions.ResponsibilityTerm.Send,
            Permissions.ResponsibilityTerm.SignatureManage,
            Permissions.Audit.Read,
        ];

        Assert.Equal(declared.OrderBy(p => p), Permissions.All.OrderBy(p => p));
    }
}
