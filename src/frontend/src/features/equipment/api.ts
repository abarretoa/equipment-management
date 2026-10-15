import { apiFetch, apiFetchBlob, buildQuery } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type {
  CreateEquipmentValues,
  EquipmentDetail,
  EquipmentHistoryEvent,
  EquipmentListParams,
  EquipmentStats,
  EquipmentSummary,
  UpdateEquipmentValues,
} from './types'

/** Flat multipart fields — no nested Laptop/MobilePhone specification object — so this binds cleanly server-side alongside the photo files. */
function toCreateFormData(values: CreateEquipmentValues): FormData {
  const form = new FormData()
  form.set('EquipmentTypeId', values.equipmentTypeId)
  if (values.manufacturer) form.set('Manufacturer', values.manufacturer)
  if (values.model) form.set('Model', values.model)
  if (values.serialNumber) form.set('SerialNumber', values.serialNumber)
  if (values.notes) form.set('Notes', values.notes)
  if (values.laptopSpecification) {
    form.set('Processor', values.laptopSpecification.processor)
    form.set('Memory', values.laptopSpecification.memory)
    form.set('Storage', values.laptopSpecification.storage)
  }
  if (values.mobilePhoneSpecification) {
    form.set('Imei', values.mobilePhoneSpecification.imei)
    if (values.mobilePhoneSpecification.mobileNumber) form.set('MobileNumber', values.mobilePhoneSpecification.mobileNumber)
    if (values.mobilePhoneSpecification.simSerialNumber) form.set('SimSerialNumber', values.mobilePhoneSpecification.simSerialNumber)
  }
  if (values.purchaseDate) form.set('PurchaseDate', values.purchaseDate)
  if (values.invoiceNumber) form.set('InvoiceNumber', values.invoiceNumber)
  if (values.purchaseVendor) form.set('PurchaseVendor', values.purchaseVendor)
  for (const photo of values.photos ?? []) {
    form.append('Photos', photo)
  }
  return form
}

export function listEquipment(params: EquipmentListParams): Promise<PagedResponse<EquipmentSummary>> {
  return apiFetch(`/api/equipment${buildQuery(params)}`)
}

/** Single aggregate query backing the dashboard's inventory view and the Equipment list's status tabs (Phase 9 refinement). `equipmentTypeId` scopes the whole aggregate to that type (Iteration 1). */
export function getEquipmentStats(equipmentTypeId?: string): Promise<EquipmentStats> {
  return apiFetch(`/api/equipment/stats${buildQuery({ equipmentTypeId })}`)
}

export function getEquipment(id: string): Promise<EquipmentDetail> {
  return apiFetch(`/api/equipment/${id}`)
}

/** Newest-first "Histórico do equipamento" timeline (Iteration 1). */
export function getEquipmentHistory(id: string): Promise<EquipmentHistoryEvent[]> {
  return apiFetch(`/api/equipment/${id}/history`)
}

/** Multipart — Laptop/MobilePhone equipment types require exactly 3 `values.photos`; every other type requires none. */
export function createEquipment(values: CreateEquipmentValues): Promise<EquipmentDetail> {
  return apiFetch('/api/equipment', { method: 'POST', body: toCreateFormData(values) })
}

/** No photo field — ordinary Equipment Edit can never create/replace the current condition photo set. */
export function updateEquipment(id: string, values: UpdateEquipmentValues): Promise<EquipmentDetail> {
  return apiFetch(`/api/equipment/${id}`, { method: 'PUT', body: JSON.stringify(values) })
}

export function retireEquipment(id: string): Promise<void> {
  return apiFetch(`/api/equipment/${id}/retire`, { method: 'POST' })
}

/** The legacy backfill action — only usable when the equipment currently has no current photo set (409 otherwise). Exactly 3 photos required. */
export function establishEquipmentPhotos(id: string, photos: File[]): Promise<EquipmentDetail> {
  const form = new FormData()
  for (const photo of photos) form.append('Photos', photo)
  return apiFetch(`/api/equipment/${id}/photos`, { method: 'POST', body: form })
}

/**
 * "Alterar fotografias" (Administration only): replaces the slots given (1-3);
 * the server copies every other slot into one complete new current set of 3.
 * `expectedPhotoSetId` is the set on screen — a 409 means it changed.
 */
export function replaceCurrentEquipmentPhotos(
  id: string,
  expectedPhotoSetId: string,
  replacements: Partial<Record<1 | 2 | 3, File>>,
): Promise<EquipmentDetail> {
  const form = new FormData()
  form.append('ExpectedPhotoSetId', expectedPhotoSetId)
  for (const slot of [1, 2, 3] as const) {
    const file = replacements[slot]
    if (file) form.append(`Photo${slot}`, file)
  }
  return apiFetch(`/api/equipment/${id}/current-photos/replacement`, { method: 'POST', body: form })
}

export function getEquipmentPhotoBlob(equipmentId: string, photoId: string): Promise<Blob> {
  return apiFetchBlob(`/api/equipment/${equipmentId}/photos/${photoId}/content`)
}

export function startMaintenance(id: string, notes?: string): Promise<void> {
  return apiFetch(`/api/equipment/${id}/maintenance`, { method: 'POST', body: JSON.stringify({ notes }) })
}
