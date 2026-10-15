import type { EquipmentStatus } from '../equipment/types'

export type HandoverStatus = 'Draft' | 'Finalized'
/** Client-facing completion status (Iteration 1) — derived server-side, distinct from the internal `status` lifecycle field. */
export type HandoverOperationalStatus = 'Draft' | 'AwaitingSignedTerm' | 'Completed'
export type EquipmentCondition = 'Excellent' | 'Good' | 'Fair' | 'Poor'
/** How the equipment reaches the Consultant (Iteration 3). Null on a handover means it predates the field and was never backfilled. */
export type HandoverDeliveryMethod = 'InPerson' | 'Carrier'

export interface HandoverSummary {
  id: string
  consultantId: string
  consultantFullName: string
  status: HandoverStatus
  operationalStatus: HandoverOperationalStatus
  deliveredOn?: string | null
  itemCount: number
  createdAtUtc: string
  finalizedAtUtc?: string | null
}

export interface InspectionPhoto {
  id: string
  originalFileName: string
  sizeBytes: number
  uploadedAtUtc: string
}

export interface DeliveryInspection {
  id: string
  condition: EquipmentCondition
  notes?: string | null
  inspectedByDisplayNameSnapshot: string
  inspectedAtUtc: string
  photos: InspectionPhoto[]
}

export interface HandoverItem {
  id: string
  equipmentId: string
  assetTag: string
  equipmentTypeName: string
  manufacturer?: string | null
  model?: string | null
  serialNumber?: string | null
  inspection?: DeliveryInspection | null
  /** The equipment's live status. In a Draft, an item that is no longer Available (or, once dispatched, no longer InTransit) cannot progress. */
  equipmentStatus?: EquipmentStatus
}

/** Present once "Registar envio" has been used — its mere presence is what locks the handover's item composition (see the backend's `HandoverRules.CanModifyComposition`); the backend enforces the lock regardless of what the client displays. */
export interface HandoverDispatch {
  id: string
  equipmentHandoverId: string
  expectedDeliveryFromDate: string
  expectedDeliveryToDate?: string | null
  dispatchedAtUtc: string
  dispatchedByDisplayNameSnapshot: string
}

export interface HandoverDetail {
  id: string
  consultantId: string
  consultantFullName: string
  status: HandoverStatus
  operationalStatus: HandoverOperationalStatus
  deliveryMethod?: HandoverDeliveryMethod | null
  deliveredOn?: string | null
  notes?: string | null
  createdByDisplayNameSnapshot: string
  createdAtUtc: string
  updatedAtUtc: string
  finalizedAtUtc?: string | null
  items: HandoverItem[]
  dispatch?: HandoverDispatch | null
  /** Dispatch attempts cancelled by "Anular envio", newest first — history only. */
  cancelledDispatches?: CancelledDispatch[] | null
}

/** A dispatch attempt cancelled by "Anular envio" before delivery was confirmed. */
export interface CancelledDispatch {
  id: string
  dispatchedAtUtc: string
  dispatchedByDisplayNameSnapshot: string
  cancelledAtUtc: string
  cancelledByDisplayNameSnapshot: string
  cancellationReason: string
}

export interface RegisterDispatchValues {
  expectedDeliveryFromDate: string
  expectedDeliveryToDate?: string
}

export interface HandoverListParams {
  consultantId?: string
  status?: HandoverStatus
  search?: string
  page: number
  pageSize: number
}

export interface RecordInspectionValues {
  condition: EquipmentCondition
  notes?: string
}
