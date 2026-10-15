export interface CurrentUser {
  id: string
  firstName: string
  lastName: string
  email: string
  role: 'Administration' | 'HR'
  permissions: string[]
  mustChangePassword: boolean
}
