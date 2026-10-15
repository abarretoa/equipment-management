import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { PackageCheck, Plus } from 'lucide-react'
import { PageHeader } from '../../shared/components/PageHeader'
import { FilterBar } from '../../shared/components/FilterBar'
import { Select } from '../../shared/components/Select'
import { DataTable, type DataTableColumn } from '../../shared/components/DataTable'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { Button } from '../../shared/components/Button'
import { handoverOperationalStatusMeta } from '../../shared/status/statusMaps'
import { formatDate } from '../../shared/utils/format'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import * as api from './api'
import type { HandoverStatus, HandoverSummary } from './types'

const PAGE_SIZE = 20
const STATUSES: HandoverStatus[] = ['Draft', 'Finalized']
/**
 * The filter sends the lifecycle `status` (Draft | Finalized). "Finalized"
 * matches every physically delivered handover — both "Aguardando termo
 * assinado" and "Finalizada" rows — so it is labelled for what it returns.
 */
const STATUS_FILTER_LABELS: Record<HandoverStatus, string> = {
  Draft: 'Rascunho',
  Finalized: 'Entrega efetuada',
}

const STATUS_PARAM = 'status'
const PAGE_PARAM = 'page'
const POSITIVE_INT = /^[1-9]\d*$/

function isHandoverStatus(value: string | null): value is HandoverStatus {
  return value !== null && (STATUSES as string[]).includes(value)
}

function OperationalStatus({ row }: { row: HandoverSummary }) {
  const meta = handoverOperationalStatusMeta[row.operationalStatus]
  return meta ? <StatusBadge label={meta.label} tone={meta.tone} icon={meta.icon} /> : <>{row.operationalStatus}</>
}

const itemCountLabel = (count: number) => (count === 1 ? '1 equipamento' : `${count} equipamentos`)

