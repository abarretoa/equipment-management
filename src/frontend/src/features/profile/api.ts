import { apiFetch } from '../../shared/api/httpClient'
import type { Profile, UpdateProfileValues } from './types'

export const PROFILE_QUERY_KEY = ['profile']

export function getProfile(): Promise<Profile> {
  return apiFetch<Profile>('/api/profile')
}

export function updateProfile(values: UpdateProfileValues): Promise<Profile> {
  return apiFetch<Profile>('/api/profile', { method: 'PUT', body: JSON.stringify(values) })
}
