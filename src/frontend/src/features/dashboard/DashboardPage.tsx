import { useEffect, type ReactNode } from 'react'
import { useQuery, type UseQueryResult } from '@tanstack/react-query'
import { Link, useNavigate, useSearchParams } from 'react-router'
import {
  ArrowUpRight,
  ChevronRight,
  CloudOff,
  FileClock,
  Plus,
  ScanSearch,
  TriangleAlert,
  Undo2,
  Wrench,
  X,
  type LucideIcon,
} from 'lucide-react'
import { PageHeader } from '../../shared/components/PageHeader'
import { Button } from '../../shared/components/Button'
import { StatusBadge, type StatusTone } from '../../shared/components/StatusBadge'
import { Skeleton } from '../../shared/components/Skeleton'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import {
  equipmentStatusMeta,
  handoverOperationalStatusMeta,
  returnStatusMeta,
  type StatusMeta,
} from '../../shared/status/statusMaps'
import { formatDate } from '../../shared/utils/format'
import { cn } from '../../shared/utils/cn'
import { ApiError, buildQuery } from '../../shared/api/httpClient'
import { useDashboardMetrics } from './useDashboardMetrics'
import { getEquipmentStats } from '../equipment/api'
import { listHandovers } from '../handovers/api'
import { listReturns } from '../returns/api'
import type { EquipmentStatus, EquipmentStatusCount } from '../equipment/types'

/** Fixed display order for the status distribution — mirrors the Equipment list's status tabs. */
const STATUS_ORDER: EquipmentStatus[] = [
  'Available',
  'InTransit',
  'Assigned',
  'ReturnPending',
  'UnderInspection',
  'Damaged',
  'UnderMaintenance',
  'Retired',
]

/** One-line reading aid under each status cell (Visual System v1 Screens, 03 Dashboard). Label/tone/icon stay in `equipmentStatusMeta`. */
const STATUS_HINT: Record<EquipmentStatus, string> = {
  Available: 'Prontos a entregar',
  InTransit: 'Envio por transportadora',
  Assigned: 'Em custódia',
  ReturnPending: 'Por receber',
  UnderInspection: 'Após receção',
  Damaged: 'Requer decisão',
  UnderMaintenance: 'Em reparação',
  Retired: 'Fora de circulação',
}

/** The Dashboard's only URL state: the "Inventário disponível por tipo" scope (Visual System v1, Phase 3). */
const TYPE_PARAM = 'equipmentTypeId'

/** Dashboard text links — ink is the primary colour, so links carry weight + underline rather than colour (Visual System v1). */
const textLinkClass =
  'font-medium text-fg underline decoration-border-strong underline-offset-2 hover:decoration-fg focus-visible:decoration-fg'

