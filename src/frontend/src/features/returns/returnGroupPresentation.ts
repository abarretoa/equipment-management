import type { StatusTone } from '../../shared/components/StatusBadge'
import { formatDate } from '../../shared/utils/format'
import type { ReturnGroup, ReturnGroupCollectionStatus, ReturnGroupItem, ReturnStatus } from './types'

interface Meta {
  label: string
  tone: StatusTone
}

/**
 * An end of engagement is a collection ("recolha") arranged by the company; a manual
 * return is a "devolução". Same statuses, the domain's own word for each.
 */
const noun = (isEndOfEngagement: boolean) => (isEndOfEngagement ? 'recolha' : 'devolução')
const capitalize = (text: string) => text.charAt(0).toUpperCase() + text.slice(1)

export function returnGroupStatusMeta(status: ReturnGroupCollectionStatus, isEndOfEngagement: boolean): Meta {
  const word = noun(isEndOfEngagement)
  switch (status) {
    case 'Awaiting':
      return { label: `Aguardando ${word}`, tone: 'warning' }
    case 'Partial':
      return { label: `${capitalize(word)} parcial`, tone: 'info' }
    case 'Collected':
      return { label: `${capitalize(word)} concluída`, tone: 'success' }
    case 'Cancelled':
      return { label: 'Cancelada', tone: 'neutral' }
  }
}

/** One item's place in the collection — about physical receipt, the only thing the group is concerned with. */
export function returnGroupItemStatusMeta(status: ReturnStatus, isEndOfEngagement: boolean): Meta {
  switch (status) {
    case 'Pending':
      return { label: `Aguardando ${noun(isEndOfEngagement)}`, tone: 'warning' }
    case 'Received':
      return { label: 'Recebido · em inspeção', tone: 'info' }
    case 'Completed':
      return { label: 'Recebido · inspeção concluída', tone: 'success' }
    case 'Cancelled':
      return { label: 'Devolução cancelada', tone: 'neutral' }
  }
}

export function expectedDateLabel(isEndOfEngagement: boolean): string {
  return isEndOfEngagement ? 'Recolha prevista' : 'Devolução prevista'
}

/** A single date, or the collection window when the group has one. */
export function formatExpectedDate(from?: string | null, to?: string | null): string {
  if (!from) return '—'
  return to && to !== from ? `${formatDate(from)} a ${formatDate(to)}` : formatDate(from)
}

export function equipmentCountLabel(count: number): string {
  return count === 1 ? '1 equipamento' : `${count} equipamentos`
}

export function equipmentDescriptor(item: Pick<ReturnGroupItem, 'manufacturer' | 'model' | 'equipmentTypeName'>): string {
  return [item.manufacturer, item.model].filter(Boolean).join(' ') || item.equipmentTypeName
}

/** A group opens as a collection only when it has several items; a one-item group is just that return. */
export function returnGroupHref(group: ReturnGroup): string {
  const [only] = group.items
  return group.items.length === 1 && only ? `/devolucoes/${only.returnId}` : `/devolucoes/grupos/${group.id}`
}
