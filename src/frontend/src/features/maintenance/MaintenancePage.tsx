import { useState } from 'react'
import { Link } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Wrench } from 'lucide-react'
import { PageHeader } from '../../shared/components/PageHeader'
import { FilterBar } from '../../shared/components/FilterBar'
import { Select } from '../../shared/components/Select'
import { Textarea } from '../../shared/components/Textarea'
import { DataTable, type DataTableColumn } from '../../shared/components/DataTable'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { Drawer } from '../../shared/components/Drawer'
import { Button } from '../../shared/components/Button'
import { FormField } from '../../shared/components/FormField'
import { KeyValueList } from '../../shared/components/KeyValueList'
import { Skeleton } from '../../shared/components/Skeleton'
import { ErrorState } from '../../shared/components/ErrorState'
import { ApiError } from '../../shared/api/httpClient'
import { formatDate } from '../../shared/utils/format'
import { maintenanceStatusMeta, maintenanceOutcomeMeta } from '../../shared/status/statusMaps'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import * as api from './api'
import type { MaintenanceStatus, MaintenanceSummary } from './types'

const PAGE_SIZE = 20
const STATUS_OPTIONS: MaintenanceStatus[] = ['Active', 'Completed']

const completeSchema = z.object({
  outcome: z.enum(['Repaired', 'NotRepairable']),
  notes: z.string().optional(),
})
type CompleteFormValues = z.infer<typeof completeSchema>

function MaintenanceStatusBadge({ status }: { status: MaintenanceStatus }) {
  const meta = maintenanceStatusMeta[status]
  return meta ? <StatusBadge label={meta.label} tone={meta.tone} icon={meta.icon} /> : <>{status}</>
}

const equipmentSecondary = (row: MaintenanceSummary) => [row.manufacturer, row.model].filter(Boolean).join(' ') || row.equipmentTypeName

