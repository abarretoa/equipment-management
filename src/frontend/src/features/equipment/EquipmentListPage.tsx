import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams, Link } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { Laptop, Plus } from 'lucide-react'
import { PageHeader } from '../../shared/components/PageHeader'
import { FilterBar } from '../../shared/components/FilterBar'
import { Select } from '../../shared/components/Select'
import { Tabs } from '../../shared/components/Tabs'
import { Tag } from '../../shared/components/Tag'
import { DataTable, type DataTableColumn } from '../../shared/components/DataTable'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { Button } from '../../shared/components/Button'
import { equipmentStatusMeta } from '../../shared/status/statusMaps'
import { formatDate } from '../../shared/utils/format'
import { ApiError } from '../../shared/api/httpClient'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import * as api from './api'
import type { EquipmentStatus, EquipmentSummary } from './types'
import { getEquipmentType, listEquipmentTypes } from '../equipment-types/api'

const PAGE_SIZE = 20
const STATUSES: EquipmentStatus[] = [
  'Available',
  'InTransit',
  'Assigned',
  'ReturnPending',
  'UnderInspection',
  'Damaged',
  'UnderMaintenance',
  'Retired',
]

const STATUS_PARAM = 'status'
const TYPE_PARAM = 'equipmentTypeId'
const PAGE_PARAM = 'page'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const POSITIVE_INT = /^[1-9]\d*$/

function isEquipmentStatus(value: string | null): value is EquipmentStatus {
  return value !== null && (STATUSES as string[]).includes(value)
}

const textLinkClass =
  'text-fg underline decoration-border-strong underline-offset-2 hover:decoration-fg focus-visible:decoration-fg'

