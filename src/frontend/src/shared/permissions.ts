/**
 * Mirrors backend `EquipmentManagement.Domain.Users.Permissions` exactly (see
 * scratchpad backend-api-contract.md). Used only to shape the UI (hide/disable
 * nav and actions) — the API remains the sole authority (docs/PROJECT_SPEC.md §4).
 */
export const Permissions = {
  User: { Read: 'User.Read', Create: 'User.Create', Update: 'User.Update', Archive: 'User.Archive', AssignRole: 'User.AssignRole' },
  Audit: { Read: 'Audit.Read' },
  Company: { Read: 'Company.Read', Create: 'Company.Create', Update: 'Company.Update', Archive: 'Company.Archive' },
  Consultant: {
    Read: 'Consultant.Read',
    Create: 'Consultant.Create',
    Update: 'Consultant.Update',
    Archive: 'Consultant.Archive',
    Import: 'Consultant.Import',
    Export: 'Consultant.Export',
  },
  ConsultantAllocation: {
    Read: 'ConsultantAllocation.Read',
    Create: 'ConsultantAllocation.Create',
    Update: 'ConsultantAllocation.Update',
    End: 'ConsultantAllocation.End',
  },
  EquipmentType: { Read: 'EquipmentType.Read', Create: 'EquipmentType.Create', Update: 'EquipmentType.Update', Archive: 'EquipmentType.Archive' },
  Equipment: {
    Read: 'Equipment.Read',
    Create: 'Equipment.Create',
    Update: 'Equipment.Update',
    Retire: 'Equipment.Retire',
    Assign: 'Equipment.Assign',
    Return: 'Equipment.Return',
    Import: 'Equipment.Import',
    Export: 'Equipment.Export',
    PhotosManage: 'Equipment.PhotosManage',
    PhotosReplace: 'Equipment.PhotosReplace',
  },
  EquipmentHandover: {
    Read: 'EquipmentHandover.Read',
    Create: 'EquipmentHandover.Create',
    Update: 'EquipmentHandover.Update',
    Dispatch: 'EquipmentHandover.Dispatch',
    CancelDispatch: 'EquipmentHandover.CancelDispatch',
  },
  EquipmentAssignment: { Read: 'EquipmentAssignment.Read' },
  EquipmentReturn: { Read: 'EquipmentReturn.Read', Update: 'EquipmentReturn.Update' },
  EquipmentMaintenance: { Read: 'EquipmentMaintenance.Read', Manage: 'EquipmentMaintenance.Manage' },
  EquipmentRetirementRequest: {
    Create: 'EquipmentRetirementRequest.Create',
    Read: 'EquipmentRetirementRequest.Read',
    Decide: 'EquipmentRetirementRequest.Decide',
  },
  ResponsibilityTerm: {
    Read: 'ResponsibilityTerm.Read',
    Generate: 'ResponsibilityTerm.Generate',
    Send: 'ResponsibilityTerm.Send',
    SignatureManage: 'ResponsibilityTerm.SignatureManage',
  },
} as const

/** The Current Inventory import creates Consultants/Allocations, creates Equipment and establishes custody: it needs ALL three (same as the API's stacked policies). */
export const CurrentInventoryImportPermissions = [
  Permissions.Consultant.Import,
  Permissions.Equipment.Import,
  Permissions.Equipment.Assign,
] as const
