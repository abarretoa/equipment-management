import { apiFetch, buildQuery } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type { CompleteMaintenanceValues, MaintenanceDetail, MaintenanceListParams, MaintenanceSummary } from './types'

export function listMaintenance(params: MaintenanceListParams): Promise<PagedResponse<MaintenanceSummary>> {
  return apiFetch(`/api/maintenance${buildQuery(params)}`)
}

export function getMaintenance(id: string): Promise<MaintenanceDetail> {
  return apiFetch(`/api/maintenance/${id}`)
}

export function completeMaintenance(id: string, values: CompleteMaintenanceValues): Promise<MaintenanceDetail> {
  return apiFetch(`/api/maintenance/${id}/complete`, { method: 'POST', body: JSON.stringify(values) })
}
