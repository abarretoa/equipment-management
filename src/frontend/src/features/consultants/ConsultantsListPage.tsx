import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { Plus, Users } from 'lucide-react'
import { PageHeader } from '../../shared/components/PageHeader'
import { FilterBar } from '../../shared/components/FilterBar'
import { Select } from '../../shared/components/Select'
import { DataTable, type DataTableColumn } from '../../shared/components/DataTable'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { Button } from '../../shared/components/Button'
import { activeStatusMeta, workforceTypeLabels } from '../../shared/status/statusMaps'
import { formatDate } from '../../shared/utils/format'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import * as api from './api'
import type { ConsultantSummary, WorkforceType } from './types'

const PAGE_SIZE = 20

const workforceLabel = (row: ConsultantSummary) =>
  row.currentWorkforceType ? (workforceTypeLabels[row.currentWorkforceType] ?? row.currentWorkforceType) : undefined

export function ConsultantsListPage() {
  const { hasPermission } = useAuth()
  const navigate = useNavigate()
  // Filters stay local component state (Visual System v1, Phase 5): only the
  // Equipment list is URL-bound, because the Dashboard deep-links into it.
  const [search, setSearch] = useState('')
  const [isActive, setIsActive] = useState('')
  const [workforceType, setWorkforceType] = useState('')
  const [page, setPage] = useState(1)

  const query = useQuery({
    queryKey: ['consultants', { search, isActive, workforceType, page }],
    queryFn: () =>
      api.listConsultants({
        search: search || undefined,
        isActive: isActive ? isActive === 'true' : undefined,
        workforceType: workforceType ? (workforceType as WorkforceType) : undefined,
        page,
        pageSize: PAGE_SIZE,
      }),
  })

  const canWrite = hasPermission(Permissions.Consultant.Create)
  const hasActiveFilters = Boolean(search || isActive || workforceType)

  function clearFilters() {
    setSearch('')
    setIsActive('')
    setWorkforceType('')
    setPage(1)
  }

  const columns: DataTableColumn<ConsultantSummary>[] = [
    { key: 'fullName', header: 'Nome', render: (row) => <span className="font-medium">{row.fullName}</span> },
    {
      key: 'workforceType',
      header: 'Regime',
      width: '104px',
      render: (row) => workforceLabel(row) ?? <span className="text-fg-subtle">—</span>,
    },
    {
      key: 'company',
      header: 'Empresa',
      render: (row) => row.currentCompanyName ?? <span className="text-fg-subtle">—</span>,
    },
    {
      key: 'role',
      header: 'Função',
      render: (row) =>
        row.currentRoleOrFunction ? (
          <span className="text-fg-muted">{row.currentRoleOrFunction}</span>
        ) : (
          <span className="text-fg-subtle">—</span>
        ),
    },
    {
      key: 'status',
      header: 'Estado',
      width: '112px',
      render: (row) => {
        const meta = activeStatusMeta(row.isActive)
        return <StatusBadge label={meta.label} tone={meta.tone} />
      },
    },
    {
      key: 'createdAtUtc',
      header: 'Criado em',
      width: '112px',
      align: 'right',
      priority: 'secondary',
      render: (row) => <span className="text-fg-muted tabular-nums">{formatDate(row.createdAtUtc)}</span>,
    },
  ]

  return (
    <div className="flex flex-col">
      <PageHeader
        title="Consultores"
        description="Consultores e o respetivo vínculo profissional atual."
        actions={
          canWrite && (
            <Button
              variant="primary"
              size="sm"
              className="max-lg:h-11 max-lg:px-4"
              icon={<Plus className="size-4" aria-hidden />}
              onClick={() => void navigate('/consultores/novo')}
            >
              Novo consultor
            </Button>
          )
        }
      />
      <div className="flex flex-col pb-6 sm:px-3">
        <FilterBar
          search={search}
          onSearchChange={(v) => { setSearch(v); setPage(1) }}
          searchPlaceholder="Pesquisar por nome…"
          hasActiveFilters={hasActiveFilters}
          onClear={clearFilters}
          summary={
            query.data && (
              <span>
                <b className="font-semibold text-fg tabular-nums">{query.data.totalCount}</b>{' '}
                {query.data.totalCount === 1 ? 'consultor' : 'consultores'}
              </span>
            )
          }
        >
          <Select
            aria-label="Filtrar por estado"
            className="w-full sm:w-40"
            value={isActive}
            onChange={(e) => { setIsActive(e.target.value); setPage(1) }}
          >
            <option value="">Todos os estados</option>
            <option value="true">Ativos</option>
            <option value="false">Inativos</option>
          </Select>
          <Select
            aria-label="Filtrar por regime"
            className="w-full sm:w-40"
            value={workforceType}
            onChange={(e) => { setWorkforceType(e.target.value); setPage(1) }}
          >
            <option value="">Todos os regimes</option>
            {Object.entries(workforceTypeLabels).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
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
          getRowHref={(row) => `/consultores/${row.id}`}
          renderMobileRow={(row) => <ConsultantMobileRow row={row} />}
          emptyIcon={<Users aria-hidden />}
          emptyTitle="Nenhum consultor encontrado"
          emptyDescription={
            hasActiveFilters
              ? 'Nenhum consultor corresponde aos filtros aplicados. Ajuste ou limpe os filtros.'
              : canWrite
                ? 'Registe o primeiro consultor para começar.'
                : undefined
          }
          emptyAction={
            hasActiveFilters ? (
              <Button variant="secondary" size="sm" onClick={clearFilters}>
                Limpar filtros
              </Button>
            ) : (
              canWrite && (
                <Button variant="secondary" size="sm" onClick={() => void navigate('/consultores/novo')}>
                  Novo consultor
                </Button>
              )
            )
          }
          pagination={query.data ? { page, pageSize: PAGE_SIZE, totalCount: query.data.totalCount, onPageChange: setPage } : undefined}
        />
      </div>
    </div>
  )
}

/** Below 768px: name + state, then the current allocation (regime · company), then the function — one link per row. */
function ConsultantMobileRow({ row }: { row: ConsultantSummary }) {
  const meta = activeStatusMeta(row.isActive)
  const allocation = [workforceLabel(row), row.currentCompanyName].filter(Boolean).join(' · ')
  return (
    <span className="flex flex-col gap-0.5">
      <span className="flex items-center justify-between gap-3">
        <span className="min-w-0 truncate text-[14px] leading-5 font-medium text-fg">{row.fullName}</span>
        <StatusBadge label={meta.label} tone={meta.tone} />
      </span>
      <span className="text-[13px] leading-[18px] text-fg-muted">{allocation || 'Sem alocação ativa'}</span>
      {row.currentRoleOrFunction && (
        <span className="truncate text-[13px] leading-[18px] text-fg-muted">{row.currentRoleOrFunction}</span>
      )}
    </span>
  )
}
