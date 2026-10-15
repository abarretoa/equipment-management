import { apiFetch, buildQuery } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type { AssignmentListParams, EquipmentAssignment, InitiateReturnValues } from './types'
import type { EquipmentReturnDetail } from '../returns/types'

export function listAssignments(params: AssignmentListParams): Promise<PagedResponse<EquipmentAssignment>> {
  return apiFetch(`/api/equipment-assignments${buildQuery(params)}`)
}

export function initiateReturn(assignmentId: string, values: InitiateReturnValues): Promise<EquipmentReturnDetail> {
  return apiFetch(`/api/equipment-assignments/${assignmentId}/return`, { method: 'POST', body: JSON.stringify(values) })
}
