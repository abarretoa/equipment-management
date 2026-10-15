import { apiFetch, apiFetchBlob, buildQuery } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type {
  EquipmentReturnDetail,
  ReceiveReturnValues,
  ReturnGroupDetail,
  ReturnListParams,
  ReturnListRow,
  ReturnOverviewParams,
  ReturnSummary,
  StartReturnGroupResult,
  StartReturnGroupValues,
  UpdateReturnInspectionValues,
} from './types'

export function listReturns(params: ReturnListParams): Promise<PagedResponse<ReturnSummary>> {
  return apiFetch(`/api/equipment-returns${buildQuery(params)}`)
}

/** The operational list: one row per return group, plus one per return that has no group. */
export function listReturnOverview(params: ReturnOverviewParams): Promise<PagedResponse<ReturnListRow>> {
  return apiFetch(`/api/equipment-returns/overview${buildQuery(params)}`)
}

export function getReturnGroup(groupId: string): Promise<ReturnGroupDetail> {
  return apiFetch(`/api/equipment-returns/groups/${groupId}`)
}

/** Starts a manual return for one or more items held by one consultant. All-or-nothing; never touches the allocation. */
export function startReturnGroup(values: StartReturnGroupValues): Promise<StartReturnGroupResult> {
  return apiFetch('/api/equipment-returns/groups', { method: 'POST', body: JSON.stringify(values) })
}

export function getReturn(id: string): Promise<EquipmentReturnDetail> {
  return apiFetch(`/api/equipment-returns/${id}`)
}

/** Multipart — see `ReceiveReturnValues.photos`'s remarks. */
export function receiveReturn(id: string, values: ReceiveReturnValues): Promise<EquipmentReturnDetail> {
  const form = new FormData()
  form.set('ReceivedOn', values.receivedOn)
  form.set('ReceivedByUserId', values.receivedByUserId)
  for (const photo of values.photos ?? []) {
    form.append('Photos', photo)
  }
  return apiFetch(`/api/equipment-returns/${id}/receive`, { method: 'POST', body: form })
}

export function cancelReturn(id: string, reason?: string): Promise<void> {
  return apiFetch(`/api/equipment-returns/${id}/cancel`, { method: 'POST', body: JSON.stringify({ reason }) })
}

export function updateReturnInspection(id: string, values: UpdateReturnInspectionValues): Promise<void> {
  return apiFetch(`/api/equipment-returns/${id}/inspection`, { method: 'PUT', body: JSON.stringify(values) })
}

export function uploadReturnPhoto(id: string, file: File): Promise<void> {
  const formData = new FormData()
  formData.append('file', file)
  return apiFetch(`/api/equipment-returns/${id}/inspection/photos`, { method: 'POST', body: formData })
}

export function deleteReturnPhoto(id: string, photoId: string): Promise<void> {
  return apiFetch(`/api/equipment-returns/${id}/inspection/photos/${photoId}`, { method: 'DELETE' })
}

export function getReturnPhotoBlob(id: string, photoId: string): Promise<Blob> {
  return apiFetchBlob(`/api/equipment-returns/${id}/inspection/photos/${photoId}/content`)
}

export function uploadDamagePhoto(id: string, file: File): Promise<void> {
  const formData = new FormData()
  formData.append('file', file)
  return apiFetch(`/api/equipment-returns/${id}/damage/photos`, { method: 'POST', body: formData })
}

export function deleteDamagePhoto(id: string, photoId: string): Promise<void> {
  return apiFetch(`/api/equipment-returns/${id}/damage/photos/${photoId}`, { method: 'DELETE' })
}

export function getDamagePhotoBlob(id: string, photoId: string): Promise<Blob> {
  return apiFetchBlob(`/api/equipment-returns/${id}/damage/photos/${photoId}/content`)
}

export function finalizeReturnInspection(id: string): Promise<EquipmentReturnDetail> {
  return apiFetch(`/api/equipment-returns/${id}/inspection/finalize`, { method: 'POST' })
}
