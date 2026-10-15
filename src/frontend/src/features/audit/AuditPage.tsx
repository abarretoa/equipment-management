import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ScrollText } from 'lucide-react'
import { PageHeader } from '../../shared/components/PageHeader'
import { FilterBar } from '../../shared/components/FilterBar'
import { Select } from '../../shared/components/Select'
import { DateField } from '../../shared/components/DateField'
import { Button } from '../../shared/components/Button'
import { DataTable, type DataTableColumn } from '../../shared/components/DataTable'
import { formatDateTime } from '../../shared/utils/format'
import * as api from './api'
import type { AuditLog } from './types'
import { AuditDetailDrawer } from './AuditDetailDrawer'
import { endOfLocalDayAsUtc, startOfLocalDayAsUtc } from './auditDateRange'
import {
  auditActionGroups,
  auditActionLabel,
  auditActionsIn,
  auditEntityTypeLabel,
  auditEntityTypeLabels,
} from './auditPresentation'

const PAGE_SIZE = 50

export function AuditPage() {
  // Filter values are the raw API codes (the backend matches them exactly);
  // only their labels are translated.
  const [action, setAction] = useState('')
  const [entityType, setEntityType] = useState('')
  // Calendar days as picked (YYYY-MM-DD); sent as UTC instants, see auditDateRange.
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<AuditLog | null>(null)

  const query = useQuery({
    queryKey: ['audit', { action, entityType, fromDate, toDate, page }],
    queryFn: () =>
      api.listAuditLogs({
        action: action || undefined,
        entityType: entityType || undefined,
        fromUtc: startOfLocalDayAsUtc(fromDate),
        toUtc: endOfLocalDayAsUtc(toDate),
        page,
        pageSize: PAGE_SIZE,
      }),
  })

  const hasActiveFilters = Boolean(action || entityType || fromDate || toDate)

  function clearFilters() {
    setAction('')
    setEntityType('')
    setFromDate('')
    setToDate('')
    setPage(1)
  }

  const columns: DataTableColumn<AuditLog>[] = [
    {
      key: 'occurredAtUtc',
      header: 'Data/hora',
      width: '148px',
      render: (row) => <span className="whitespace-nowrap tabular-nums">{formatDateTime(row.occurredAtUtc)}</span>,
    },
    {
      key: 'action',
      header: 'Ação',
      render: (row) => (
        // The record has no page of its own: this button opens its detail.
        <button
          type="button"
          onClick={() => setSelected(row)}
          className="text-left font-medium text-fg underline-offset-2 hover:underline focus-visible:outline-offset-2"
        >
          {auditActionLabel(row.action)}
        </button>
      ),
    },
    { key: 'actor', header: 'Autor', width: '180px', render: (row) => row.actorDisplayNameSnapshot ?? <span className="text-fg-muted">Sistema</span> },
    {
      key: 'entity',
      header: 'Entidade',
      width: '220px',
      render: (row) => (
        <span className="flex flex-col">
          <span>{auditEntityTypeLabel(row.entityType)}</span>
          {row.entityId && (
            <span className="font-mono text-[11.5px] text-fg-muted" title={row.entityId}>
              {row.entityId.slice(0, 8)}…
            </span>
          )}
        </span>
      ),
    },
    {
      key: 'ip',
      header: 'Endereço IP',
      width: '132px',
      render: (row) => (row.ipAddress ? <span className="font-mono text-[12px]">{row.ipAddress}</span> : <span className="text-fg-subtle">—</span>),
    },
  ]

  return (
    <div className="flex flex-col">
      <PageHeader title="Auditoria" description="Histórico de ações críticas realizadas na aplicação." />
      <div className="flex flex-col pb-6 sm:px-3">
        <FilterBar
          hasActiveFilters={hasActiveFilters}
          onClear={clearFilters}
          summary={
            query.data && (
              <span>
                <b className="font-semibold text-fg tabular-nums">{query.data.totalCount}</b>{' '}
                {query.data.totalCount === 1 ? 'registo' : 'registos'}
              </span>
            )
          }
        >
          <Select
            aria-label="Filtrar por ação"
            className="w-full sm:w-64"
            value={action}
            onChange={(e) => { setAction(e.target.value); setPage(1) }}
          >
            <option value="">Todas as ações</option>
            {auditActionGroups.map((group) => (
              <optgroup key={group.label} label={group.label}>
                {auditActionsIn(group.prefixes).map((code) => (
                  <option key={code} value={code}>
                    {auditActionLabel(code)}
                  </option>
                ))}
              </optgroup>
            ))}
          </Select>
          <Select
            aria-label="Filtrar por entidade"
            className="w-full sm:w-52"
            value={entityType}
            onChange={(e) => { setEntityType(e.target.value); setPage(1) }}
          >
            <option value="">Todas as entidades</option>
            {Object.entries(auditEntityTypeLabels).map(([code, label]) => (
              <option key={code} value={code}>
                {label}
              </option>
            ))}
          </Select>
          <span className="flex w-full items-center gap-2 sm:w-auto">
            <DateField
              aria-label="Data inicial"
              className="min-w-0 flex-1 sm:w-40 sm:flex-none"
              value={fromDate}
              onChange={(e) => { setFromDate(e.target.value); setPage(1) }}
            />
            <span className="text-fg-muted" aria-hidden>
              –
            </span>
            <DateField
              aria-label="Data final"
              className="min-w-0 flex-1 sm:w-40 sm:flex-none"
              value={toDate}
              onChange={(e) => { setToDate(e.target.value); setPage(1) }}
            />
          </span>
        </FilterBar>
        <DataTable
          columns={columns}
          rows={query.data?.items ?? []}
          rowKey={(row) => row.id}
          isLoading={query.isLoading}
          error={query.error}
          onRetry={() => void query.refetch()}
          onRowClick={setSelected}
          minTableWidth="840px"
          scrollLabel="Registos de auditoria"
          emptyIcon={<ScrollText aria-hidden />}
          emptyTitle="Nenhum registo de auditoria encontrado"
          emptyDescription={hasActiveFilters ? 'Ajuste os filtros para ver outros períodos ou ações.' : undefined}
          emptyAction={
            hasActiveFilters ? (
              <Button variant="secondary" size="sm" onClick={clearFilters}>
                Limpar filtros
              </Button>
            ) : undefined
          }
          pagination={query.data ? { page, pageSize: PAGE_SIZE, totalCount: query.data.totalCount, onPageChange: setPage } : undefined}
        />
      </div>

      <AuditDetailDrawer entry={selected} onClose={() => setSelected(null)} />
    </div>
  )
}
