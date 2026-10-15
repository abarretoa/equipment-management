import { apiFetch, buildQuery } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type {
  CreateEquipmentTypeValues,
  EquipmentType,
  EquipmentTypeListParams,
  UpdateEquipmentTypeValues,
} from './types'

export function listEquipmentTypes(params: EquipmentTypeListParams): Promise<PagedResponse<EquipmentType>> {
  return apiFetch(`/api/equipment-types${buildQuery(params)}`)
}

export function getEquipmentType(id: string): Promise<EquipmentType> {
  return apiFetch(`/api/equipment-types/${id}`)
}

export function createEquipmentType(values: CreateEquipmentTypeValues): Promise<EquipmentType> {
  return apiFetch('/api/equipment-types', { method: 'POST', body: JSON.stringify(values) })
}

export function updateEquipmentType(id: string, values: UpdateEquipmentTypeValues): Promise<EquipmentType> {
  return apiFetch(`/api/equipment-types/${id}`, { method: 'PUT', body: JSON.stringify(values) })
}

export function setEquipmentTypeActive(id: string, active: boolean): Promise<void> {
  return apiFetch(`/api/equipment-types/${id}/${active ? 'activate' : 'deactivate'}`, { method: 'POST' })
}
