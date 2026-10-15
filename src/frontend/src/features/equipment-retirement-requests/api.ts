import { apiFetch, buildQuery } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type {
  CreateEquipmentRetirementRequestValues,
  EquipmentRetirementRequest,
  EquipmentRetirementRequestListParams,
} from './types'

export function listEquipmentRetirementRequests(
  params: EquipmentRetirementRequestListParams,
): Promise<PagedResponse<EquipmentRetirementRequest>> {
  return apiFetch(`/api/equipment-retirement-requests${buildQuery(params)}`)
}

export function createEquipmentRetirementRequest(values: CreateEquipmentRetirementRequestValues): Promise<EquipmentRetirementRequest> {
  return apiFetch('/api/equipment-retirement-requests', { method: 'POST', body: JSON.stringify(values) })
}

export function approveEquipmentRetirementRequest(id: string): Promise<EquipmentRetirementRequest> {
  return apiFetch(`/api/equipment-retirement-requests/${id}/approve`, { method: 'POST' })
}

export function rejectEquipmentRetirementRequest(id: string, reason: string): Promise<EquipmentRetirementRequest> {
  return apiFetch(`/api/equipment-retirement-requests/${id}/reject`, { method: 'POST', body: JSON.stringify({ reason }) })
}
