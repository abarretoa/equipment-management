export type MaintenanceStatus = 'Active' | 'Completed'
export type MaintenanceOutcome = 'Repaired' | 'NotRepairable'

export interface MaintenanceSummary {
  id: string
  equipmentId: string
  assetTag: string
  equipmentTypeName: string
  manufacturer?: string | null
  model?: string | null
  status: MaintenanceStatus
  sentAtUtc: string
  completedAtUtc?: string | null
}

export interface MaintenanceDetail {
  id: string
  equipmentId: string
  assetTag: string
  equipmentTypeName: string
  damageId?: string | null
  status: MaintenanceStatus
  notes?: string | null
  outcome?: MaintenanceOutcome | null
  sentAtUtc: string
  completedAtUtc?: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export interface MaintenanceListParams {
  equipmentId?: string
  status?: MaintenanceStatus
  page: number
  pageSize: number
}

export interface CompleteMaintenanceValues {
  outcome: MaintenanceOutcome
  notes?: string
}
