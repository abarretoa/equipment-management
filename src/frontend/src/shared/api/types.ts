export interface PagedResponse<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export interface IssueDto {
  code: string
  field: string
  message: string
  severity: 'Warning' | 'Error'
}
