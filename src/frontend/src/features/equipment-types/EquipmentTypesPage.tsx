import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { MoreHorizontal, Plus, Tags } from 'lucide-react'
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
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import * as api from './api'
import type { EquipmentType } from './types'
import { EquipmentTypeFormDialog } from './EquipmentTypeFormDialog'

const PAGE_SIZE = 20
const kindLabels: Record<string, string> = { Generic: 'Genérico', Laptop: 'Portátil', MobilePhone: 'Telemóvel' }

export function EquipmentTypesPage() {
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [isActive, setIsActive] = useState('')
  const [page, setPage] = useState(1)
  const [editing, setEditing] = useState<EquipmentType | undefined>(undefined)
  const [formOpen, setFormOpen] = useState(false)
  const [archiveTarget, setArchiveTarget] = useState<EquipmentType | null>(null)

  const query = useQuery({
    queryKey: ['equipment-types', { search, isActive, page }],
    queryFn: () =>
      api.listEquipmentTypes({
        search: search || undefined,
        isActive: isActive ? isActive === 'true' : undefined,
        page,
        pageSize: PAGE_SIZE,
      }),
  })

  const archiveMutation = useMutation({
    mutationFn: (row: EquipmentType) => api.setEquipmentTypeActive(row.id, !row.isActive),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['equipment-types'] })
      setArchiveTarget(null)
    },
  })

  const canArchive = hasPermission(Permissions.EquipmentType.Archive)
  const canWrite = hasPermission(Permissions.EquipmentType.Create)
  const hasActiveFilters = Boolean(search || isActive)

  function clearFilters() {
    setSearch('')
    setIsActive('')
    setPage(1)
  }

  function openCreate() {
    setEditing(undefined)
    setFormOpen(true)
  }

  function rowMenu(row: EquipmentType, size: 'sm' | 'touch') {
    return (
      <DropdownMenu
        trigger={<IconButton icon={<MoreHorizontal className="size-4" aria-hidden />} label={`Mais ações: ${row.name}`} size={size} />}
        items={[
          { label: 'Editar', onSelect: () => { setEditing(row); setFormOpen(true) }, disabled: !canWrite },
          ...(canArchive
            ? [{ label: row.isActive ? 'Desativar' : 'Ativar', destructive: row.isActive, onSelect: () => setArchiveTarget(row) }]
            : []),
        ]}
      />
    )
  }

  const columns: DataTableColumn<EquipmentType>[] = [
    { key: 'name', header: 'Nome', render: (row) => <span className="font-medium">{row.name}</span> },
    { key: 'kind', header: 'Categoria', width: '136px', render: (row) => <span className="text-fg-muted">{kindLabels[row.kind] ?? row.kind}</span> },
    {
      key: 'prefix',
      header: 'Prefixo',
      width: '104px',
      render: (row) => <span className="font-mono text-[12px] font-medium text-fg">{row.assetTagPrefix}</span>,
    },
    {
      key: 'status',
      header: 'Estado',
      width: '128px',
      render: (row) => {
        const meta = activeStatusMeta(row.isActive)
        return <StatusBadge label={meta.label} tone={meta.tone} />
      },
    },
    { key: 'actions', header: '', width: '48px', align: 'right', render: (row) => rowMenu(row, 'sm') },
  ]

  return (
    <div className="flex flex-col">
      <PageHeader
        title="Tipos de equipamento"
        description="Categorias reutilizáveis de equipamento (ex.: Portátil, Monitor, Teclado)."
        actions={
          canWrite && (
            <Button
              variant="primary"
              size="sm"
              className="max-lg:h-11 max-lg:px-4"
              icon={<Plus className="size-4" aria-hidden />}
              onClick={openCreate}
            >
              Novo tipo
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
                {query.data.totalCount === 1 ? 'tipo' : 'tipos'}
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
                meta={
                  <>
                    {kindLabels[row.kind] ?? row.kind} · <span className="font-mono text-[12px]">{row.assetTagPrefix}</span>
                  </>
                }
                menu={rowMenu(row, 'touch')}
              />
            )
          }}
          emptyIcon={<Tags aria-hidden />}
          emptyTitle="Nenhum tipo de equipamento encontrado"
          emptyDescription={
            hasActiveFilters
              ? 'Nenhum tipo corresponde aos filtros aplicados. Ajuste ou limpe os filtros.'
              : canWrite
                ? 'Crie o primeiro tipo para poder registar equipamento.'
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
                  Novo tipo
                </Button>
              )
            )
          }
          pagination={query.data ? { page, pageSize: PAGE_SIZE, totalCount: query.data.totalCount, onPageChange: setPage } : undefined}
        />
      </div>

      <EquipmentTypeFormDialog open={formOpen} onOpenChange={setFormOpen} equipmentType={editing} />

      <ConfirmDialog
        open={Boolean(archiveTarget)}
        onOpenChange={(open) => !open && setArchiveTarget(null)}
        title={archiveTarget?.isActive ? 'Desativar tipo de equipamento' : 'Ativar tipo de equipamento'}
        description={
          archiveTarget?.isActive
            ? `O tipo "${archiveTarget?.name}" deixa de estar disponível para novo equipamento. Os equipamentos já registados não são afetados.`
            : `O tipo "${archiveTarget?.name}" volta a estar disponível para novo equipamento.`
        }
        confirmLabel={archiveTarget?.isActive ? 'Desativar' : 'Ativar'}
        destructive={archiveTarget?.isActive}
        loading={archiveMutation.isPending}
        onConfirm={() => archiveTarget && archiveMutation.mutate(archiveTarget)}
      />
    </div>
  )
}
