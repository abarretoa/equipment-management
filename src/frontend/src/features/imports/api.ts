import { apiFetch, apiFetchBlob } from '../../shared/api/httpClient'
import type {
  CurrentInventoryImportConfirmResponse,
  CurrentInventoryImportPreviewResponse,
  ConsultantImportRow,
  EquipmentImportRow,
  ImportConfirmResponse,
  ImportPreviewResponse,
} from './types'

export type ImportKind = 'consultants' | 'equipment'

export function getImportTemplate(kind: ImportKind): Promise<Blob> {
  return apiFetchBlob(`/api/imports/${kind}/template`)
}

export function previewImport(kind: 'consultants', file: File): Promise<ImportPreviewResponse<ConsultantImportRow>>
export function previewImport(kind: 'equipment', file: File): Promise<ImportPreviewResponse<EquipmentImportRow>>
export function previewImport(kind: ImportKind, file: File): Promise<ImportPreviewResponse<ConsultantImportRow | EquipmentImportRow>> {
  const formData = new FormData()
  formData.append('file', file)
  return apiFetch(`/api/imports/${kind}/preview`, { method: 'POST', body: formData })
}

export function getImportSession(kind: 'consultants', sessionId: string): Promise<ImportPreviewResponse<ConsultantImportRow>>
export function getImportSession(kind: 'equipment', sessionId: string): Promise<ImportPreviewResponse<EquipmentImportRow>>
export function getImportSession(kind: ImportKind, sessionId: string): Promise<ImportPreviewResponse<ConsultantImportRow | EquipmentImportRow>> {
  return apiFetch(`/api/imports/${kind}/${sessionId}`)
}

export function confirmImport(kind: ImportKind, sessionId: string): Promise<ImportConfirmResponse> {
  return apiFetch(`/api/imports/${kind}/${sessionId}/confirm`, { method: 'POST' })
}

export function cancelImport(kind: ImportKind, sessionId: string): Promise<void> {
  return apiFetch(`/api/imports/${kind}/${sessionId}/cancel`, { method: 'POST' })
}

const currentInventoryBase = '/api/imports/current-inventory'

/** Current Inventory import — its own endpoints and response shapes (planned create/reuse summary), alongside the two flows above. */
export const currentInventory = {
  getTemplate: (): Promise<Blob> => apiFetchBlob(`${currentInventoryBase}/template`),
  preview: (file: File): Promise<CurrentInventoryImportPreviewResponse> => {
    const formData = new FormData()
    formData.append('file', file)
    return apiFetch(`${currentInventoryBase}/preview`, { method: 'POST', body: formData })
  },
  getSession: (sessionId: string): Promise<CurrentInventoryImportPreviewResponse> =>
    apiFetch(`${currentInventoryBase}/${sessionId}`),
  confirm: (sessionId: string): Promise<CurrentInventoryImportConfirmResponse> =>
    apiFetch(`${currentInventoryBase}/${sessionId}/confirm`, { method: 'POST' }),
  cancel: (sessionId: string): Promise<void> => apiFetch(`${currentInventoryBase}/${sessionId}/cancel`, { method: 'POST' }),
}
