import { apiFetch, buildQuery } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type { CreateUserValues, UpdateUserValues, UserListParams, UserRole, UserSummary } from './types'

export function listUsers(params: UserListParams): Promise<PagedResponse<UserSummary>> {
  return apiFetch(`/api/users${buildQuery(params)}`)
}

export function getUser(id: string): Promise<UserSummary> {
  return apiFetch(`/api/users/${id}`)
}

export function createUser(values: CreateUserValues): Promise<{ user: UserSummary; temporaryPassword: string }> {
  return apiFetch('/api/users', { method: 'POST', body: JSON.stringify(values) })
}

export function updateUser(id: string, values: UpdateUserValues): Promise<UserSummary> {
  return apiFetch(`/api/users/${id}`, { method: 'PUT', body: JSON.stringify(values) })
}

export function changeUserRole(id: string, role: UserRole, concurrencyStamp?: string): Promise<UserSummary> {
  return apiFetch(`/api/users/${id}/role`, { method: 'PUT', body: JSON.stringify({ role, concurrencyStamp }) })
}

export function setUserActive(id: string, active: boolean, concurrencyStamp?: string): Promise<void> {
  return apiFetch(`/api/users/${id}/${active ? 'activate' : 'deactivate'}`, {
    method: 'POST',
    body: JSON.stringify({ concurrencyStamp }),
  })
}

export function resetUserPassword(id: string): Promise<{ temporaryPassword: string }> {
  return apiFetch(`/api/users/${id}/reset-password`, { method: 'POST' })
}
