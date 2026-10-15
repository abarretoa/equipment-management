import { apiFetch, apiFetchBlob } from '../../shared/api/httpClient'
import type { ResponsibilityTerm, ResponsibilityTermDelivery } from './types'

export function getResponsibilityTerm(id: string): Promise<ResponsibilityTerm> {
  return apiFetch(`/api/responsibility-terms/${id}`)
}

export function getResponsibilityTermPdf(id: string): Promise<Blob> {
  return apiFetchBlob(`/api/responsibility-terms/${id}/content`)
}

export function sendResponsibilityTerm(id: string, recipientEmail?: string): Promise<void> {
  return apiFetch(`/api/responsibility-terms/${id}/send`, { method: 'POST', body: JSON.stringify({ recipientEmail }) })
}

export function listDeliveries(id: string): Promise<ResponsibilityTermDelivery[]> {
  return apiFetch(`/api/responsibility-terms/${id}/deliveries`)
}

export function uploadSignedDocument(id: string, file: File, signedOn?: string): Promise<void> {
  const formData = new FormData()
  formData.append('file', file)
  if (signedOn) formData.append('signedOn', signedOn)
  return apiFetch(`/api/responsibility-terms/${id}/signed-document`, { method: 'POST', body: formData })
}

export function getSignedDocumentPdf(id: string): Promise<Blob> {
  return apiFetchBlob(`/api/responsibility-terms/${id}/signed-document/content`)
}
