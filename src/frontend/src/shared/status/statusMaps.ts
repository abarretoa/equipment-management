import {
  Archive,
  CircleCheck,
  CircleMinus,
  Clock,
  FileClock,
  FilePenLine,
  PackageOpen,
  ScanSearch,
  TriangleAlert,
  Truck,
  Undo2,
  UserRound,
  Wrench,
  type LucideIcon,
} from 'lucide-react'
import type { StatusTone } from '../components/StatusBadge'

export interface StatusMeta {
  label: string
  tone: StatusTone
  /** The status's own icon (Visual System v1). Optional — StatusBadge falls back to the tone's icon. */
  icon?: LucideIcon
}

function meta(label: string, tone: StatusTone, icon?: LucideIcon): StatusMeta {
  return icon ? { label, tone, icon } : { label, tone }
}

/** Single source of truth mapping every backend status string to its pt-PT label + tone (Phase 9 scope). */
export const equipmentStatusMeta: Record<string, StatusMeta> = {
  Available: meta('Disponível', 'success', CircleCheck),
  InTransit: meta('Em trânsito', 'info', Truck),
  Assigned: meta('Atribuído', 'primary', UserRound),
  ReturnPending: meta('Devolução pendente', 'warning', Clock),
  UnderInspection: meta('Em inspeção', 'info', ScanSearch),
  Damaged: meta('Avariado', 'danger', TriangleAlert),
  UnderMaintenance: meta('Em manutenção', 'warning', Wrench),
  Retired: meta('Abatido', 'neutral', Archive),
}

export const handoverStatusMeta: Record<string, StatusMeta> = {
  Draft: meta('Rascunho', 'neutral', FilePenLine),
  Finalized: meta('Finalizada', 'success', CircleCheck),
}

/** Client-facing completion status (Iteration 1) — use this, not `handoverStatusMeta`, for anything the user sees. */
export const handoverOperationalStatusMeta: Record<string, StatusMeta> = {
  Draft: meta('Rascunho', 'neutral', FilePenLine),
  AwaitingSignedTerm: meta('Aguardando termo assinado', 'warning', FileClock),
  Completed: meta('Finalizada', 'success', CircleCheck),
}

export const assignmentStatusMeta: Record<string, StatusMeta> = {
  Active: meta('Ativa', 'primary'),
  Ended: meta('Terminada', 'neutral'),
}

/** Origin of an equipment's current condition photo set. */
export const photoSetSourceMeta: Record<string, StatusMeta> = {
  InitialRegistration: meta('Registo inicial', 'neutral'),
  Return: meta('Devolução', 'info', Undo2),
}

export const returnStatusMeta: Record<string, StatusMeta> = {
  Pending: meta('Pendente', 'warning', Clock),
  Received: meta('Recebida', 'info', PackageOpen),
  Completed: meta('Concluída', 'success', CircleCheck),
  Cancelled: meta('Cancelada', 'neutral', CircleMinus),
}

export const damageStatusMeta: Record<string, StatusMeta> = {
  Open: meta('Aberta', 'danger'),
  Resolved: meta('Resolvida', 'success'),
}

export const maintenanceStatusMeta: Record<string, StatusMeta> = {
  Active: meta('Em curso', 'warning'),
  Completed: meta('Concluída', 'success'),
}

export const maintenanceOutcomeMeta: Record<string, StatusMeta> = {
  Repaired: meta('Reparado', 'success'),
  NotRepairable: meta('Sem reparação possível', 'danger'),
}

export const equipmentRetirementRequestStatusMeta: Record<string, StatusMeta> = {
  Pending: meta('Pendente', 'warning'),
  Approved: meta('Aprovado', 'success'),
  Rejected: meta('Rejeitado', 'neutral'),
}

export const responsibilityTermStatusMeta: Record<string, StatusMeta> = {
  Generated: meta('Atual', 'primary'),
  Superseded: meta('Substituído', 'neutral'),
}

export const executionStatusMeta: Record<string, StatusMeta> = {
  NotSent: meta('Não enviado', 'neutral'),
  SendPending: meta('Envio pendente', 'warning'),
  Sent: meta('Enviado', 'info'),
  Signed: meta('Assinado', 'success'),
}

export const allocationStatusMeta: Record<string, StatusMeta> = {
  Active: meta('Ativa', 'primary'),
  Ended: meta('Terminada', 'neutral'),
}

export const importSessionStatusMeta: Record<string, StatusMeta> = {
  Pending: meta('Pendente', 'warning'),
  Completed: meta('Concluída', 'success'),
  Cancelled: meta('Cancelada', 'neutral'),
}

export const deliveryStatusMeta: Record<string, StatusMeta> = {
  Attempting: meta('A processar', 'warning'),
  Sent: meta('Enviado', 'success'),
  Failed: meta('Falhou', 'danger'),
}

export function activeStatusMeta(isActive: boolean): StatusMeta {
  return isActive ? meta('Ativo', 'success') : meta('Inativo', 'neutral')
}

export const equipmentConditionMeta: Record<string, StatusMeta> = {
  Excellent: meta('Excelente', 'success'),
  Good: meta('Bom', 'success'),
  Fair: meta('Razoável', 'warning'),
  Poor: meta('Mau', 'danger'),
}

export const handoverDeliveryMethodLabels: Record<string, string> = {
  InPerson: 'Entrega presencial',
  Carrier: 'Envio por transportadora',
}

export const workforceTypeLabels: Record<string, string> = {
  Internal: 'Interno',
  External: 'Externo',
}

export const engagementTypeLabels: Record<string, string> = {
  ServiceProvider: 'Prestador de serviço',
  EmploymentContract: 'Contrato de trabalho',
}

export const roleLabels: Record<string, string> = {
  Administration: 'Administração',
  HR: 'RH',
}
