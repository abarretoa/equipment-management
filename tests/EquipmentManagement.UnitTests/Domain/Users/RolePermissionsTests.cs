using EquipmentManagement.Domain.Users;

namespace EquipmentManagement.UnitTests.Domain.Users;

public class RolePermissionsTests
{
    [Fact]
    public void Administration_HasFullUserManagementAndAuditAccess()
    {
        var permissions = RolePermissions.GetPermissions(Roles.Administration);

        Assert.Contains(Permissions.User.Read, permissions);
        Assert.Contains(Permissions.User.Create, permissions);
        Assert.Contains(Permissions.User.Update, permissions);
        Assert.Contains(Permissions.User.Archive, permissions);
        Assert.Contains(Permissions.User.AssignRole, permissions);
        Assert.Contains(Permissions.Audit.Read, permissions);
    }

    [Fact]
    public void Administration_HasFullPhase2BusinessAccess()
    {
        var permissions = RolePermissions.GetPermissions(Roles.Administration);

        Assert.Contains(Permissions.Company.Read, permissions);
        Assert.Contains(Permissions.Company.Create, permissions);
        Assert.Contains(Permissions.Company.Update, permissions);
        Assert.Contains(Permissions.Company.Archive, permissions);
        Assert.Contains(Permissions.Consultant.Read, permissions);
        Assert.Contains(Permissions.Consultant.Create, permissions);
        Assert.Contains(Permissions.Consultant.Update, permissions);
        Assert.Contains(Permissions.Consultant.Archive, permissions);
        Assert.Contains(Permissions.ConsultantAllocation.Read, permissions);
        Assert.Contains(Permissions.ConsultantAllocation.Create, permissions);
        Assert.Contains(Permissions.ConsultantAllocation.Update, permissions);
        Assert.Contains(Permissions.ConsultantAllocation.End, permissions);
    }

    [Fact]
    public void Administration_HasFullPhase3EquipmentAccess()
    {
        var permissions = RolePermissions.GetPermissions(Roles.Administration);

        Assert.Contains(Permissions.EquipmentType.Read, permissions);
        Assert.Contains(Permissions.EquipmentType.Create, permissions);
        Assert.Contains(Permissions.EquipmentType.Update, permissions);
        Assert.Contains(Permissions.EquipmentType.Archive, permissions);
        Assert.Contains(Permissions.Equipment.Read, permissions);
        Assert.Contains(Permissions.Equipment.Create, permissions);
        Assert.Contains(Permissions.Equipment.Update, permissions);
        Assert.Contains(Permissions.Equipment.Retire, permissions);
    }

    [Fact]
    public void Administration_HasFullPhase4HandoverAccess()
    {
        var permissions = RolePermissions.GetPermissions(Roles.Administration);

        Assert.Contains(Permissions.Equipment.Assign, permissions);
        Assert.Contains(Permissions.EquipmentHandover.Read, permissions);
        Assert.Contains(Permissions.EquipmentHandover.Create, permissions);
        Assert.Contains(Permissions.EquipmentHandover.Update, permissions);
        Assert.Contains(Permissions.EquipmentHandover.CancelDispatch, permissions);
        Assert.Contains(Permissions.EquipmentAssignment.Read, permissions);
    }

    [Fact]
    public void Administration_HasFullPhase5ReturnDamageMaintenanceAccess()
    {
        var permissions = RolePermissions.GetPermissions(Roles.Administration);

        Assert.Contains(Permissions.Equipment.Return, permissions);
        Assert.Contains(Permissions.EquipmentReturn.Read, permissions);
        Assert.Contains(Permissions.EquipmentReturn.Update, permissions);
        Assert.Contains(Permissions.EquipmentMaintenance.Read, permissions);
        Assert.Contains(Permissions.EquipmentMaintenance.Manage, permissions);
    }

    [Fact]
    public void Administration_HasFullPhase6ResponsibilityTermAccess()
    {
        var permissions = RolePermissions.GetPermissions(Roles.Administration);

        Assert.Contains(Permissions.ResponsibilityTerm.Read, permissions);
        Assert.Contains(Permissions.ResponsibilityTerm.Generate, permissions);
    }

    [Fact]
    public void Administration_HasFullPhase7DeliveryAndSignatureAccess()
    {
        var permissions = RolePermissions.GetPermissions(Roles.Administration);

        Assert.Contains(Permissions.ResponsibilityTerm.Send, permissions);
        Assert.Contains(Permissions.ResponsibilityTerm.SignatureManage, permissions);
    }

    [Fact]
    public void Hr_HasNoUserManagementPermissions()
    {
        var permissions = RolePermissions.GetPermissions(Roles.HR);

        Assert.DoesNotContain(Permissions.User.Create, permissions);
        Assert.DoesNotContain(Permissions.User.Update, permissions);
        Assert.DoesNotContain(Permissions.User.Archive, permissions);
        Assert.DoesNotContain(Permissions.User.AssignRole, permissions);
    }

