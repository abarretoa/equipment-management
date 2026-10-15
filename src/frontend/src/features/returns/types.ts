import type { EquipmentCondition, InspectionPhoto } from '../handovers/types'
import type { EquipmentStatus } from '../equipment/types'

export type ReturnStatus = 'Pending' | 'Received' | 'Completed' | 'Cancelled'
export type DamageStatus = 'Open' | 'Resolved'

export interface ReturnSummary {
  id: string
  equipmentAssignmentId: string
  equipmentId: string
  assetTag: string
  consultantId: string
  consultantFullName: string
  status: ReturnStatus
  reason?: string | null
  expectedReturnDate?: string | null
  receivedOn?: string | null
  createdAtUtc: string
  completedAtUtc?: string | null
}

export interface DamageInfo {
  id: string
  description: string
  status: DamageStatus
  reportedAtUtc: string
  resolvedAtUtc?: string | null
  photos: InspectionPhoto[]
}

export interface ReturnInspection {
  id: string
  condition: EquipmentCondition
  notes?: string | null
  hasDamage: boolean
  damageDescription?: string | null
  inspectedByDisplayNameSnapshot: string
  inspectedAtUtc: string
  finalizedAtUtc?: string | null
  photos: InspectionPhoto[]
  damagePhotos: InspectionPhoto[]
  damage?: DamageInfo | null
}

export interface EquipmentReturnDetail {
  id: string
  equipmentAssignmentId: string
  equipmentId: string
  assetTag: string
  equipmentTypeName: string
  consultantId: string
  consultantFullName: string
  status: ReturnStatus
  reason?: string | null
  expectedReturnDate?: string | null
  receivedOn?: string | null
  receivedAtUtc?: string | null
  receivedByDisplayNameSnapshot?: string | null
  createdByDisplayNameSnapshot: string
  createdAtUtc: string
  updatedAtUtc: string
  completedAtUtc?: string | null
  cancelledAtUtc?: string | null
  cancelledByDisplayNameSnapshot?: string | null
  cancellationReason?: string | null
  inspection?: ReturnInspection | null
  /** The initiation event this return was created by. Null only for returns that predate grouping. */
  returnGroupId?: string | null
  /** How many returns that group holds (0 when there is no group). */
  returnGroupSize: number
}

/** Derived by the server from the group's returns — never stored. */
export type ReturnGroupCollectionStatus = 'Awaiting' | 'Partial' | 'Collected' | 'Cancelled'

/** One return inside a group. Every action on it still lives on its own page. */
export interface ReturnGroupItem {
  returnId: string
  equipmentId: string
  assetTag: string
  equipmentTypeName: string
  manufacturer?: string | null
  model?: string | null
  status: ReturnStatus
  equipmentStatus: EquipmentStatus
  expectedReturnDate?: string | null
  receivedOn?: string | null
}

/**
 * One return-initiation event (an end of engagement, or a manual return of one
 * or more items) shown as one collection. It groups independent returns; it is
 * not a return itself.
 */
export interface ReturnGroup {
  id: string
  consultantId: string
  consultantFullName: string
  isEndOfEngagement: boolean
  collectionStatus: ReturnGroupCollectionStatus
  expectedFromDate?: string | null
  expectedToDate?: string | null
  receivedCount: number
  createdByDisplayNameSnapshot: string
  createdAtUtc: string
  items: ReturnGroupItem[]
}

/** One row of the Returns list: a group, or a return with no group (created before grouping existed). Exactly one is set. */
export interface ReturnListRow {
  group?: ReturnGroup | null
  return?: ReturnSummary | null
}

export interface ReturnGroupDetail {
  group: ReturnGroup
  /** The same consultant's other returns still awaiting physical receipt, which are not part of this group. */
  otherPendingReturns: ReturnSummary[]
}

export interface ReturnOverviewParams {
  status?: ReturnStatus
  search?: string
  page: number
  pageSize: number
}

export interface StartReturnGroupValues {
  consultantId: string
  equipmentAssignmentIds: string[]
  expectedReturnDate?: string
  reason?: string
}

export interface StartReturnGroupResult {
  returnGroupId: string
  returnsCreatedCount: number
  returnIds: string[]
}

export interface ReturnListParams {
  consultantId?: string
  equipmentId?: string
  status?: ReturnStatus
  search?: string
  page: number
  pageSize: number
}

export interface ReceiveReturnValues {
  receivedOn: string
  receivedByUserId: string
  /** Exactly 3 for Laptop/MobilePhone equipment, empty otherwise — the official condition-photo refresh, enforced server-side. */
  photos?: File[]
}

export interface UpdateReturnInspectionValues {
  condition: EquipmentCondition
  notes?: string
  hasDamage: boolean
  damageDescription?: string
}
