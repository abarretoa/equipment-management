export type UserRole = 'Administration' | 'HR'

export interface UserSummary {
  id: string
  firstName: string
  lastName: string
  email: string
  phoneNumber: string | null
  address?: string | null
  role: UserRole
  isActive: boolean
  mustChangePassword: boolean
  concurrencyStamp: string
}

export interface UserListParams {
  search?: string
  role?: UserRole
  isActive?: boolean
  page: number
  pageSize: number
}

export interface CreateUserValues {
  firstName: string
  lastName: string
  email: string
  phone?: string | null
  role: UserRole
  address?: string
}

export interface UpdateUserValues {
  firstName: string
  lastName: string
  phone?: string | null
  address?: string
  concurrencyStamp?: string
}