export function HandoversListPage() {
  const { hasPermission } = useAuth()
  const navigate = useNavigate()

  // The URL is the source of truth for status and page, as on the Equipment
  // list (the Dashboard deep-links to "/entregas?status=Draft", and the
  // sidebar's plain "/entregas" must then mean "all"). Free text stays local:
  // syncing a per-keystroke field through useSearchParams dropped characters
  // (Phase 9 refinement QA finding).
  const [searchParams, setSearchParams] = useSearchParams()
  const [search, setSearch] = useState('')

  const rawStatus = searchParams.get(STATUS_PARAM)
  const status = isHandoverStatus(rawStatus) ? rawStatus : undefined
  const rawPage = searchParams.get(PAGE_PARAM)
  const page = rawPage !== null && POSITIVE_INT.test(rawPage) ? Number(rawPage) : 1

  // A value in the URL that cannot be honoured visibly is removed, never silently applied.
  const statusInvalid = rawStatus !== null && !status
  const pageInvalid = rawPage !== null && !POSITIVE_INT.test(rawPage)
  useEffect(() => {
    if (!statusInvalid && !pageInvalid) return
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (statusInvalid) next.delete(STATUS_PARAM)
        next.delete(PAGE_PARAM)
        return next
      },
      { replace: true },
    )
  }, [statusInvalid, pageInvalid, setSearchParams])

  const query = useQuery({
    queryKey: ['handovers', { search, status: status ?? '', page }],
    queryFn: () =>
      api.listHandovers({
        search: search || undefined,
        status,
        page,
        pageSize: PAGE_SIZE,
      }),
  })

  /** A status change pushes a history entry and returns to page 1. */
  function changeStatus(value: string) {
    const next = new URLSearchParams(searchParams)
    if (value) next.set(STATUS_PARAM, value)
    else next.delete(STATUS_PARAM)
    next.delete(PAGE_PARAM)
    setSearchParams(next)
  }

  function goToPage(nextPage: number) {
    const next = new URLSearchParams(searchParams)
    if (nextPage > 1) next.set(PAGE_PARAM, String(nextPage))
    else next.delete(PAGE_PARAM)
    setSearchParams(next)
  }

  function changeSearch(value: string) {
    setSearch(value)
    // A new search starts from page 1 — one replace, never a per-keystroke history entry.
    if (searchParams.has(PAGE_PARAM)) {
      setSearchParams(
        (prev) => {
          const next = new URLSearchParams(prev)
          next.delete(PAGE_PARAM)
          return next
        },
        { replace: true },
      )
    }
  }

  function clearFilters() {
    setSearch('')
    const next = new URLSearchParams(searchParams)
    next.delete(STATUS_PARAM)
    next.delete(PAGE_PARAM)
    setSearchParams(next)
  }

  const canCreate = hasPermission(Permissions.EquipmentHandover.Create)
  const hasActiveFilters = Boolean(search || status)

  const columns: DataTableColumn<HandoverSummary>[] = [
    { key: 'consultant', header: 'Consultor', render: (row) => <span className="font-medium">{row.consultantFullName}</span> },
    { key: 'status', header: 'Estado', width: '220px', render: (row) => <OperationalStatus row={row} /> },
    {
      key: 'itemCount',
      header: 'Itens',
      width: '72px',
      align: 'right',
      render: (row) => <span className="tabular-nums">{row.itemCount}</span>,
    },
    {
      key: 'deliveredOn',
      header: 'Data de entrega',
      width: '128px',
      render: (row) =>
        row.deliveredOn ? <span className="tabular-nums">{formatDate(row.deliveredOn)}</span> : <span className="text-fg-subtle">—</span>,
    },
    {
      key: 'createdAtUtc',
      header: 'Criada em',
      width: '112px',
      align: 'right',
      priority: 'secondary',
      render: (row) => <span className="text-fg-muted tabular-nums">{formatDate(row.createdAtUtc)}</span>,
    },
  ]

  return (
    <div className="flex flex-col">
      <PageHeader
        title="Entregas"
        description="Entrega de equipamento a consultores, incluindo inspeção de entrega e termo de responsabilidade."
        actions={
          canCreate && (
            <Button
              variant="primary"
              size="sm"
              className="max-lg:h-11 max-lg:px-4"
              icon={<Plus className="size-4" aria-hidden />}
              onClick={() => void navigate('/entregas/nova')}
            >
              Nova entrega
            </Button>
          )
        }
      />
      <div className="flex flex-col pb-6 sm:px-3">
        <FilterBar
          search={search}
          onSearchChange={changeSearch}
          searchPlaceholder="Pesquisar por consultor…"
          hasActiveFilters={hasActiveFilters}
          onClear={clearFilters}
          summary={
            query.data && (
              <span>
                <b className="font-semibold text-fg tabular-nums">{query.data.totalCount}</b>{' '}
                {query.data.totalCount === 1 ? 'entrega' : 'entregas'}
              </span>
            )
          }
        >
          <Select
            aria-label="Filtrar por estado"
            className="w-full sm:w-48"
            value={status ?? ''}
            onChange={(e) => changeStatus(e.target.value)}
          >
            <option value="">Todos os estados</option>
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {STATUS_FILTER_LABELS[s]}
              </option>
            ))}
          </Select>
        </FilterBar>
        <DataTable
          columns={columns}
          rows={query.data?.items ?? []}
          rowKey={(row) => row.id}
          isLoading={query.isLoading}
          error={query.error}
          onRetry={() => void query.refetch()}
          getRowHref={(row) => `/entregas/${row.id}`}
          renderMobileRow={(row) => <HandoverMobileRow row={row} />}
          emptyIcon={<PackageCheck aria-hidden />}
          emptyTitle="Nenhuma entrega encontrada"
          emptyDescription={
            hasActiveFilters
              ? 'Nenhuma entrega corresponde aos filtros aplicados. Ajuste ou limpe os filtros.'
              : canCreate
                ? 'Crie uma entrega para atribuir equipamento a um consultor.'
                : undefined
          }
          emptyAction={
            hasActiveFilters ? (
              <Button variant="secondary" size="sm" onClick={clearFilters}>
                Limpar filtros
              </Button>
            ) : (
              canCreate && (
                <Button variant="secondary" size="sm" onClick={() => void navigate('/entregas/nova')}>
                  Nova entrega
                </Button>
              )
            )
          }
          pagination={
            query.data ? { page, pageSize: PAGE_SIZE, totalCount: query.data.totalCount, onPageChange: goToPage } : undefined
          }
        />
      </div>
    </div>
  )
}

/** Below 768px: consultant + state, then how many items and the delivery (or creation) date — one link per row. */
function HandoverMobileRow({ row }: { row: HandoverSummary }) {
  const when = row.deliveredOn ? `Entrega ${formatDate(row.deliveredOn)}` : `Criada ${formatDate(row.createdAtUtc)}`
  return (
    <span className="flex flex-col gap-0.5">
      <span className="flex items-center justify-between gap-3">
        <span className="min-w-0 truncate text-[14px] leading-5 font-medium text-fg">{row.consultantFullName}</span>
        <OperationalStatus row={row} />
      </span>
      <span className="text-[13px] leading-[18px] text-fg-muted tabular-nums">
        {itemCountLabel(row.itemCount)} · {when}
      </span>
    </span>
  )
}
