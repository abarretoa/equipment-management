import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { Plus, Undo2 } from 'lucide-react'
import { PageHeader } from '../../shared/components/PageHeader'
import { Button } from '../../shared/components/Button'
import { FilterBar } from '../../shared/components/FilterBar'
import { Select } from '../../shared/components/Select'
import { DataTable, type DataTableColumn } from '../../shared/components/DataTable'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { returnStatusMeta, type StatusMeta } from '../../shared/status/statusMaps'
import { formatDate } from '../../shared/utils/format'
import { Permissions } from '../../shared/permissions'
import { useAuth } from '../auth/AuthContext'
import * as api from './api'
import { NewReturnDialog } from './NewReturnDialog'
import { equipmentCountLabel, formatExpectedDate, returnGroupHref, returnGroupStatusMeta } from './returnGroupPresentation'
import type { ReturnListRow, ReturnStatus } from './types'

const PAGE_SIZE = 20
const STATUS_OPTIONS: ReturnStatus[] = ['Pending', 'Received', 'Completed', 'Cancelled']

/**
 * What one list row shows. A row is a return group — one initiation event,
 * shown as one collection however many items it has — or a single return.
 * A group of one is presented exactly like a single return, because that is
 * all it is; only a multi-item group reads as a collection.
 */
interface RowView {
  key: string
  href: string
  consultantFullName: string
  /** Set for a multi-item collection only. */
  collection?: { count: number; receivedCount: number; assetTags: string }
  assetTag?: string
  status: StatusMeta
  isEndOfEngagement: boolean
  expected: string
  receivedOn?: string | null
  createdAtUtc: string
}

function toRowView(row: ReturnListRow): RowView | null {
  if (row.group) {
    const group = row.group
    const base = {
      key: group.id,
      href: returnGroupHref(group),
      consultantFullName: group.consultantFullName,
      isEndOfEngagement: group.isEndOfEngagement,
      expected: formatExpectedDate(group.expectedFromDate, group.expectedToDate),
      createdAtUtc: group.createdAtUtc,
    }

    const [item] = group.items
    if (group.items.length === 1 && item) {
      const meta = returnStatusMeta[item.status]
      return { ...base, assetTag: item.assetTag, status: meta ?? { label: item.status, tone: 'neutral' }, receivedOn: item.receivedOn }
    }

    return {
      ...base,
      collection: { count: group.items.length, receivedCount: group.receivedCount, assetTags: group.items.map((i) => i.assetTag).join(', ') },
      status: returnGroupStatusMeta(group.collectionStatus, group.isEndOfEngagement),
    }
  }

  if (row.return) {
    const ret = row.return
    return {
      key: ret.id,
      href: `/devolucoes/${ret.id}`,
      consultantFullName: ret.consultantFullName,
      assetTag: ret.assetTag,
      status: returnStatusMeta[ret.status] ?? { label: ret.status, tone: 'neutral' },
      isEndOfEngagement: false,
      expected: formatExpectedDate(ret.expectedReturnDate),
      receivedOn: ret.receivedOn,
      createdAtUtc: ret.createdAtUtc,
    }
  }

  return null
}

