import type { CurrentUser } from '../features/auth/types'
import { Permissions } from '../shared/permissions'

/**
 * Mirrors the two roles' permission sets in
 * src/backend/EquipmentManagement.Domain/Users/RolePermissions.cs — the API is
 * the real authority; these only shape what the UI shows in tests.
 */
const hrPermissions: string[] = [
  Permissions.Company.Read,
  Permissions.Company.Create,
  Permissions.Company.Update,
  ...Object.values(Permissions.Consultant),
  ...Object.values(Permissions.ConsultantAllocation),
  Permissions.EquipmentType.Read,
  Permissions.EquipmentType.Create,
  Permissions.EquipmentType.Update,
  Permissions.Equipment.Read,
  Permissions.Equipment.Create,
  Permissions.Equipment.Update,
  Permissions.Equipment.Assign,
  Permissions.Equipment.Return,
  Permissions.Equipment.Import,
  Permissions.Equipment.Export,
  Permissions.Equipment.PhotosManage,
  ...Object.values(Permissions.EquipmentHandover),
  Permissions.EquipmentAssignment.Read,
  ...Object.values(Permissions.EquipmentReturn),
  ...Object.values(Permissions.EquipmentMaintenance),
  Permissions.EquipmentRetirementRequest.Create,
  ...Object.values(Permissions.ResponsibilityTerm),
]

const administrationPermissions: string[] = [
  ...Object.values(Permissions.User),
  ...Object.values(Permissions.Company),
  ...Object.values(Permissions.Consultant),
  ...Object.values(Permissions.ConsultantAllocation),
  ...Object.values(Permissions.EquipmentType),
  ...Object.values(Permissions.Equipment),
  ...Object.values(Permissions.EquipmentHandover),
  Permissions.EquipmentAssignment.Read,
  ...Object.values(Permissions.EquipmentReturn),
  ...Object.values(Permissions.EquipmentMaintenance),
  Permissions.EquipmentRetirementRequest.Read,
  Permissions.EquipmentRetirementRequest.Decide,
  ...Object.values(Permissions.ResponsibilityTerm),
  Permissions.Audit.Read,
]

export const administrationUser: CurrentUser = {
  id: '00000000-0000-0000-0000-000000000001',
  firstName: 'Eva',
  lastName: 'Example',
  email: 'eva.example@example.test',
  role: 'Administration',
  permissions: administrationPermissions,
  mustChangePassword: false,
}

export const hrUser: CurrentUser = {
  id: '00000000-0000-0000-0000-000000000002',
  firstName: 'Ana',
  lastName: 'Demo',
  email: 'ana.demo@example.test',
  role: 'HR',
  permissions: hrPermissions,
  mustChangePassword: false,
}
