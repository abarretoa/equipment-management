export interface Company {
  id: string
  name: string
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CompanyListParams {
  search?: string
  isActive?: boolean
  page: number
  pageSize: number
}

export interface CompanyFormValues {
  name: string
}
