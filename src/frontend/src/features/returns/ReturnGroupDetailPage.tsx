import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { DetailSection } from '../../shared/components/DetailSection'
import { KeyValueList } from '../../shared/components/KeyValueList'
import { DataTable, type DataTableColumn } from '../../shared/components/DataTable'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { Skeleton } from '../../shared/components/Skeleton'
import { ErrorState } from '../../shared/components/ErrorState'
import { formatDate, formatDateTime } from '../../shared/utils/format'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import * as api from './api'
import {
  equipmentCountLabel,
  equipmentDescriptor,
  expectedDateLabel,
  formatExpectedDate,
  returnGroupItemStatusMeta,
  returnGroupStatusMeta,
} from './returnGroupPresentation'
import type { ReturnGroupItem, ReturnSummary } from './types'

const textLinkClass = 'text-fg underline decoration-border-strong underline-offset-2 hover:decoration-fg'

/**
 * One return-initiation event shown as one collection. It is a view over
 * independent returns: there is deliberately no group-level action here —
 * each item is received, inspected and finalized on its own page.
 */
export function ReturnGroupDetailPage() {
  const { groupId } = useParams<{ groupId: string }>()
  const { hasPermission } = useAuth()

  const query = useQuery({
    queryKey: ['returns', 'group', groupId],
    queryFn: () => api.getReturnGroup(groupId!),
    enabled: Boolean(groupId),
  })

  if (query.isLoading) {
    return (
      <div className="flex flex-col gap-3 px-4 py-6 sm:px-6" aria-busy="true">
        <Skeleton className="h-7 w-64" />
        <Skeleton className="h-80 w-full" />
      </div>
    )
  }

  if (query.error || !query.data) {
    return (
      <div className="px-4 py-6 sm:px-6">
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </div>
    )
  }

  const { group, otherPendingReturns } = query.data
  const statusMeta = returnGroupStatusMeta(group.collectionStatus, group.isEndOfEngagement)
  const canReadConsultants = hasPermission(Permissions.Consultant.Read)

  const itemColumns: DataTableColumn<ReturnGroupItem>[] = [
    {
      key: 'assetTag',
      header: 'Identificador',
      width: '136px',
      render: (row) => <span className="font-mono text-[12px] font-medium whitespace-nowrap text-fg">{row.assetTag}</span>,
    },
    { key: 'type', header: 'Tipo', render: (row) => <span className="text-fg-muted">{row.equipmentTypeName}</span> },
    { key: 'equipment', header: 'Equipamento', render: (row) => equipmentDescriptor(row) },
    {
      key: 'status',
      header: 'Estado',
      width: '240px',
      render: (row) => {
        const meta = returnGroupItemStatusMeta(row.status, group.isEndOfEngagement)
        return <StatusBadge label={meta.label} tone={meta.tone} />
      },
    },
    {
      key: 'receivedOn',
      header: 'Recebido em',
      width: '112px',
      render: (row) => <span className="tabular-nums">{formatDate(row.receivedOn)}</span>,
    },
    {
      key: 'open',
      header: '',
      width: '150px',
      align: 'right',
      render: (row) => (
        <Link to={`/devolucoes/${row.returnId}`} className={`text-[13px] whitespace-nowrap ${textLinkClass}`}>
          Abrir devolução
        </Link>
      ),
    },
  ]

  const otherColumns: DataTableColumn<ReturnSummary>[] = [
    {
      key: 'assetTag',
      header: 'Identificador',
      width: '136px',
      render: (row) => <span className="font-mono text-[12px] font-medium whitespace-nowrap text-fg">{row.assetTag}</span>,
    },
    { key: 'reason', header: 'Motivo', render: (row) => row.reason ?? <span className="text-fg-subtle">—</span> },
    { key: 'expected', header: 'Data prevista', width: '128px', render: (row) => <span className="tabular-nums">{formatDate(row.expectedReturnDate)}</span> },
    {
      key: 'created',
      header: 'Iniciada em',
      width: '112px',
      align: 'right',
      render: (row) => <span className="text-fg-muted tabular-nums">{formatDate(row.createdAtUtc)}</span>,
    },
  ]

  return (
    <div className="flex flex-col">
      <header className="flex flex-col gap-2 border-b border-border bg-surface px-4 py-5 sm:px-6">
        <nav aria-label="Localização">
          <ol className="flex flex-wrap items-center gap-1.5 text-[12px] text-fg-muted">
            <li>
              <Link to="/devolucoes" className="hover:text-fg hover:underline">
                Devoluções
              </Link>
            </li>
            <li aria-hidden className="text-border-control">
              /
            </li>
            <li className="min-w-0 truncate text-fg" aria-current="page">
              {group.consultantFullName}
            </li>
          </ol>
        </nav>
        <div className="flex flex-wrap items-center gap-x-3.5 gap-y-1">
          <h1 className="text-xl leading-7 font-semibold tracking-[-0.01em] break-words text-fg">{group.consultantFullName}</h1>
          <StatusBadge label={statusMeta.label} tone={statusMeta.tone} />
        </div>
        <p className="text-[13px] leading-5 text-fg-muted">
          {group.isEndOfEngagement ? 'Recolha de equipamento por fim de vínculo' : 'Devolução de equipamento'}
        </p>
      </header>

      <div className="flex flex-col px-4 pb-6 sm:px-6">
        <DetailSection variant="ruled" title="Detalhes">
          <KeyValueList
            columns={3}
            items={[
              {
                label: 'Consultor',
                value: canReadConsultants ? (
                  <Link to={`/consultores/${group.consultantId}`} className={textLinkClass}>
                    {group.consultantFullName}
                  </Link>
                ) : (
                  group.consultantFullName
                ),
              },
              {
                label: expectedDateLabel(group.isEndOfEngagement),
                value: formatExpectedDate(group.expectedFromDate, group.expectedToDate),
              },
              {
                label: 'Equipamentos',
                value: `${equipmentCountLabel(group.items.length)} · ${group.receivedCount} de ${group.items.length} recebidos`,
              },
              { label: 'Iniciada por', value: group.createdByDisplayNameSnapshot },
              { label: 'Iniciada em', value: formatDateTime(group.createdAtUtc) },
            ]}
          />
        </DetailSection>

        <DetailSection
          variant="ruled"
          title="Equipamentos"
          description="Cada equipamento tem a sua própria devolução: é recebido e inspecionado individualmente."
        >
          <div className="-mx-4 md:mx-0">
            <DataTable
              columns={itemColumns}
              rows={group.items}
              rowKey={(row) => row.returnId}
              getRowHref={(row) => `/devolucoes/${row.returnId}`}
              renderMobileRow={(row) => <GroupItemMobileRow row={row} isEndOfEngagement={group.isEndOfEngagement} />}
              emptyTitle="Sem equipamentos."
            />
          </div>
        </DetailSection>

        {otherPendingReturns.length > 0 && (
          <DetailSection
            variant="ruled"
            title="Outras devoluções pendentes deste consultor"
            description="Iniciadas separadamente. Não fazem parte deste conjunto, mas o equipamento também está por receber."
          >
            <div className="-mx-4 md:mx-0">
              <DataTable
                columns={otherColumns}
                rows={otherPendingReturns}
                rowKey={(row) => row.id}
                getRowHref={(row) => `/devolucoes/${row.id}`}
                renderMobileRow={(row) => (
                  <span className="flex flex-col gap-0.5">
                    <span className="font-mono text-[12.5px] leading-5 font-medium text-fg">{row.assetTag}</span>
                    <span className="text-[13px] leading-[18px] text-fg-muted tabular-nums">
                      Prevista: {formatDate(row.expectedReturnDate)} · Iniciada {formatDate(row.createdAtUtc)}
                    </span>
                  </span>
                )}
                emptyTitle="Sem outras devoluções pendentes."
              />
            </div>
          </DetailSection>
        )}
      </div>
    </div>
  )
}

/** Below 768px: asset tag + the item's receipt state, then what it is and when it was received — one link per row. */
function GroupItemMobileRow({ row, isEndOfEngagement }: { row: ReturnGroupItem; isEndOfEngagement: boolean }) {
  const meta = returnGroupItemStatusMeta(row.status, isEndOfEngagement)
  return (
    <span className="flex flex-col gap-0.5">
      <span className="flex items-center justify-between gap-3">
        <span className="font-mono text-[12.5px] leading-5 font-medium text-fg">{row.assetTag}</span>
        <StatusBadge label={meta.label} tone={meta.tone} />
      </span>
      <span className="truncate text-[13px] leading-[18px] text-fg-muted">
        {row.equipmentTypeName} · {equipmentDescriptor(row)}
      </span>
      {row.receivedOn && (
        <span className="text-[13px] leading-[18px] text-fg-muted tabular-nums">Recebido em {formatDate(row.receivedOn)}</span>
      )}
    </span>
  )
}
