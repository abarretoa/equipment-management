import { apiFetch, apiFetchBlob, buildQuery } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type {
  HandoverDeliveryMethod,
  HandoverDetail,
  HandoverListParams,
  HandoverSummary,
  RecordInspectionValues,
  RegisterDispatchValues,
} from './types'
import type { ResponsibilityTerm } from '../responsibility-terms/types'

export function listHandovers(params: HandoverListParams): Promise<PagedResponse<HandoverSummary>> {
  return apiFetch(`/api/equipment-handovers${buildQuery(params)}`)
}

export function getHandover(id: string): Promise<HandoverDetail> {
  return apiFetch(`/api/equipment-handovers/${id}`)
}

export function createHandover(consultantId: string, deliveryMethod: HandoverDeliveryMethod, notes?: string): Promise<HandoverDetail> {
  return apiFetch('/api/equipment-handovers', { method: 'POST', body: JSON.stringify({ consultantId, deliveryMethod, notes }) })
}

/** Only while Draft and before "Registar envio": the method locks with the rest of the composition once dispatched. */
export function setHandoverDeliveryMethod(id: string, deliveryMethod: HandoverDeliveryMethod): Promise<HandoverDetail> {
  return apiFetch(`/api/equipment-handovers/${id}/delivery-method`, { method: 'PUT', body: JSON.stringify({ deliveryMethod }) })
}

export function updateHandoverHeader(id: string, deliveredOn?: string, notes?: string): Promise<HandoverDetail> {
  return apiFetch(`/api/equipment-handovers/${id}`, { method: 'PUT', body: JSON.stringify({ deliveredOn, notes }) })
}

export function addHandoverItem(id: string, equipmentId: string): Promise<HandoverDetail> {
  return apiFetch(`/api/equipment-handovers/${id}/items`, { method: 'POST', body: JSON.stringify({ equipmentId }) })
}

export function removeHandoverItem(id: string, itemId: string): Promise<void> {
  return apiFetch(`/api/equipment-handovers/${id}/items/${itemId}`, { method: 'DELETE' })
}

export function recordInspection(id: string, itemId: string, values: RecordInspectionValues): Promise<void> {
  return apiFetch(`/api/equipment-handovers/${id}/items/${itemId}/inspection`, {
    method: 'PUT',
    body: JSON.stringify(values),
  })
}

export function uploadInspectionPhoto(id: string, itemId: string, file: File): Promise<void> {
  const formData = new FormData()
  formData.append('file', file)
  return apiFetch(`/api/equipment-handovers/${id}/items/${itemId}/inspection/photos`, {
    method: 'POST',
    body: formData,
  })
}

export function deleteInspectionPhoto(id: string, itemId: string, photoId: string): Promise<void> {
  return apiFetch(`/api/equipment-handovers/${id}/items/${itemId}/inspection/photos/${photoId}`, { method: 'DELETE' })
}

export function getInspectionPhotoBlob(id: string, itemId: string, photoId: string): Promise<Blob> {
  return apiFetchBlob(`/api/equipment-handovers/${id}/items/${itemId}/inspection/photos/${photoId}/content`)
}

export function finalizeHandover(id: string): Promise<HandoverDetail> {
  return apiFetch(`/api/equipment-handovers/${id}/finalize`, { method: 'POST' })
}

/** "Registar envio" — only usable while the Handover is still a Draft, before finalization; registering it locks the item composition (see `HandoverDetail.dispatch`). */
export function registerDispatch(id: string, values: RegisterDispatchValues): Promise<HandoverDetail['dispatch']> {
  return apiFetch(`/api/equipment-handovers/${id}/dispatch`, { method: 'POST', body: JSON.stringify(values) })
}

/** "Anular envio": the reason is mandatory; the server re-checks every precondition. */
export function cancelDispatch(id: string, reason: string): Promise<void> {
  return apiFetch(`/api/equipment-handovers/${id}/dispatch/cancel`, { method: 'POST', body: JSON.stringify({ reason }) })
}

export function generateResponsibilityTerm(handoverId: string): Promise<ResponsibilityTerm> {
  return apiFetch(`/api/equipment-handovers/${handoverId}/responsibility-terms`, { method: 'POST' })
}

export function listResponsibilityTerms(handoverId: string): Promise<ResponsibilityTerm[]> {
  return apiFetch(`/api/equipment-handovers/${handoverId}/responsibility-terms`)
}

/** "Eliminar rascunho": only a Draft before its operational lock (dispatch/confirmation); the server re-checks eligibility. */
export function deleteHandoverDraft(id: string): Promise<void> {
  return apiFetch(`/api/equipment-handovers/${id}`, { method: 'DELETE' })
}