export function EquipmentListPage() {
  const { hasPermission } = useAuth()
  const navigate = useNavigate()

  // The URL is the single source of truth for status, type and page
  // (Visual System v1, Phase 4): Dashboard links, refresh, bookmarks, Back and
  // the sidebar's plain "/equipamentos" all mean exactly what they say. Free
  // text stays local — syncing a per-keystroke field through useSearchParams
  // dropped characters (Phase 9 refinement QA finding).
  const [searchParams, setSearchParams] = useSearchParams()
  const [search, setSearch] = useState('')

  const rawStatus = searchParams.get(STATUS_PARAM)
  const status = isEquipmentStatus(rawStatus) ? rawStatus : undefined
  const rawPage = searchParams.get(PAGE_PARAM)
  const page = rawPage !== null && POSITIVE_INT.test(rawPage) ? Number(rawPage) : 1
  const rawTypeId = searchParams.get(TYPE_PARAM)
  const typeIdWellFormed = rawTypeId !== null && GUID.test(rawTypeId)

  const equipmentTypesQuery = useQuery({
    queryKey: ['equipment-types', 'active-options'],
    queryFn: () => listEquipmentTypes({ isActive: true, page: 1, pageSize: 100 }),
  })

  // A type in the URL that the active-types dropdown does not list (e.g. an
  // archived one) is looked up by id so its filter chip is never nameless.
  const activeMatch = typeIdWellFormed
    ? equipmentTypesQuery.data?.items.find((t) => t.id === rawTypeId)
    : undefined
  const needsTypeLookup =
    typeIdWellFormed &&
    !activeMatch &&
    (equipmentTypesQuery.isSuccess || equipmentTypesQuery.isError)
  const typeLookup = useQuery({
    queryKey: ['equipment-types', rawTypeId],
    queryFn: () => getEquipmentType(rawTypeId!),
    enabled: needsTypeLookup,
  })

  const typeNotFound =
    needsTypeLookup &&
    typeLookup.error instanceof ApiError &&
    (typeLookup.error.status === 404 || typeLookup.error.status === 400)
  const typeInvalid = rawTypeId !== null && (!typeIdWellFormed || typeNotFound)
  const equipmentTypeId = typeIdWellFormed && !typeInvalid ? rawTypeId : undefined
  const selectedType = activeMatch ?? (needsTypeLookup ? typeLookup.data : undefined)

  // Anything in the URL that cannot be honoured visibly is removed, never
  // silently applied: an unknown status, a malformed/unknown type id, a bad page.
  const statusInvalid = rawStatus !== null && !status
  const pageInvalid = rawPage !== null && !POSITIVE_INT.test(rawPage)
  useEffect(() => {
    if (!statusInvalid && !typeInvalid && !pageInvalid) return
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (statusInvalid) next.delete(STATUS_PARAM)
        if (typeInvalid) next.delete(TYPE_PARAM)
        if (pageInvalid || statusInvalid || typeInvalid) next.delete(PAGE_PARAM)
        return next
      },
      { replace: true },
    )
  }, [statusInvalid, typeInvalid, pageInvalid, setSearchParams])

  // Tab counts describe the same type scope as the list: inventory-wide with
  // no type filter, that type only when one is active. Status, page and the
  // local search never narrow them. An invalid id was already normalized
  // away above, so it falls back to the global counts. Still under the
  // ['equipment'] prefix, so equipment mutations refresh it with the list.
  const statsQuery = useQuery({
    queryKey: ['equipment', 'stats', equipmentTypeId ?? null],
    queryFn: () => api.getEquipmentStats(equipmentTypeId),
  })

  const query = useQuery({
    queryKey: [
      'equipment',
      { search, status: status ?? '', equipmentTypeId: equipmentTypeId ?? '', page },
    ],
    queryFn: () =>
      api.listEquipment({
        search: search || undefined,
        status,
        equipmentTypeId,
        page,
        pageSize: PAGE_SIZE,
      }),
  })

  /** Discrete filter changes push a history entry and always return to page 1; unrelated params are kept. */
  function updateFilters(changes: { status?: string | null; equipmentTypeId?: string | null }) {
    const next = new URLSearchParams(searchParams)
    for (const [key, value] of [
      [STATUS_PARAM, changes.status],
      [TYPE_PARAM, changes.equipmentTypeId],
    ] as const) {
      if (value === undefined) continue
      if (value) next.set(key, value)
      else next.delete(key)
    }
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
    next.delete(TYPE_PARAM)
    next.delete(PAGE_PARAM)
    setSearchParams(next)
  }

  const canWrite = hasPermission(Permissions.Equipment.Create)
  const canReadConsultants = hasPermission(Permissions.Consultant.Read)
  const hasDiscreteFilters = Boolean(status || equipmentTypeId)
  const hasActiveFilters = Boolean(search || hasDiscreteFilters)

  const tabCount = (count: number | undefined) =>
    count === undefined ? undefined : (
      <span className="text-[11px] text-fg-muted tabular-nums">{count}</span>
    )
  const statusTabs = [
    { value: '', label: 'Todos', badge: tabCount(statsQuery.data?.totalCount) },
    ...STATUSES.map((s) => ({
      value: s,
      label: equipmentStatusMeta[s]?.label ?? s,
      badge: tabCount(
        statsQuery.data?.byStatus.find((entry) => entry.status === s)?.count ??
          (statsQuery.data ? 0 : undefined),
      ),
    })),
  ]

  const typeChipLabel =
    selectedType?.name ?? (typeLookup.isError ? 'Tipo indisponível' : 'A carregar…')

  const columns: DataTableColumn<EquipmentSummary>[] = [
    {
      key: 'assetTag',
      header: 'Identificador',
      width: '136px',
      render: (row) => (
        <span className="font-mono text-[12px] font-medium whitespace-nowrap text-fg">
          {row.assetTag}
        </span>
      ),
    },
    {
      key: 'type',
      header: 'Tipo',
      render: (row) => <span className="text-fg-muted">{row.equipmentTypeName}</span>,
    },
    {
      key: 'model',
      header: 'Fabricante / modelo',
      render: (row) => {
        const parts = [row.manufacturer, row.model].filter(Boolean)
        return parts.length > 0 ? parts.join(' · ') : <span className="text-fg-subtle">—</span>
      },
    },
    {
      key: 'serialNumber',
      header: 'Nº de série',
      priority: 'secondary',
      render: (row) =>
        row.serialNumber ? (
          <span className="font-mono text-[12px] text-fg-muted">{row.serialNumber}</span>
        ) : (
          <span className="text-fg-subtle">—</span>
        ),
    },
    {
      key: 'status',
      header: 'Estado',
      width: '176px',
      render: (row) => {
        const meta = equipmentStatusMeta[row.status]
        return meta ? (
          <StatusBadge label={meta.label} tone={meta.tone} icon={meta.icon} />
        ) : (
          row.status
        )
      },
    },
    {
      key: 'currentConsultant',
      header: 'Atribuído a',
      render: (row) =>
        row.currentConsultantId ? (
          canReadConsultants ? (
            <Link to={`/consultores/${row.currentConsultantId}`} className={textLinkClass}>
              {row.currentConsultantName}
            </Link>
          ) : (
            row.currentConsultantName
          )
        ) : (
          <span className="text-fg-subtle">—</span>
        ),
    },
    {
      key: 'createdAtUtc',
      header: 'Registado em',
      width: '112px',
      align: 'right',
      priority: 'secondary',
      render: (row) => (
        <span className="text-fg-muted tabular-nums">{formatDate(row.createdAtUtc)}</span>
      ),
    },
  ]

  return (
    <div className="flex flex-col">
      <PageHeader
        title="Equipamentos"
        description="Registo de equipamento físico e o respetivo estado de ciclo de vida."
        actions={
          canWrite && (
            <Button
              variant="primary"
              size="sm"
              className="max-lg:h-11 max-lg:px-4"
              icon={<Plus className="size-4" aria-hidden />}
              onClick={() => void navigate('/equipamentos/novo')}
            >
              Novo equipamento
            </Button>
          )
        }
      />
      <div className="px-4 pt-4 sm:px-6">
        <Tabs
          items={statusTabs}
          value={status ?? ''}
          onValueChange={(value) => updateFilters({ status: value || null })}
          aria-label="Filtrar por estado"
          activationMode="manual"
        />
      </div>
      <div className="flex flex-col pb-6 sm:px-3">
        <FilterBar
          search={search}
          onSearchChange={changeSearch}
          searchPlaceholder="Pesquisar equipamentos"
          hasActiveFilters={hasActiveFilters}
          onClear={clearFilters}
          chips={
            <>
              {equipmentTypeId && (
                <Tag
                  prefix="Tipo"
                  label={typeChipLabel}
                  onRemove={() => updateFilters({ equipmentTypeId: null })}
                />
              )}
              {status && (
                <Tag
                  prefix="Estado"
                  label={equipmentStatusMeta[status]?.label ?? status}
                  onRemove={() => updateFilters({ status: null })}
                />
              )}
            </>
          }
          summary={
            query.data && (
              <span>
                <b className="font-semibold text-fg tabular-nums">{query.data.totalCount}</b>{' '}
                equipamentos
              </span>
            )
          }
        >
          <Select
            aria-label="Filtrar por tipo"
            className="w-full sm:w-48"
            value={equipmentTypeId ?? ''}
            onChange={(e) => updateFilters({ equipmentTypeId: e.target.value || null })}
          >
            <option value="">Todos os tipos</option>
            {equipmentTypesQuery.data?.items.map((type) => (
              <option key={type.id} value={type.id}>
                {type.name}
              </option>
            ))}
            {equipmentTypeId && !activeMatch && (
              <option value={equipmentTypeId}>
                {selectedType
                  ? `${selectedType.name}${selectedType.isActive ? '' : ' (inativo)'}`
                  : typeChipLabel}
              </option>
            )}
          </Select>
        </FilterBar>
        <DataTable
          columns={columns}
          rows={query.data?.items ?? []}
          rowKey={(row) => row.id}
          isLoading={query.isLoading}
          error={query.error}
          onRetry={() => void query.refetch()}
          getRowHref={(row) => `/equipamentos/${row.id}`}
          renderMobileRow={(row) => <EquipmentMobileRow row={row} />}
          emptyIcon={<Laptop aria-hidden />}
          emptyTitle={
            hasActiveFilters
              ? 'Nenhum equipamento encontrado'
              : 'Ainda não existem equipamentos registados'
          }
          emptyDescription={
            hasActiveFilters
              ? 'Nenhum equipamento corresponde aos filtros aplicados. Ajuste ou limpe os filtros.'
              : canWrite
                ? 'Registe o primeiro equipamento para começar.'
                : undefined
          }
          emptyAction={
            hasActiveFilters ? (
              <Button variant="secondary" size="sm" onClick={clearFilters}>
                Limpar filtros
              </Button>
            ) : (
              canWrite && (
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => void navigate('/equipamentos/novo')}
                >
                  Novo equipamento
                </Button>
              )
            )
          }
          pagination={
            query.data
              ? {
                  page,
                  pageSize: PAGE_SIZE,
                  totalCount: query.data.totalCount,
                  onPageChange: goToPage,
                }
              : undefined
          }
        />
      </div>
    </div>
  )
}

/** Below 768px: identifier + status, then the model, then type and custodian — one link per row, no nested links. */
function EquipmentMobileRow({ row }: { row: EquipmentSummary }) {
  const meta = equipmentStatusMeta[row.status]
  const model = [row.manufacturer, row.model].filter(Boolean).join(' · ')
  return (
    <span className="flex flex-col gap-0.5">
      <span className="flex items-center justify-between gap-3">
        <span className="font-mono text-[13px] font-medium text-fg">{row.assetTag}</span>
        {meta ? <StatusBadge label={meta.label} tone={meta.tone} icon={meta.icon} /> : row.status}
      </span>
      {model && <span className="truncate text-[14px] leading-5 text-fg">{model}</span>}
      <span className="text-[13px] leading-[18px] text-fg-muted">
        {row.equipmentTypeName}
        {row.currentConsultantName && ` · ${row.currentConsultantName}`}
      </span>
    </span>
  )
}
