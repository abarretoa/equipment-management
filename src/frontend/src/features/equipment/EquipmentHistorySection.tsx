import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import {
  Boxes,
  PackageCheck,
  Undo2,
  Search,
  AlertTriangle,
  Wrench,
  CheckCircle2,
  Archive,
  FileWarning,
  XCircle,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { DetailSection } from '../../shared/components/DetailSection'
import { Skeleton } from '../../shared/components/Skeleton'
import { ErrorState } from '../../shared/components/ErrorState'
import { equipmentConditionMeta, maintenanceOutcomeMeta } from '../../shared/status/statusMaps'
import { formatDate } from '../../shared/utils/format'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import { getEquipmentHistory } from './api'
import type { EquipmentHistoryEvent, EquipmentHistoryEventType } from './types'

const EVENT_ICONS: Record<EquipmentHistoryEventType, LucideIcon> = {
  Registered: Boxes,
  Assigned: PackageCheck,
  Returned: Undo2,
  ReturnInspectionCompleted: Search,
  DamageReported: AlertTriangle,
  MaintenanceStarted: Wrench,
  MaintenanceCompleted: CheckCircle2,
  RetirementRequested: FileWarning,
  RetirementRequestRejected: XCircle,
  Retired: Archive,
}

function titleFor(event: EquipmentHistoryEvent): string {
  switch (event.eventType) {
    case 'Registered':
      return 'Equipamento registado'
    case 'Assigned':
      return event.consultantName
        ? `Atribuído a ${event.consultantName}`
        : 'Atribuído a um consultor'
    case 'Returned':
      return event.consultantName ? `Devolvido por ${event.consultantName}` : 'Equipamento recebido'
    case 'ReturnInspectionCompleted':
      return 'Inspeção de devolução concluída'
    case 'DamageReported':
      return 'Avaria identificada'
    case 'MaintenanceStarted':
      return 'Enviado para manutenção'
    case 'MaintenanceCompleted':
      return 'Manutenção concluída'
    case 'RetirementRequested':
      return event.requestedByName
        ? `Abate solicitado por ${event.requestedByName}`
        : 'Abate solicitado'
    case 'RetirementRequestRejected':
      return 'Solicitação de abate rejeitada'
    case 'Retired':
      return 'Equipamento abatido'
  }
}

function secondaryFor(event: EquipmentHistoryEvent): string | null {
  switch (event.eventType) {
    case 'Assigned': {
      if (event.assignmentOrigin === 'CurrentInventoryImport')
        return 'Registado na importação do inventário atual'
      const parts = [event.companyName, event.role].filter(Boolean)
      return parts.length > 0 ? parts.join(' · ') : null
    }
    case 'ReturnInspectionCompleted':
      return event.condition
        ? `Estado: ${equipmentConditionMeta[event.condition]?.label ?? event.condition}`
        : null
    case 'DamageReported':
      return event.description ?? null
    case 'MaintenanceCompleted':
      return event.outcome
        ? `Resultado: ${maintenanceOutcomeMeta[event.outcome]?.label ?? event.outcome}`
        : null
    case 'RetirementRequested':
    case 'RetirementRequestRejected':
      return event.description ?? null
    default:
      return null
  }
}

/** "Histórico do equipamento" — a restrained operational timeline, not an audit log (Iteration 1). */
export function EquipmentHistorySection({ equipmentId }: { equipmentId: string }) {
  const { hasPermission } = useAuth()
  const canReadHandovers = hasPermission(Permissions.EquipmentHandover.Read)
  const canReadReturns = hasPermission(Permissions.EquipmentReturn.Read)

  const query = useQuery({
    queryKey: ['equipment', equipmentId, 'history'],
    queryFn: () => getEquipmentHistory(equipmentId),
  })

  return (
    <DetailSection variant="ruled" title="Histórico do equipamento">
      {query.isLoading ? (
        <Skeleton className="h-32 w-full" />
      ) : query.error || !query.data ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : query.data.length === 0 ? (
        <p className="text-[13px] text-fg-subtle">Sem histórico registado.</p>
      ) : (
        <ul className="flex flex-col">
          {query.data.map((event) => {
            const Icon = EVENT_ICONS[event.eventType]
            const secondary = secondaryFor(event)
            const linkTo =
              event.eventType === 'Assigned' && canReadHandovers && event.handoverId
                ? `/entregas/${event.handoverId}`
                : (event.eventType === 'ReturnInspectionCompleted' ||
                      event.eventType === 'DamageReported') &&
                    canReadReturns &&
                    event.returnId
                  ? `/devolucoes/${event.returnId}`
                  : null

            const key = `${event.eventType}-${event.occurredAtUtc}-${event.handoverId ?? event.returnId ?? event.maintenanceId ?? ''}`

            return (
              <li
                key={key}
                className="grid grid-cols-[28px_minmax(0,1fr)_auto] items-start gap-x-3 border-b border-border-row py-2.5 first:pt-0 last:border-b-0"
              >
                <span className="flex size-7 items-center justify-center rounded-md bg-surface-selected text-fg-muted">
                  <Icon className="size-3.5" aria-hidden />
                </span>
                <div className="min-w-0">
                  <p className="text-[13px] leading-[19px] font-medium break-words text-fg">
                    {linkTo ? (
                      <Link
                        to={linkTo}
                        className="underline decoration-border-strong underline-offset-2 hover:decoration-fg focus-visible:decoration-fg"
                      >
                        {titleFor(event)}
                      </Link>
                    ) : (
                      titleFor(event)
                    )}
                  </p>
                  {secondary && (
                    <p className="text-[12px] leading-[17px] break-words text-fg-muted">
                      {secondary}
                    </p>
                  )}
                </div>
                <time
                  dateTime={event.occurredAtUtc}
                  className="text-[12px] leading-[19px] text-fg-muted tabular-nums"
                >
                  {formatDate(event.occurredAtUtc)}
                </time>
              </li>
            )
          })}
        </ul>
      )}
    </DetailSection>
  )
}