export function MaintenancePage() {
  const { hasPermission } = useAuth()
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const query = useQuery({
    queryKey: ['maintenance', { status, page }],
    queryFn: () => api.listMaintenance({ status: (status || undefined) as MaintenanceStatus | undefined, page, pageSize: PAGE_SIZE }),
  })

  function clearFilters() {
    setStatus('')
    setPage(1)
  }

  // There is no maintenance page of its own: the record opens in a drawer over
  // the list, so the primary cell is a real button (keyboard reachable), not a link.
  const columns: DataTableColumn<MaintenanceSummary>[] = [
    {
      key: 'assetTag',
      header: 'Equipamento',
      render: (row) => (
        <button
          type="button"
          onClick={() => setSelectedId(row.id)}
          aria-label={`Abrir manutenção de ${row.assetTag}`}
          className="flex flex-col items-start text-left focus-visible:outline-offset-2"
        >
          <span className="font-mono text-[12px] font-medium text-fg underline-offset-2 hover:underline">{row.assetTag}</span>
          <span className="text-[12px] text-fg-muted">{equipmentSecondary(row)}</span>
        </button>
      ),
    },
    { key: 'status', header: 'Estado', width: '140px', render: (row) => <MaintenanceStatusBadge status={row.status} /> },
    {
      key: 'sentAtUtc',
      header: 'Enviado em',
      width: '120px',
      render: (row) => <span className="tabular-nums">{formatDate(row.sentAtUtc)}</span>,
    },
    {
      key: 'completedAtUtc',
      header: 'Concluído em',
      width: '120px',
      align: 'right',
      render: (row) =>
        row.completedAtUtc ? (
          <span className="text-fg-muted tabular-nums">{formatDate(row.completedAtUtc)}</span>
        ) : (
          <span className="text-fg-subtle">—</span>
        ),
    },
  ]

  return (
    <div className="flex flex-col">
      <PageHeader title="Manutenção" description="Equipamento avariado atualmente em reparação." />
      <div className="flex flex-col pb-6 sm:px-3">
        <FilterBar
          hasActiveFilters={Boolean(status)}
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
            aria-label="Filtrar por estado"
            className="w-full sm:w-44"
            value={status}
            onChange={(e) => { setStatus(e.target.value); setPage(1) }}
          >
            <option value="">Todos os estados</option>
            {STATUS_OPTIONS.map((s) => (
              <option key={s} value={s}>
                {maintenanceStatusMeta[s]?.label ?? s}
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
          onRowClick={(row) => setSelectedId(row.id)}
          renderMobileRow={(row) => <MaintenanceMobileRow row={row} onOpen={() => setSelectedId(row.id)} />}
          emptyIcon={<Wrench aria-hidden />}
          emptyTitle="Nenhum registo de manutenção"
          emptyDescription={
            status
              ? 'Nenhum registo corresponde ao filtro aplicado.'
              : 'Equipamento entra em manutenção a partir de uma devolução com avaria.'
          }
          emptyAction={
            status ? (
              <Button variant="secondary" size="sm" onClick={clearFilters}>
                Limpar filtros
              </Button>
            ) : undefined
          }
          pagination={query.data ? { page, pageSize: PAGE_SIZE, totalCount: query.data.totalCount, onPageChange: setPage } : undefined}
        />
      </div>

      <MaintenanceDrawer
        id={selectedId}
        open={Boolean(selectedId)}
        onOpenChange={(open) => !open && setSelectedId(null)}
        canManage={hasPermission(Permissions.EquipmentMaintenance.Manage)}
        canOpenEquipment={hasPermission(Permissions.Equipment.Read)}
      />
    </div>
  )
}

/** Below 768px: asset tag + state, the equipment, then the dates — the whole row is one button that opens the record. */
function MaintenanceMobileRow({ row, onOpen }: { row: MaintenanceSummary; onOpen: () => void }) {
  return (
    <button type="button" onClick={onOpen} className="-mx-4 -my-2.5 flex w-[calc(100%+2rem)] flex-col gap-0.5 px-4 py-2.5 text-left">
      <span className="flex w-full items-center justify-between gap-3">
        <span className="font-mono text-[12.5px] leading-5 font-medium text-fg">{row.assetTag}</span>
        <MaintenanceStatusBadge status={row.status} />
      </span>
      <span className="truncate text-[13px] leading-[18px] text-fg-muted">{equipmentSecondary(row)}</span>
      <span className="text-[13px] leading-[18px] text-fg-muted tabular-nums">
        Enviado {formatDate(row.sentAtUtc)}
        {row.completedAtUtc && ` · Concluído ${formatDate(row.completedAtUtc)}`}
      </span>
    </button>
  )
}

function MaintenanceDrawer({
  id,
  open,
  onOpenChange,
  canManage,
  canOpenEquipment,
}: {
  id: string | null
  open: boolean
  onOpenChange: (open: boolean) => void
  canManage: boolean
  canOpenEquipment: boolean
}) {
  const queryClient = useQueryClient()

  const detailQuery = useQuery({
    queryKey: ['maintenance', 'detail', id],
    queryFn: () => api.getMaintenance(id!),
    enabled: Boolean(id) && open,
  })

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<CompleteFormValues>({ resolver: zodResolver(completeSchema), defaultValues: { outcome: 'Repaired', notes: '' } })

  const completeMutation = useMutation({
    mutationFn: (values: CompleteFormValues) => api.completeMaintenance(id!, values),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['maintenance'] })
      // The outcome moves the equipment (Repaired → Available, NotRepairable → Damaged).
      void queryClient.invalidateQueries({ queryKey: ['equipment'] })
      onOpenChange(false)
    },
    onError: (error) => {
      setError('outcome', { message: error instanceof ApiError ? error.message : 'Não foi possível concluir a manutenção.' })
    },
  })

  const detail = detailQuery.data

  return (
    <Drawer open={open} onOpenChange={onOpenChange} title={detail ? `Manutenção: ${detail.assetTag}` : 'Manutenção'}>
      {detailQuery.isLoading ? (
        <div className="flex flex-col gap-3" aria-busy="true">
          <Skeleton className="h-5 w-40" />
          <Skeleton className="h-24 w-full" />
        </div>
      ) : detailQuery.error || !detail ? (
        <ErrorState error={detailQuery.error} onRetry={() => void detailQuery.refetch()} />
      ) : (
        <div className="flex flex-col">
          <section aria-labelledby="maintenance-equipment-heading" className="flex flex-col gap-3 border-b border-border pb-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h3 id="maintenance-equipment-heading" className="text-[14px] leading-5 font-semibold text-fg">
                Equipamento
              </h3>
              <MaintenanceStatusBadge status={detail.status} />
            </div>
            <p className="text-[13px] leading-5 text-fg">
              {canOpenEquipment ? (
                <Link
                  to={`/equipamentos/${detail.equipmentId}`}
                  className="font-mono text-[12.5px] font-medium underline decoration-border-strong underline-offset-2 hover:decoration-fg"
                >
                  {detail.assetTag}
                </Link>
              ) : (
                <span className="font-mono text-[12.5px] font-medium">{detail.assetTag}</span>
              )}
              <span className="text-fg-muted"> · {detail.equipmentTypeName}</span>
            </p>
          </section>

          <section aria-labelledby="maintenance-record-heading" className="flex flex-col gap-3 border-b border-border py-4">
            <h3 id="maintenance-record-heading" className="text-[14px] leading-5 font-semibold text-fg">
              Registo
            </h3>
            <KeyValueList
              items={[
                { label: 'Enviado em', value: formatDate(detail.sentAtUtc) },
                ...(detail.status === 'Completed'
                  ? [
                      { label: 'Concluído em', value: formatDate(detail.completedAtUtc) },
                      {
                        label: 'Resultado',
                        value: detail.outcome ? (
                          <StatusBadge
                            label={maintenanceOutcomeMeta[detail.outcome]?.label ?? detail.outcome}
                            tone={maintenanceOutcomeMeta[detail.outcome]?.tone ?? 'neutral'}
                          />
                        ) : (
                          '—'
                        ),
                      },
                    ]
                  : []),
              ]}
            />
            <div>
              <p className="text-[12px] leading-4 text-fg-muted">Notas</p>
              <p className="mt-0.5 text-[13px] leading-5 whitespace-pre-wrap text-fg">{detail.notes ?? '—'}</p>
            </div>
          </section>

          {detail.status === 'Active' && canManage && (
            <form
              aria-labelledby="maintenance-complete-heading"
              className="flex flex-col gap-4 pt-4"
              onSubmit={(e) => void handleSubmit((v) => completeMutation.mutate(v))(e)}
            >
              <h3 id="maintenance-complete-heading" className="text-[14px] leading-5 font-semibold text-fg">
                Concluir manutenção
              </h3>
              <FormField
                id="outcome"
                label="Resultado"
                required
                error={errors.outcome?.message}
                helperText="Reparado devolve o equipamento a Disponível. Sem reparação possível devolve-o a Avariado, para eventual abate pela Administração."
              >
                <Select id="outcome" invalid={Boolean(errors.outcome)} {...register('outcome')}>
                  <option value="Repaired">Reparado</option>
                  <option value="NotRepairable">Sem reparação possível</option>
                </Select>
              </FormField>
              <FormField id="maintenance-notes" label="Notas" optional>
                <Textarea id="maintenance-notes" {...register('notes')} />
              </FormField>
              <div className="flex justify-end">
                <Button type="submit" variant="primary" className="max-lg:h-11" loading={completeMutation.isPending}>
                  Concluir manutenção
                </Button>
              </div>
            </form>
          )}
        </div>
      )}
    </Drawer>
  )
}