    [Fact]
    public void Hr_HasNoAuditAccess()
    {
        var permissions = RolePermissions.GetPermissions(Roles.HR);

        Assert.DoesNotContain(Permissions.Audit.Read, permissions);
    }

    [Fact]
    public void Hr_CanCreateAndUpdateCompaniesButNotArchive()
    {
        // Approved Phase 2 permission adjustment: HR may create/update
        // Companies (day-to-day reference-data entry) but not archive one,
        // which removes it from normal operational circulation.
        var permissions = RolePermissions.GetPermissions(Roles.HR);

        Assert.Contains(Permissions.Company.Read, permissions);
        Assert.Contains(Permissions.Company.Create, permissions);
        Assert.Contains(Permissions.Company.Update, permissions);
        Assert.DoesNotContain(Permissions.Company.Archive, permissions);
    }

    [Fact]
    public void Hr_HasFullConsultantAndAllocationAccess()
    {
        var permissions = RolePermissions.GetPermissions(Roles.HR);

        Assert.Contains(Permissions.Consultant.Read, permissions);
        Assert.Contains(Permissions.Consultant.Create, permissions);
        Assert.Contains(Permissions.Consultant.Update, permissions);
        Assert.Contains(Permissions.Consultant.Archive, permissions);

        Assert.Contains(Permissions.ConsultantAllocation.Read, permissions);
        Assert.Contains(Permissions.ConsultantAllocation.Create, permissions);
        Assert.Contains(Permissions.ConsultantAllocation.Update, permissions);
        Assert.Contains(Permissions.ConsultantAllocation.End, permissions);
    }

    [Fact]
    public void Hr_HasOperationalEquipmentAccessButNotArchiveOrRetire()
    {
        // Approved Phase 3 permission matrix, mirroring the Company split:
        // HR does day-to-day create/update; Administration alone can remove
        // something from normal operational circulation.
        var permissions = RolePermissions.GetPermissions(Roles.HR);

        Assert.Contains(Permissions.EquipmentType.Read, permissions);
        Assert.Contains(Permissions.EquipmentType.Create, permissions);
        Assert.Contains(Permissions.EquipmentType.Update, permissions);
        Assert.DoesNotContain(Permissions.EquipmentType.Archive, permissions);

        Assert.Contains(Permissions.Equipment.Read, permissions);
        Assert.Contains(Permissions.Equipment.Create, permissions);
        Assert.Contains(Permissions.Equipment.Update, permissions);
        Assert.DoesNotContain(Permissions.Equipment.Retire, permissions);
    }

    [Fact]
    public void Hr_RunsTheFullHandoverWorkflowIncludingFinalization()
    {
        // HR performs the entire Phase 4 delivery workflow end to end
        // (docs/PROJECT_SPEC.md, section 11), so it gets Equipment.Assign too —
        // unlike Retire/Archive, finalizing a handover is normal HR operation.
        var permissions = RolePermissions.GetPermissions(Roles.HR);

        Assert.Contains(Permissions.Equipment.Assign, permissions);
        Assert.Contains(Permissions.EquipmentHandover.Read, permissions);
        Assert.Contains(Permissions.EquipmentHandover.Create, permissions);
        Assert.Contains(Permissions.EquipmentHandover.Update, permissions);
        Assert.Contains(Permissions.EquipmentHandover.CancelDispatch, permissions);
        Assert.Contains(Permissions.EquipmentAssignment.Read, permissions);
    }

    [Fact]
    public void Hr_RunsTheFullReturnDamageMaintenanceWorkflow()
    {
        // HR operationally owns the entire Phase 5 workflow end to end, same
        // shape as the Phase 4 handover decision (Phase 5 scope).
        var permissions = RolePermissions.GetPermissions(Roles.HR);

        Assert.Contains(Permissions.Equipment.Return, permissions);
        Assert.Contains(Permissions.EquipmentReturn.Read, permissions);
        Assert.Contains(Permissions.EquipmentReturn.Update, permissions);
        Assert.Contains(Permissions.EquipmentMaintenance.Read, permissions);
        Assert.Contains(Permissions.EquipmentMaintenance.Manage, permissions);
    }

    [Fact]
    public void Administration_ReviewsAndDecidesRetirementRequestsButDoesNotNeedToCreateOne()
    {
        // Administration retires equipment directly (Equipment.Retire) and
        // alone reviews/decides HR's requests — it never needs to *request*
        // what it can already do, so it does not get
        // EquipmentRetirementRequest.Create.
        var permissions = RolePermissions.GetPermissions(Roles.Administration);

        Assert.Contains(Permissions.Equipment.Retire, permissions);
        Assert.Contains(Permissions.EquipmentRetirementRequest.Read, permissions);
        Assert.Contains(Permissions.EquipmentRetirementRequest.Decide, permissions);
        Assert.DoesNotContain(Permissions.EquipmentRetirementRequest.Create, permissions);
    }

