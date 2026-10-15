import type { UserRole } from '../users/types'

/** GET/PUT /api/profile response — the signed-in user's own profile (ADR-073). */
export interface Profile {
  firstName: string
  lastName: string
  email: string
  phoneNumber: string | null
  address: string | null
  role: UserRole
}

/** PUT /api/profile body — only the four self-editable fields; there is no user id. */
export interface UpdateProfileValues {
  firstName: string
  lastName: string
  phone: string | null
  address: string | null
}