export function ReturnsListPage() {
  const { hasPermission } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()
  const status = (searchParams.get('status') as ReturnStatus | null) ?? undefined
  const page = Number(searchParams.get('page') ?? '1')
  // Kept as local state, not URL-synced like status/page — syncing a per-keystroke
  // text field through useSearchParams raced the router's re-render and dropped
  // characters (Phase 9 refinement QA finding).
  const [search, setSearch] = useState('')
  const [newReturnOpen, setNewReturnOpen] = useState(false)

  const canStartReturn = hasPermission(Permissions.Equipment.Return)

  function updateParams(next: { status?: string; page?: number }) {
    const params = new URLSearchParams(searchParams)
    if (next.status !== undefined) {
      if (next.status) params.set('status', next.status)
      else params.delete('status')
      params.set('page', '1')
    }
    if (next.page !== undefined) params.set('page', String(next.page))
    setSearchParams(params)
  }

  function changeSearch(value: string) {
    setSearch(value)
    // A new search starts from page 1 — one replace (never a per-keystroke history
    // entry); after that, pagination through the matching results works as usual.
    if (searchParams.has('page')) {
      setSearchParams(
        (prev) => {
          const next = new URLSearchParams(prev)
          next.delete('page')
          return next
        },
        { replace: true },
      )
    }
  }

  const query = useQuery({
    queryKey: ['returns', 'overview', { status, search, page }],
    queryFn: () => api.listReturnOverview({ status, search: search || undefined, page, pageSize: PAGE_SIZE }),
  })

  const rows = (query.data?.items ?? []).map(toRowView).filter((row): row is RowView => row !== null)
  const hasActiveFilters = Boolean(status || search)

  function clearFilters() {
    setSearch('')
    updateParams({ status: '' })
  }

  const columns: DataTableColumn<RowView>[] = [
    { key: 'consultant', header: 'Consultor', render: (row) => <span className="font-medium">{row.consultantFullName}</span> },
    {
      key: 'equipment',
      header: 'Equipamento',
      render: (row) =>
        row.collection ? (
          <div className="flex min-w-0 flex-col">
            <span>{equipmentCountLabel(row.collection.count)}</span>
            <span className="truncate font-mono text-[12px] text-fg-muted">{row.collection.assetTags}</span>
          </div>
        ) : (
          <span className="font-mono text-[12px] font-medium whitespace-nowrap">{row.assetTag}</span>
        ),
    },
    {
      key: 'status',
      header: 'Estado',
      width: '200px',
      render: (row) => <StatusBadge label={row.status.label} tone={row.status.tone} icon={row.status.icon} />,
    },
    {
      key: 'expected',
      header: 'Data prevista',
      width: '220px',
      render: (row) =>
        row.expected === '—' ? (
          <span className="text-fg-subtle">—</span>
        ) : (
          <span className="tabular-nums">
            <span className="text-fg-muted">{row.isEndOfEngagement ? 'Recolha' : 'Devolução'}:</span> {row.expected}
          </span>
        ),
    },
    {
      key: 'received',
      header: 'Receção',
      width: '104px',
      render: (row) => (
        <span className="tabular-nums">
          {row.collection ? `${row.collection.receivedCount} de ${row.collection.count}` : formatDate(row.receivedOn)}
        </span>
      ),
    },
    {
      key: 'createdAtUtc',
      header: 'Iniciada em',
      width: '112px',
      align: 'right',
      priority: 'secondary',
      render: (row) => <span className="text-fg-muted tabular-nums">{formatDate(row.createdAtUtc)}</span>,
    },
  ]

  return (
    <div className="flex flex-col">
      <PageHeader
        title="Devoluções"
        description="Processos de devolução de equipamento e respetiva inspeção."
        actions={
          canStartReturn && (
            <Button
              variant="primary"
              size="sm"
              className="max-lg:h-11 max-lg:px-4"
              icon={<Plus className="size-4" aria-hidden />}
              onClick={() => setNewReturnOpen(true)}
            >
              Nova devolução
            </Button>
          )
        }
      />
      <div className="flex flex-col pb-6 sm:px-3">
        <FilterBar
          search={search}
          onSearchChange={changeSearch}
          searchPlaceholder="Pesquisar por consultor ou identificador…"
          hasActiveFilters={hasActiveFilters}
          onClear={clearFilters}
          summary={
            query.data && (
              <span>
                <b className="font-semibold text-fg tabular-nums">{query.data.totalCount}</b>{' '}
                {query.data.totalCount === 1 ? 'resultado' : 'resultados'}
              </span>
            )
          }
        >
          <Select
            aria-label="Filtrar por estado"
            className="w-full sm:w-48"
            value={status ?? ''}
            onChange={(e) => updateParams({ status: e.target.value })}
          >
            <option value="">Todos os estados</option>
            {STATUS_OPTIONS.map((s) => (
              <option key={s} value={s}>
                {returnStatusMeta[s]?.label ?? s}
              </option>
            ))}
          </Select>
        </FilterBar>
        <DataTable
          columns={columns}
          rows={rows}
          rowKey={(row) => row.key}
          isLoading={query.isLoading}
          error={query.error}
          onRetry={() => void query.refetch()}
          getRowHref={(row) => row.href}
          renderMobileRow={(row) => <ReturnMobileRow row={row} />}
          emptyIcon={<Undo2 aria-hidden />}
          emptyTitle="Nenhuma devolução encontrada"
          emptyDescription={
            hasActiveFilters
              ? 'Nenhuma devolução corresponde aos filtros aplicados. Ajuste ou limpe os filtros.'
              : 'Inicie uma devolução aqui, a partir de um equipamento atribuído, ou ao encerrar o vínculo de um consultor.'
          }
          emptyAction={
            hasActiveFilters ? (
              <Button variant="secondary" size="sm" onClick={clearFilters}>
                Limpar filtros
              </Button>
            ) : (
              canStartReturn && (
                <Button variant="secondary" size="sm" onClick={() => setNewReturnOpen(true)}>
                  Nova devolução
                </Button>
              )
            )
          }
          pagination={
            query.data
              ? { page, pageSize: PAGE_SIZE, totalCount: query.data.totalCount, onPageChange: (p) => updateParams({ page: p }) }
              : undefined
          }
        />
      </div>

      {canStartReturn && <NewReturnDialog open={newReturnOpen} onOpenChange={setNewReturnOpen} />}
    </div>
  )
}

/**
 * Below 768px: consultant + state, then the equipment (one asset tag, or the
 * collection's count and receipt progress), then the expected date — one link per row.
 */
function ReturnMobileRow({ row }: { row: RowView }) {
  const equipment = row.collection
    ? `${equipmentCountLabel(row.collection.count)} · ${row.collection.receivedCount} de ${row.collection.count} recebidos`
    : row.assetTag
  return (
    <span className="flex flex-col gap-0.5">
      <span className="flex items-center justify-between gap-3">
        <span className="min-w-0 truncate text-[14px] leading-5 font-medium text-fg">{row.consultantFullName}</span>
        <StatusBadge label={row.status.label} tone={row.status.tone} icon={row.status.icon} />
      </span>
      <span className={row.collection ? 'text-[13px] leading-[18px] text-fg-muted' : 'font-mono text-[12.5px] leading-[18px] text-fg'}>
        {equipment}
      </span>
      {row.expected !== '—' && (
        <span className="text-[13px] leading-[18px] text-fg-muted tabular-nums">
          {row.isEndOfEngagement ? 'Recolha' : 'Devolução'} prevista: {row.expected}
        </span>
      )}
    </span>
  )
}