export function DashboardPage() {
  const { user, hasPermission } = useAuth()
  const navigate = useNavigate()
  const { equipmentStats, consultantsActive, handoversDraft, returnsPending, returnsReceived } =
    useDashboardMetrics()

  // Selecting an Equipment Type re-scopes only "Equipamento por estado" —
  // every other dashboard figure (attention list, inventory, context links)
  // stays global (Iteration 1). The URL is the source
  // of truth so Back from the Equipment list restores the scope.
  const [searchParams, setSearchParams] = useSearchParams()
  const requestedTypeId = searchParams.get(TYPE_PARAM)
  const availableByType = equipmentStats.data?.availableByType
  // Only an id present in the available-by-type response is a valid scope —
  // anything else (stale, malformed) is never sent to the API or labelled.
  const selectedType =
    (requestedTypeId && availableByType?.find((t) => t.equipmentTypeId === requestedTypeId)) || null

  const typeUnresolvable =
    requestedTypeId !== null && equipmentStats.isSuccess && selectedType === null
  useEffect(() => {
    if (typeUnresolvable) setSearchParams({}, { replace: true })
  }, [typeUnresolvable, setSearchParams])

  const typeStats = useQuery({
    queryKey: ['dashboard', 'equipment', 'stats', selectedType?.equipmentTypeId ?? null],
    queryFn: () => getEquipmentStats(selectedType!.equipmentTypeId),
    enabled: selectedType !== null,
  })

  const recentHandovers = useQuery({
    queryKey: ['dashboard', 'recent-handovers'],
    queryFn: () => listHandovers({ page: 1, pageSize: 5 }),
  })

  const recentReturns = useQuery({
    queryKey: ['dashboard', 'recent-returns'],
    queryFn: () => listReturns({ page: 1, pageSize: 5 }),
  })

  function toggleType(id: string) {
    setSearchParams(requestedTypeId === id ? {} : { [TYPE_PARAM]: id })
  }

  function clearType() {
    setSearchParams({})
  }

  const distributionQuery = selectedType ? typeStats : equipmentStats

  const globalStatusCount = (status: EquipmentStatus) =>
    countFor(equipmentStats.data?.byStatus, status)

  const attentionItems: AttentionItem[] = [
    {
      label: 'Entregas em rascunho',
      icon: FileClock,
      tone: 'warning',
      to: '/entregas?status=Draft',
      queries: [handoversDraft],
      value: () => handoversDraft.data!.totalCount,
    },
    {
      label: 'Devoluções pendentes',
      icon: Undo2,
      tone: 'warning',
      to: '/devolucoes',
      queries: [returnsPending, returnsReceived],
      value: () => returnsPending.data!.totalCount + returnsReceived.data!.totalCount,
    },
    {
      label: 'Em inspeção',
      icon: ScanSearch,
      tone: 'info',
      to: '/equipamentos?status=UnderInspection',
      queries: [equipmentStats],
      value: () => globalStatusCount('UnderInspection'),
    },
    {
      label: 'Equipamentos avariados',
      icon: TriangleAlert,
      tone: 'danger',
      to: '/equipamentos?status=Damaged',
      queries: [equipmentStats],
      value: () => globalStatusCount('Damaged'),
    },
    {
      label: 'Em manutenção',
      icon: Wrench,
      tone: 'warning',
      to: '/manutencao',
      queries: [equipmentStats],
      value: () => globalStatusCount('UnderMaintenance'),
    },
  ]

  return (
    <div className="flex flex-col">
      <PageHeader
        title={`Olá, ${user?.firstName}`}
        description="Resumo operacional do estado atual dos equipamentos e processos."
        actions={
          hasPermission(Permissions.EquipmentHandover.Create) && (
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

      <div className="flex flex-col gap-6 px-4 py-6 sm:px-6">
        {/* Available inventory is the primary operational answer this dashboard exists to give. */}
        <section aria-labelledby="dashboard-inventory" className="flex flex-col gap-3">
          <SectionHeading id="dashboard-inventory" title="Inventário disponível">
            <Link
              to="/equipamentos?status=Available"
              className={cn(textLinkClass, 'text-[12.5px]')}
            >
              Ver equipamento disponível
            </Link>
          </SectionHeading>

          {equipmentStats.isError ? (
            <InlineError
              error={equipmentStats.error}
              onRetry={() => void equipmentStats.refetch()}
            />
          ) : (
            <div className="flex flex-col gap-5 border-t border-border-divider pt-4 md:flex-row md:gap-8">
              <div className="flex shrink-0 flex-col gap-1.5 md:w-60">
                {equipmentStats.isPending ? (
                  <>
                    <Skeleton className="h-9 w-20" />
                    <Skeleton className="h-4 w-44" />
                  </>
                ) : (
                  <>
                    <p className="text-[34px] leading-none font-semibold tracking-[-0.02em] tabular-nums text-fg">
                      {globalStatusCount('Available')}
                      <span className="sr-only"> equipamentos disponíveis</span>
                    </p>
                    <p className="text-[12.5px] leading-5 text-fg-muted">
                      de{' '}
                      <Link to="/equipamentos" className={textLinkClass}>
                        <span className="tabular-nums">{equipmentStats.data.totalCount}</span>{' '}
                        equipamentos
                      </Link>{' '}
                      no total
                    </p>
                  </>
                )}
                <ConsultantsActiveLink query={consultantsActive} />
              </div>

              <div className="min-w-0 flex-1 md:border-l md:border-border md:pl-8">
                <h3
                  id="dashboard-inventory-by-type"
                  className="text-[12px] leading-4 font-medium text-fg-muted"
                >
                  Inventário disponível por tipo
                </h3>
                {equipmentStats.isPending ? (
                  <div className="mt-2 flex flex-wrap gap-1.5">
                    <Skeleton className="h-8 w-24" />
                    <Skeleton className="h-8 w-20" />
                    <Skeleton className="h-8 w-28" />
                  </div>
                ) : equipmentStats.data.availableByType.length === 0 ? (
                  <p className="mt-2 text-[13px] text-fg-muted">
                    Sem equipamento disponível de momento.
                  </p>
                ) : (
                  <div
                    role="group"
                    aria-labelledby="dashboard-inventory-by-type"
                    className="mt-2 flex flex-wrap gap-1.5"
                  >
                    {equipmentStats.data.availableByType.map((entry) => {
                      const isSelected = selectedType?.equipmentTypeId === entry.equipmentTypeId
                      return (
                        <button
                          key={entry.equipmentTypeId}
                          type="button"
                          onClick={() => toggleType(entry.equipmentTypeId)}
                          aria-pressed={isSelected}
                          className={cn(
                            'inline-flex h-11 items-center gap-2 rounded-md border px-3 text-[13px] text-fg transition-colors duration-[120ms] motion-reduce:transition-none sm:h-8 sm:px-2.5',
                            isSelected
                              ? 'border-fg bg-surface-selected font-semibold'
                              : 'border-border-control bg-surface hover:bg-surface-sunken',
                          )}
                        >
                          {entry.equipmentTypeName}{' '}
                          <span className="font-semibold tabular-nums">{entry.count}</span>
                        </button>
                      )
                    })}
                  </div>
                )}
              </div>
            </div>
          )}
        </section>

        <section aria-labelledby="dashboard-status" className="flex flex-col gap-2.5">
          <div className="flex min-h-8 flex-wrap items-center gap-x-2.5 gap-y-1.5">
            <h2 id="dashboard-status" className="text-[14px] leading-5 font-semibold text-fg">
              Equipamento por estado{' '}
              <span
                className={selectedType ? 'font-semibold text-fg' : 'font-normal text-fg-muted'}
              >
                · {selectedType ? selectedType.equipmentTypeName : 'Todos os tipos'}
              </span>
            </h2>
            {selectedType && (
              <button
                type="button"
                onClick={clearType}
                aria-label={`Todos os tipos (remover o filtro ${selectedType.equipmentTypeName})`}
                className="inline-flex h-11 items-center gap-1 rounded-md border border-border-control bg-surface pr-1.5 pl-2 text-[12px] font-medium text-fg hover:bg-surface-sunken sm:h-6"
              >
                Todos os tipos
                <X className="size-3" aria-hidden />
              </button>
            )}
          </div>

          {distributionQuery.isError ? (
            <InlineError
              error={distributionQuery.error}
              onRetry={() => void distributionQuery.refetch()}
            />
          ) : (
            <StatusStrip
              byStatus={distributionQuery.isPending ? undefined : distributionQuery.data?.byStatus}
              selectedType={
                selectedType
                  ? { id: selectedType.equipmentTypeId, name: selectedType.equipmentTypeName }
                  : null
              }
            />
          )}
        </section>

        <div className="mt-2 grid grid-cols-1 gap-8 lg:grid-cols-[minmax(0,2fr)_minmax(300px,1fr)]">
          {/* Operational attention — global on purpose: the type scope above never narrows it. */}
          <section aria-labelledby="dashboard-attention" className="flex flex-col gap-1.5">
            <SectionHeading id="dashboard-attention" title="Atenção operacional" />
            <AttentionList items={attentionItems} />
          </section>

          <section aria-labelledby="dashboard-recent" className="flex flex-col gap-1.5">
            <SectionHeading id="dashboard-recent" title="Atividade recente" />
            <div className="flex flex-col gap-4">
              <RecentList
                title="Entregas"
                emptyLabel="Sem entregas recentes"
                query={recentHandovers}
                toItems={(data) =>
                  data.items.map((h) => ({
                    id: h.id,
                    to: `/entregas/${h.id}`,
                    primary: h.consultantFullName,
                    secondary: formatDate(h.createdAtUtc),
                    status: handoverOperationalStatusMeta[h.operationalStatus],
                  }))
                }
              />
              <RecentList
                title="Devoluções"
                emptyLabel="Sem devoluções recentes"
                query={recentReturns}
                toItems={(data) =>
                  data.items.map((r) => ({
                    id: r.id,
                    to: `/devolucoes/${r.id}`,
                    primary: r.consultantFullName,
                    secondary: `${r.assetTag} · ${formatDate(r.createdAtUtc)}`,
                    status: returnStatusMeta[r.status],
                  }))
                }
              />
            </div>
          </section>
        </div>
      </div>
    </div>
  )
}

function countFor(byStatus: EquipmentStatusCount[] | undefined, status: EquipmentStatus) {
  return byStatus?.find((s) => s.status === status)?.count ?? 0
}

function SectionHeading({
  id,
  title,
  children,
}: {
  id: string
  title: string
  children?: ReactNode
}) {
  return (
    <div className="flex min-h-7 flex-wrap items-center justify-between gap-x-4 gap-y-1">
      <h2 id={id} className="text-[14px] leading-5 font-semibold text-fg">
        {title}
      </h2>
      {children}
    </div>
  )
}

/**
 * Compact section-level failure: says the data did not load (never a "0"),
 * keeps the rest of the Dashboard usable, and retries only the failed query.
 */
function InlineError({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  const message =
    error instanceof ApiError ? error.message : 'Ocorreu um erro inesperado. Tente novamente.'
  return (
    <div
      role="alert"
      className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-lg border border-danger-border bg-danger-bg px-3.5 py-3"
    >
      <CloudOff className="size-4 shrink-0 text-danger" aria-hidden />
      <p className="min-w-0 flex-1 text-[13px] leading-5 text-fg">
        <span className="font-semibold">Não foi possível carregar os dados.</span>{' '}
        <span className="text-fg-muted">{message}</span>
      </p>
      <Button variant="secondary" size="sm" onClick={onRetry}>
        Tentar novamente
      </Button>
    </div>
  )
}

function ConsultantsActiveLink({ query }: { query: UseQueryResult<{ totalCount: number }> }) {
  if (query.isPending) return <Skeleton className="h-4 w-36" />
  if (query.isError) {
    return (
      <p className="text-[12.5px] leading-5 text-fg-muted">
        Consultores ativos indisponíveis.{' '}
        <button type="button" onClick={() => void query.refetch()} className={textLinkClass}>
          Tentar novamente
        </button>
      </p>
    )
  }
  return (
    <p className="text-[12.5px] leading-5 text-fg-muted">
      <Link to="/consultores" className={textLinkClass}>
        <span className="tabular-nums">{query.data.totalCount}</span> consultores ativos
      </Link>
    </p>
  )
}

const statusIconClass: Record<StatusTone, string> = {
  success: 'text-success',
  primary: 'text-fg-muted',
  neutral: 'text-fg-subtle',
  warning: 'text-warning',
  info: 'text-info',
  danger: 'text-danger',
}

/**
 * Seven status links in one bordered strip (Visual System v1 "KPI / StatItem").
 * Rows below md, a 4-up grid to xl, the approved seven-cell strip at xl+.
 * Zero counts stay visible and navigable — quieter, never disabled: the count
 * can be stale and the Equipment list has its own empty state.
 */
function StatusStrip({
  byStatus,
  selectedType,
}: {
  byStatus: EquipmentStatusCount[] | undefined
  selectedType: { id: string; name: string } | null
}) {
  return (
    // Each cell draws its own top/left hairline; the -1px offset tucks the outer-edge
    // lines under the frame's border, so any column count divides cleanly.
    <div className="overflow-hidden rounded-lg border border-border">
      <ul className="-mt-px -ml-px grid grid-cols-1 md:grid-cols-4 2xl:grid-cols-8">
        {STATUS_ORDER.map((status) => {
          const meta = equipmentStatusMeta[status] ?? { label: status, tone: 'neutral' as const }
          const Icon = meta.icon
          const count = byStatus ? countFor(byStatus, status) : undefined
          const zero = count === 0
          return (
            <li key={status} className="border-t border-l border-border">
              <Link
                to={`/equipamentos${buildQuery({ status, equipmentTypeId: selectedType?.id })}`}
                aria-label={
                  count === undefined
                    ? undefined
                    : `${meta.label}: ${count}. Abrir equipamentos com estado ${meta.label}${selectedType ? `, tipo ${selectedType.name}` : ''}`
                }
                className="group grid min-h-11 grid-cols-[16px_minmax(0,1fr)_auto_14px] items-center gap-x-2.5 px-3.5 py-2 transition-colors duration-[120ms] hover:bg-surface-sunken focus-visible:outline-offset-[-2px] motion-reduce:transition-none md:flex md:h-full md:flex-col md:items-stretch md:gap-1.5 md:py-3"
              >
                <span className="contents md:flex md:items-center md:gap-1.5">
                  {Icon && (
                    <Icon
                      className={cn('size-[13px] shrink-0', statusIconClass[meta.tone])}
                      aria-hidden
                    />
                  )}
                  <span className="min-w-0 text-[13px] leading-4 text-fg md:truncate md:text-[12px] md:text-fg-muted">
                    {meta.label}
                  </span>
                </span>
                <span className="contents md:flex md:items-baseline md:justify-between">
                  {count === undefined ? (
                    <Skeleton className="h-5 w-8 justify-self-end md:h-7 md:w-10" />
                  ) : (
                    <span
                      className={cn(
                        'text-right text-[15px] leading-5 font-semibold tracking-[-0.02em] tabular-nums md:text-[24px] md:leading-7',
                        zero ? 'text-fg-disabled' : 'text-fg',
                      )}
                    >
                      {count}
                    </span>
                  )}
                  <ArrowUpRight
                    className={cn(
                      'size-3.5 shrink-0 text-fg-disabled group-hover:text-fg',
                      zero && 'opacity-60',
                    )}
                    aria-hidden
                  />
                </span>
                <span className="hidden text-[11px] leading-[14px] text-fg-muted md:block">
                  {STATUS_HINT[status]}
                </span>
              </Link>
            </li>
          )
        })}
      </ul>
    </div>
  )
}

interface AttentionItem {
  label: string
  icon: LucideIcon
  tone: 'warning' | 'info' | 'danger'
  to: string
  /** Every query the value depends on — any failure shows "unavailable", never a zero. */
  queries: UseQueryResult[]
  value: () => number
}

const attentionToneClasses: Record<AttentionItem['tone'] | 'neutral', string> = {
  warning: 'border-warning-border bg-warning-bg text-warning',
  info: 'border-info-border bg-info-bg text-info',
  danger: 'border-danger-border bg-danger-bg text-danger',
  neutral: 'border-border bg-surface-sunken text-fg-subtle',
}

/** Five aggregate worklist links — one row each, with a chevron. No item-level records exist on the Dashboard. */
function AttentionList({ items }: { items: AttentionItem[] }) {
  const failed = [...new Set(items.flatMap((item) => item.queries.filter((q) => q.isError)))]
  const firstFailure = failed[0]

  return (
    <div className="flex flex-col gap-2">
      {firstFailure && (
        <InlineError
          error={firstFailure.error}
          onRetry={() => failed.forEach((q) => void q.refetch())}
        />
      )}
      <ul className="border-t border-border-divider">
        {items.map((item) => {
          const isError = item.queries.some((q) => q.isError)
          const isPending = !isError && item.queries.some((q) => q.isPending)
          const value = isError || isPending ? undefined : item.value()
          const tone = value ? item.tone : 'neutral'
          return (
            <li key={item.label} className="border-b border-border-row">
              <Link
                to={item.to}
                className="grid min-h-11 grid-cols-[28px_minmax(0,1fr)_auto_16px] items-center gap-3 py-2 transition-colors duration-[120ms] hover:bg-surface-sunken focus-visible:outline-offset-[-2px] motion-reduce:transition-none"
              >
                <span
                  className={cn(
                    'flex size-7 items-center justify-center rounded-md border',
                    attentionToneClasses[tone],
                  )}
                >
                  <item.icon className="size-3.5" aria-hidden />
                </span>
                <span className="min-w-0 text-[13px] leading-5 text-fg">{item.label}</span>
                {isPending ? (
                  <Skeleton className="h-5 w-8" />
                ) : isError ? (
                  <span className="text-[15px] font-semibold text-fg-disabled">
                    <span aria-hidden>—</span>
                    <span className="sr-only">: indisponível</span>
                  </span>
                ) : (
                  <span className="text-right text-[15px] leading-5 font-semibold tabular-nums text-fg">
                    <span className="sr-only">: </span>
                    {value}
                  </span>
                )}
                <ChevronRight className="size-3.5 text-fg-muted" aria-hidden />
              </Link>
            </li>
          )
        })}
      </ul>
    </div>
  )
}

interface RecentItem {
  id: string
  to: string
  primary: string
  secondary: string
  status?: StatusMeta
}

interface RecentListProps<T> {
  title: string
  emptyLabel: string
  query: UseQueryResult<T>
  toItems: (data: T) => RecentItem[]
}

function RecentList<T>({ title, emptyLabel, query, toItems }: RecentListProps<T>) {
  return (
    <div>
      <h3 className="text-[11px] leading-4 font-medium tracking-[0.02em] text-fg-muted">{title}</h3>
      <div className="mt-1 border-t border-border-divider">
        {query.isPending ? (
          <div className="flex flex-col gap-2 py-2">
            <Skeleton className="h-9 w-full" />
            <Skeleton className="h-9 w-full" />
          </div>
        ) : query.isError ? (
          <div className="pt-2">
            <InlineError error={query.error} onRetry={() => void query.refetch()} />
          </div>
        ) : (
          (() => {
            const items = toItems(query.data)
            return items.length === 0 ? (
              <p className="py-2.5 text-[13px] text-fg-muted">{emptyLabel}</p>
            ) : (
              <ul>
                {items.map((item) => (
                  <li key={item.id} className="border-b border-border-row">
                    <Link
                      to={item.to}
                      className="flex min-h-11 items-center justify-between gap-2.5 py-1.5 transition-colors duration-[120ms] hover:bg-surface-sunken focus-visible:outline-offset-[-2px] motion-reduce:transition-none"
                    >
                      <span className="min-w-0">
                        <span className="block truncate text-[13px] leading-[18px] font-medium text-fg">
                          {item.primary}
                        </span>
                        <span className="block text-[12px] leading-4 text-fg-muted">
                          {item.secondary}
                        </span>
                      </span>
                      {item.status && (
                        <StatusBadge
                          label={item.status.label}
                          tone={item.status.tone}
                          icon={item.status.icon}
                          className="shrink-0"
                        />
                      )}
                    </Link>
                  </li>
                ))}
              </ul>
            )
          })()
        )}
      </div>
    </div>
  )
}
