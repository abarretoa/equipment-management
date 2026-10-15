import { apiFetch, buildQuery } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type { AuditListParams, AuditLog } from './types'

export function listAuditLogs(params: AuditListParams): Promise<PagedResponse<AuditLog>> {
  return apiFetch(`/api/audit${buildQuery(params)}`)
}

export function getAuditLog(id: string): Promise<AuditLog> {
  return apiFetch(`/api/audit/${id}`)
}
