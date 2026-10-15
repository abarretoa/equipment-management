import type { WorkforceType, EngagementType } from '../consultants/types'
import type { EquipmentCondition } from '../handovers/types'
import type { MaintenanceOutcome } from '../maintenance/types'

export type EquipmentStatus =
  | 'Available'
  | 'InTransit'
  | 'Assigned'
  | 'ReturnPending'
  | 'UnderInspection'
  | 'Damaged'
  | 'UnderMaintenance'
  | 'Retired'

export interface LaptopSpecification {
  processor: string
  memory: string
  storage: string
}

/** Domain persistence stays carrier-neutral (`mobileNumber`) even when the client profile labels it after its mobile operator (see `mobileNumberLabel`). */
export interface MobilePhoneSpecification {
  imei: string
  mobileNumber?: string | null
  simSerialNumber?: string | null
}

export type PhotoSetSource = 'InitialRegistration' | 'Return'

export interface EquipmentPhoto {
  id: string
  originalFileName: string
  contentType: string
  sizeBytes: number
  sortOrder: number
  uploadedAtUtc: string
}

/** The equipment's current condition photo set (exactly 3 photos) — absent for legacy Laptop/MobilePhone equipment that predates this feature. */
export interface EquipmentPhotoSet {
  id: string
  source: PhotoSetSource
  createdAtUtc: string
  photos: EquipmentPhoto[]
}

export interface EquipmentSummary {
  id: string
  assetTag: string
  equipmentTypeName: string
  manufacturer?: string | null
  model?: string | null
  serialNumber?: string | null
  status: EquipmentStatus
  createdAtUtc: string
  currentConsultantId?: string | null
  currentConsultantName?: string | null
}

/**
 * Current custody — resolved exclusively from the equipment's Active-status
 * EquipmentAssignment, never from EquipmentStatus. Regime/Empresa/Função
 * reflect the consultant's current allocation and may legitimately be absent.
 */
export interface CurrentCustody {
  consultantId: string
  consultantFullName: string
  workforceType?: WorkforceType | null
  engagementType?: EngagementType | null
  companyName?: string | null
  roleOrFunction?: string | null
  assignedAtUtc: string
  /** Absent for custody established by the Current Inventory import — there was no delivery behind it. */
  equipmentHandoverId?: string | null
  origin: EquipmentAssignmentOrigin
}

/** How the custody was established: a finalized delivery, or the initial Current Inventory import (pre-existing custody). */
export type EquipmentAssignmentOrigin = 'Handover' | 'CurrentInventoryImport'

/** Present only while the equipment has a currently-Pending retirement request — deliberately minimal, requester display name only. */
export interface EquipmentPendingRetirementRequest {
  id: string
  requestedByDisplayName: string
  requestedAtUtc: string
  requestReason: string
}

export interface EquipmentDetail {
  id: string
  equipmentTypeId: string
  equipmentTypeName: string
  assetTag: string
  manufacturer?: string | null
  model?: string | null
  serialNumber?: string | null
  notes?: string | null
  status: EquipmentStatus
  createdAtUtc: string
  updatedAtUtc: string
  laptopSpecification?: LaptopSpecification | null
  currentCustody?: CurrentCustody | null
  purchaseDate?: string | null
  invoiceNumber?: string | null
  purchaseVendor?: string | null
  pendingRetirementRequest?: EquipmentPendingRetirementRequest | null
  mobilePhoneSpecification?: MobilePhoneSpecification | null
  currentPhotoSet?: EquipmentPhotoSet | null
  /** Server-computed: no custody and no dispatched delivery, so "Alterar fotografias" is offered (the endpoint re-checks). */
  currentPhotosCorrectable?: boolean
}

export interface EquipmentListParams {
  search?: string
  status?: EquipmentStatus
  equipmentTypeId?: string
  manufacturer?: string
  page: number
  pageSize: number
}

export interface CreateEquipmentValues {
  equipmentTypeId: string
  manufacturer?: string
  model?: string
  serialNumber?: string
  notes?: string
  laptopSpecification?: LaptopSpecification
  mobilePhoneSpecification?: MobilePhoneSpecification
  /** Exactly 3 for Laptop/MobilePhone equipment types, empty otherwise — enforced server-side. */
  photos?: File[]
  purchaseDate?: string
  invoiceNumber?: string
  purchaseVendor?: string
}

export type UpdateEquipmentValues = Omit<CreateEquipmentValues, 'equipmentTypeId' | 'photos'>

export interface EquipmentStatusCount {
  status: EquipmentStatus
  count: number
}

export interface EquipmentTypeCount {
  equipmentTypeId: string
  equipmentTypeName: string
  count: number
}

export interface EquipmentStats {
  totalCount: number
  byStatus: EquipmentStatusCount[]
  availableByType: EquipmentTypeCount[]
}

export type EquipmentHistoryEventType =
  | 'Registered'
  | 'Assigned'
  | 'Returned'
  | 'ReturnInspectionCompleted'
  | 'DamageReported'
  | 'MaintenanceStarted'
  | 'MaintenanceCompleted'
  | 'RetirementRequested'
  | 'RetirementRequestRejected'
  | 'Retired'

/** One "Histórico do equipamento" timeline entry (Iteration 1). */
export interface EquipmentHistoryEvent {
  eventType: EquipmentHistoryEventType
  occurredAtUtc: string
  consultantId?: string | null
  consultantName?: string | null
  companyName?: string | null
  role?: string | null
  handoverId?: string | null
  returnId?: string | null
  maintenanceId?: string | null
  condition?: EquipmentCondition | null
  outcome?: MaintenanceOutcome | null
  description?: string | null
  retirementRequestId?: string | null
  requestedByName?: string | null
  /** Assigned/Returned events only. */
  assignmentOrigin?: EquipmentAssignmentOrigin | null
}
