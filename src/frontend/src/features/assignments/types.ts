import type { EquipmentAssignmentOrigin, EquipmentStatus } from '../equipment/types'

export type AssignmentStatus = 'Active' | 'Ended'

export interface EquipmentAssignment {
  id: string
  equipmentId: string
  assetTag: string
  equipmentTypeName: string
  manufacturer?: string | null
  model?: string | null
  consultantId: string
  consultantFullName: string
  status: AssignmentStatus
  /** Absent for custody established by the Current Inventory import. */
  equipmentHandoverId?: string | null
  origin: EquipmentAssignmentOrigin
  startedAtUtc: string
  endedAtUtc?: string | null
  /** The equipment's live lifecycle status. On an Active assignment, `ReturnPending` means its return is already initiated; custody is unchanged until physical receipt. */
  equipmentStatus: EquipmentStatus
}

export interface AssignmentListParams {
  equipmentId?: string
  consultantId?: string
  status?: AssignmentStatus
  page: number
  pageSize: number
}

export interface InitiateReturnValues {
  expectedReturnDate?: string
  reason?: string
}
