import type { EquipmentStatus } from '../equipment/types'

export type EquipmentRetirementRequestStatus = 'Pending' | 'Approved' | 'Rejected'

/** Row shape for the Administration-only "Solicitações de abate" review list. */
export interface EquipmentRetirementRequest {
  id: string
  equipmentId: string
  assetTag: string
  equipmentTypeName: string
  manufacturer?: string | null
  model?: string | null
  equipmentStatus: EquipmentStatus
  requestedByDisplayName: string
  requestedAtUtc: string
  requestReason: string
  status: EquipmentRetirementRequestStatus
  decidedByDisplayName?: string | null
  decidedAtUtc?: string | null
  decisionReason?: string | null
}

export interface EquipmentRetirementRequestListParams {
  status?: EquipmentRetirementRequestStatus
  page: number
  pageSize: number
}

export interface CreateEquipmentRetirementRequestValues {
  equipmentId: string
  reason: string
}
