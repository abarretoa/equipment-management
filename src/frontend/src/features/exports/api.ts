import { apiFetchBlob, buildQuery } from '../../shared/api/httpClient'

export interface ConsultantExportFilters {
  search?: string
  isActive?: boolean
  workforceType?: string
  companyId?: string
}

export interface EquipmentExportFilters {
  search?: string
  status?: string
  equipmentTypeId?: string
  manufacturer?: string
}

export function exportConsultants(filters: ConsultantExportFilters): Promise<Blob> {
  return apiFetchBlob(`/api/exports/consultants${buildQuery(filters)}`)
}

export function exportEquipment(filters: EquipmentExportFilters): Promise<Blob> {
  return apiFetchBlob(`/api/exports/equipment${buildQuery(filters)}`)
}
