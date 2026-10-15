import { apiFetch, buildQuery } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type {
  ConsultantAllocation,
  ConsultantDetail,
  ConsultantFormValues,
  ConsultantListParams,
  ConsultantSummary,
  CreateAllocationValues,
  EndAllocationResult,
  EndAllocationValues,
} from './types'

export function listConsultants(params: ConsultantListParams): Promise<PagedResponse<ConsultantSummary>> {
  return apiFetch(`/api/consultants${buildQuery(params)}`)
}

export function getConsultant(id: string): Promise<ConsultantDetail> {
  return apiFetch(`/api/consultants/${id}`)
}

export function createConsultant(values: ConsultantFormValues): Promise<ConsultantDetail> {
  return apiFetch('/api/consultants', { method: 'POST', body: JSON.stringify(values) })
}

export function updateConsultant(id: string, values: ConsultantFormValues): Promise<ConsultantDetail> {
  return apiFetch(`/api/consultants/${id}`, { method: 'PUT', body: JSON.stringify(values) })
}

export function setConsultantActive(id: string, active: boolean): Promise<void> {
  return apiFetch(`/api/consultants/${id}/${active ? 'activate' : 'deactivate'}`, { method: 'POST' })
}

export function listAllocations(consultantId: string, page: number, pageSize: number): Promise<PagedResponse<ConsultantAllocation>> {
  return apiFetch(`/api/consultants/${consultantId}/allocations${buildQuery({ page, pageSize })}`)
}

export function createAllocation(consultantId: string, values: CreateAllocationValues): Promise<ConsultantAllocation> {
  return apiFetch(`/api/consultants/${consultantId}/allocations`, { method: 'POST', body: JSON.stringify(values) })
}

export function updateAllocationRole(consultantId: string, allocationId: string, roleOrFunction: string): Promise<ConsultantAllocation> {
  return apiFetch(`/api/consultants/${consultantId}/allocations/${allocationId}`, {
    method: 'PUT',
    body: JSON.stringify({ roleOrFunction }),
  })
}

export function endAllocation(consultantId: string, allocationId: string, values: EndAllocationValues): Promise<EndAllocationResult> {
  return apiFetch(`/api/consultants/${consultantId}/allocations/${allocationId}/end`, {
    method: 'POST',
    body: JSON.stringify(values),
  })
}
