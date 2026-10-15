import { apiFetch, buildQuery } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type { Company, CompanyFormValues, CompanyListParams } from './types'

export function listCompanies(params: CompanyListParams): Promise<PagedResponse<Company>> {
  return apiFetch(`/api/companies${buildQuery(params)}`)
}

export function getCompany(id: string): Promise<Company> {
  return apiFetch(`/api/companies/${id}`)
}

export function createCompany(values: CompanyFormValues): Promise<Company> {
  return apiFetch('/api/companies', { method: 'POST', body: JSON.stringify(values) })
}

export function updateCompany(id: string, values: CompanyFormValues): Promise<Company> {
  return apiFetch(`/api/companies/${id}`, { method: 'PUT', body: JSON.stringify(values) })
}

export function setCompanyActive(id: string, active: boolean): Promise<void> {
  return apiFetch(`/api/companies/${id}/${active ? 'activate' : 'deactivate'}`, { method: 'POST' })
}
