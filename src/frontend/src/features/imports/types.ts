import type { IssueDto } from '../../shared/api/types'

export type ImportRowStatus = 'Valid' | 'Warning' | 'Error'
export type ImportSessionStatus = 'Pending' | 'Completed' | 'Cancelled'

export interface ConsultantImportRow {
  rowNumber: number
  status: ImportRowStatus
  fullName: string
  nif: string
  email: string
  address: string
  phone: string
  hasAllocation: boolean
  workforceType?: string | null
  engagementType?: string | null
  roleOrFunction?: string | null
  company?: string | null
  allocationStartDate?: string | null
  allocationEndDate?: string | null
  issues: IssueDto[]
}

export interface EquipmentImportRow {
  rowNumber: number
  status: ImportRowStatus
  equipmentType: string
  manufacturer?: string | null
  model?: string | null
  serialNumber?: string | null
  notes?: string | null
  processor?: string | null
  memory?: string | null
  storage?: string | null
  issues: IssueDto[]
}

export interface ImportPreviewResponse<TRow> {
  importSessionId: string
  fileName: string
  fileSha256: string
  status: ImportSessionStatus
  totalRows: number
  validRows: number
  warningRows: number
  errorRows: number
  createdAtUtc: string
  rows: TRow[]
}

export interface ImportConfirmResponse {
  importSessionId: string
  status: ImportSessionStatus
  completedAtUtc: string
  createdConsultants?: number
  createdAllocations?: number
  createdEquipment?: number
}

export type CurrentInventoryConsultantAction = 'None' | 'Create' | 'Reuse'
export type CurrentInventoryAllocationAction = 'None' | 'Create' | 'Reuse'

/** One Current Inventory row: one piece of equipment and, when `willBeAssigned`, the person who currently holds it. */
export interface CurrentInventoryImportRow {
  rowNumber: number
  status: ImportRowStatus
  equipmentType: string
  equipmentTypeKind?: 'Generic' | 'Laptop' | 'MobilePhone' | null
  manufacturer?: string | null
  model?: string | null
  serialNumber?: string | null
  notes?: string | null
  processor?: string | null
  memory?: string | null
  storage?: string | null
  imei?: string | null
  mobileNumber?: string | null
  simSerialNumber?: string | null
  willBeAssigned: boolean
  fullName?: string | null
  nif?: string | null
  email?: string | null
  phone?: string | null
  address?: string | null
  workforceType?: string | null
  engagementType?: string | null
  roleOrFunction?: string | null
  company?: string | null
  allocationStartDate?: string | null
  allocationEndDate?: string | null
  assignmentDate?: string | null
  consultantAction: CurrentInventoryConsultantAction
  allocationAction: CurrentInventoryAllocationAction
  issues: IssueDto[]
}

/** What confirming will do — consultant/allocation counts are per person (NIF); rows with errors are not counted. */
export interface CurrentInventoryImportSummary {
  newConsultants: number
  reusedConsultants: number
  newAllocations: number
  reusedAllocations: number
  equipmentToCreate: number
  equipmentToAssign: number
  equipmentToStayAvailable: number
  internalConsultants: number
  externalConsultants: number
}

export interface CurrentInventoryImportPreviewResponse extends ImportPreviewResponse<CurrentInventoryImportRow> {
  summary: CurrentInventoryImportSummary
}

export interface CurrentInventoryImportConfirmResponse {
  importSessionId: string
  status: ImportSessionStatus
  createdConsultants: number
  reusedConsultants: number
  createdAllocations: number
  reusedAllocations: number
  createdEquipment: number
  createdAssignments: number
  completedAtUtc: string
}
