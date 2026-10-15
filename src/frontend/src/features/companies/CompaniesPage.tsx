import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, MoreHorizontal, Plus } from 'lucide-react'
import { PageHeader } from '../../shared/components/PageHeader'
import { FilterBar } from '../../shared/components/FilterBar'
import { Select } from '../../shared/components/Select'
import { DataTable, type DataTableColumn } from '../../shared/components/DataTable'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { Button } from '../../shared/components/Button'
import { DropdownMenu } from '../../shared/components/DropdownMenu'
import { ConfirmDialog } from '../../shared/components/ConfirmDialog'
import { IconButton } from '../../shared/components/IconButton'
import { AdminMobileRow } from '../../shared/components/AdminMobileRow'
import { activeStatusMeta } from '../../shared/status/statusMaps'
import { formatDate } from '../../shared/utils/format'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import * as api from './api'
import type { Company } from './types'
import { CompanyFormDialog } from './CompanyFormDialog'

const PAGE_SIZE = 20

export function CompaniesPage() {
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [isActive, setIsActive] = useState<string>('')
  const [page, setPage] = useState(1)
  const [editingCompany, setEditingCompany] = useState<Company | undefined>(undefined)
  const [formOpen, setFormOpen] = useState(false)
  const [archiveTarget, setArchiveTarget] = useState<Company | null>(null)

  const query = useQuery({
    queryKey: ['companies', { search, isActive, page }],
    queryFn: () =>
      api.listCompanies({
        search: search || undefined,
        isActive: isActive ? isActive === 'true' : undefined,
        page,
        pageSize: PAGE_SIZE,
      }),
  })

  const archiveMutation = useMutation({
    mutationFn: (company: Company) => api.setCompanyActive(company.id, !company.isActive),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['companies'] })
      setArchiveTarget(null)
    },
  })

  const canArchive = hasPermission(Permissions.Company.Archive)
  const canWrite = hasPermission(Permissions.Company.Create)
  const hasActiveFilters = Boolean(search || isActive)

  function clearFilters() {
    setSearch('')
    setIsActive('')
    setPage(1)
  }

  function openCreate() {
    setEditingCompany(undefined)
    setFormOpen(true)
  }

  function rowMenu(row: Company, size: 'sm' | 'touch') {
    return (
      <DropdownMenu
        trigger={<IconButton icon={<MoreHorizontal className="size-4" aria-hidden />} label={`Mais ações: ${row.name}`} size={size} />}
        items={[
          { label: 'Editar', onSelect: () => { setEditingCompany(row); setFormOpen(true) }, disabled: !canWrite },
          ...(canArchive
            ? [
                {
                  label: row.isActive ? 'Desativar' : 'Ativar',
                  destructive: row.isActive,
                  onSelect: () => setArchiveTarget(row),
                },
              ]
            : []),
        ]}
      />
    )
  }

  const columns: DataTableColumn<Company>[] = [
    { key: 'name', header: 'Nome', render: (row) => <span className="font-medium">{row.name}</span> },
    {
      key: 'status',
      header: 'Estado',
      width: '128px',
      render: (row) => {
        const meta = activeStatusMeta(row.isActive)
        return <StatusBadge label={meta.label} tone={meta.tone} />
      },
    },
    {
      key: 'createdAtUtc',
      header: 'Criada em',
      width: '112px',
      align: 'right',
      render: (row) => <span className="text-fg-muted tabular-nums">{formatDate(row.createdAtUtc)}</span>,
    },
    { key: 'actions', header: '', width: '48px', align: 'right', render: (row) => rowMenu(row, 'sm') },
  ]

  return (
    <div className="flex flex-col">
      <PageHeader
        title="Empresas"
        description="Empresas cliente associadas a consultores externos."
        actions={
          canWrite && (
            <Button
              variant="primary"
              size="sm"
              className="max-lg:h-11 max-lg:px-4"
              icon={<Plus className="size-4" aria-hidden />}
              onClick={openCreate}
            >
              Nova empresa
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
                {query.data.totalCount === 1 ? 'empresa' : 'empresas'}
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
            <option value="true">Ativas</option>
            <option value="false">Inativas</option>
          </Select>
        </FilterBar>
        <DataTable
          columns={columns}
          rows={query.data?.items ?? []}
          rowKey={(row) => row.id}
          isLoading={query.isLoading}
          error={query.error}
          onRetry={() => void query.refetch()}
          renderMobileRow={(row) => {
            const meta = activeStatusMeta(row.isActive)
            return (
              <AdminMobileRow
                name={row.name}
                status={<StatusBadge label={meta.label} tone={meta.tone} />}
                meta={`Criada em ${formatDate(row.createdAtUtc)}`}
                menu={rowMenu(row, 'touch')}
              />
            )
          }}
          emptyIcon={<Building2 aria-hidden />}
          emptyTitle="Nenhuma empresa encontrada"
          emptyDescription={
            hasActiveFilters
              ? 'Nenhuma empresa corresponde aos filtros aplicados. Ajuste ou limpe os filtros.'
              : canWrite
                ? 'Crie a primeira empresa cliente para a associar a consultores externos.'
                : undefined
          }
          emptyAction={
            hasActiveFilters ? (
              <Button variant="secondary" size="sm" onClick={clearFilters}>
                Limpar filtros
              </Button>
            ) : (
              canWrite && (
                <Button variant="secondary" size="sm" onClick={openCreate}>
                  Nova empresa
                </Button>
              )
            )
          }
          pagination={
            query.data
              ? { page, pageSize: PAGE_SIZE, totalCount: query.data.totalCount, onPageChange: setPage }
              : undefined
          }
        />
      </div>

      <CompanyFormDialog open={formOpen} onOpenChange={setFormOpen} company={editingCompany} />

      <ConfirmDialog
        open={Boolean(archiveTarget)}
        onOpenChange={(open) => !open && setArchiveTarget(null)}
        title={archiveTarget?.isActive ? 'Desativar empresa' : 'Ativar empresa'}
        description={
          archiveTarget?.isActive
            ? `A empresa "${archiveTarget?.name}" deixará de estar disponível para novas alocações externas. O histórico existente não é afetado.`
            : `A empresa "${archiveTarget?.name}" volta a estar disponível para novas alocações externas.`
        }
        confirmLabel={archiveTarget?.isActive ? 'Desativar' : 'Ativar'}
        destructive={archiveTarget?.isActive}
        loading={archiveMutation.isPending}
        onConfirm={() => archiveTarget && archiveMutation.mutate(archiveTarget)}
      />
    </div>
  )
}