    [Fact]
    public void Hr_CanCreateARetirementRequestButCannotReviewOrDecideOrRetireDirectly()
    {
        // The final approved business rule: HR requests, Administration alone
        // executes retirement and alone reviews/decides requests.
        var permissions = RolePermissions.GetPermissions(Roles.HR);

        Assert.Contains(Permissions.EquipmentRetirementRequest.Create, permissions);
        Assert.DoesNotContain(Permissions.EquipmentRetirementRequest.Read, permissions);
        Assert.DoesNotContain(Permissions.EquipmentRetirementRequest.Decide, permissions);
        Assert.DoesNotContain(Permissions.Equipment.Retire, permissions);
    }

    [Fact]
    public void Hr_HasFullResponsibilityTermAccessButNoUnrelatedPermissionRegressions()
    {
        // Phase 6 scope: HR gets both Responsibility Term
        // permissions (no separate "Regenerate" permission is needed — it is
        // the same operation as generation), but this must not grant HR any
        // permission it did not already have.
        var permissions = RolePermissions.GetPermissions(Roles.HR);

        Assert.Contains(Permissions.ResponsibilityTerm.Read, permissions);
        Assert.Contains(Permissions.ResponsibilityTerm.Generate, permissions);
        Assert.Contains(Permissions.ResponsibilityTerm.Send, permissions);
        Assert.Contains(Permissions.ResponsibilityTerm.SignatureManage, permissions);

        Assert.DoesNotContain(Permissions.Audit.Read, permissions);
        Assert.DoesNotContain(Permissions.User.Create, permissions);
        Assert.DoesNotContain(Permissions.User.Update, permissions);
        Assert.DoesNotContain(Permissions.User.Archive, permissions);
        Assert.DoesNotContain(Permissions.User.AssignRole, permissions);
        Assert.DoesNotContain(Permissions.Equipment.Retire, permissions);
        Assert.DoesNotContain(Permissions.Company.Archive, permissions);
    }

    [Fact]
    public void Administration_HasFullPhase8ImportExportAccess()
    {
        var permissions = RolePermissions.GetPermissions(Roles.Administration);

        Assert.Contains(Permissions.Consultant.Import, permissions);
        Assert.Contains(Permissions.Consultant.Export, permissions);
        Assert.Contains(Permissions.Equipment.Import, permissions);
        Assert.Contains(Permissions.Equipment.Export, permissions);
    }

    [Fact]
    public void Hr_HasFullPhase8ImportExportAccessButNoUnrelatedPermissionRegressions()
    {
        // Phase 8 scope: HR runs Consultant/Equipment Excel
        // import-export identically to Administration — normal HR
        // operational work, same shape as every prior phase's grant.
        var permissions = RolePermissions.GetPermissions(Roles.HR);

        Assert.Contains(Permissions.Consultant.Import, permissions);
        Assert.Contains(Permissions.Consultant.Export, permissions);
        Assert.Contains(Permissions.Equipment.Import, permissions);
        Assert.Contains(Permissions.Equipment.Export, permissions);

        Assert.DoesNotContain(Permissions.Audit.Read, permissions);
        Assert.DoesNotContain(Permissions.User.Create, permissions);
        Assert.DoesNotContain(Permissions.Equipment.Retire, permissions);
        Assert.DoesNotContain(Permissions.EquipmentType.Archive, permissions);
        Assert.DoesNotContain(Permissions.Company.Archive, permissions);
    }

    [Fact]
    public void OnlyAdministration_CorrectsCurrentEquipmentPhotos_WhileBothRolesKeepNormalPhotoRegistration()
    {
        // "Alterar fotografias" is an administrative correction outside
        // custody; HR's normal photo work (registration, Return receipt) is
        // unaffected and never grants it.
        var administration = RolePermissions.GetPermissions(Roles.Administration);
        var hr = RolePermissions.GetPermissions(Roles.HR);

        Assert.Contains(Permissions.Equipment.PhotosReplace, administration);
        Assert.DoesNotContain(Permissions.Equipment.PhotosReplace, hr);
        Assert.Contains(Permissions.Equipment.PhotosManage, administration);
        Assert.Contains(Permissions.Equipment.PhotosManage, hr);
        Assert.Contains(Permissions.Equipment.Return, hr);
    }

    [Fact]
    public void UnknownRole_HasNoPermissions()
    {
        var permissions = RolePermissions.GetPermissions("SomeMadeUpRole");

        Assert.Empty(permissions);
    }
}
