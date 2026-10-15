import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router'
import { CheckCircle2, CircleAlert, Pencil, Plus, X } from 'lucide-react'
import { KeyValueList } from '../../shared/components/KeyValueList'
import { DetailSection } from '../../shared/components/DetailSection'
import { DataTable, type DataTableColumn } from '../../shared/components/DataTable'
import { ContentWidth } from '../../shared/components/ContentWidth'
import { StatusBadge, type StatusTone } from '../../shared/components/StatusBadge'
import { Button } from '../../shared/components/Button'
import { IconButton } from '../../shared/components/IconButton'
import { Dialog } from '../../shared/components/Dialog'
import { ConfirmDialog } from '../../shared/components/ConfirmDialog'
import { FormField } from '../../shared/components/FormField'
import { DateField } from '../../shared/components/DateField'
import { Select } from '../../shared/components/Select'
import { Skeleton } from '../../shared/components/Skeleton'
import { ErrorState } from '../../shared/components/ErrorState'
import { ApiError } from '../../shared/api/httpClient'
import {
  activeStatusMeta,
  allocationStatusMeta,
  equipmentStatusMeta,
  workforceTypeLabels,
  engagementTypeLabels,
} from '../../shared/status/statusMaps'
import { formatDate } from '../../shared/utils/format'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import * as api from './api'
import type { ConsultantAllocation, EndAllocationResult } from './types'
import type { EquipmentAssignment } from '../assignments/types'
import { AllocationFormDialog } from './AllocationFormDialog'
import { endAllocationSuccessMessage, hasReturnCounts } from './endAllocationMessage'

export function ConsultantDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { hasPermission } = useAuth()

  const [archiveOpen, setArchiveOpen] = useState(false)
  const [newAllocationOpen, setNewAllocationOpen] = useState(false)
  const [endAllocationOpen, setEndAllocationOpen] = useState(false)
  // Keyed by consultant so the result of one consultant's ending never shows on another's page.
  const [endResult, setEndResult] = useState<EndAllocationResult | null>(null)

  const query = useQuery({
    queryKey: ['consultants', id],
    queryFn: () => api.getConsultant(id!),
    enabled: Boolean(id),
  })

  const archiveMutation = useMutation({
    mutationFn: () => api.setConsultantActive(id!, !query.data!.isActive),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['consultants'] })
      setArchiveOpen(false)
    },
  })

  if (query.isLoading) {
    return (
      <div className="flex flex-col gap-3 px-4 py-6 sm:px-6" aria-busy="true">
        <Skeleton className="h-4 w-40" />
        <Skeleton className="h-7 w-72" />
        <Skeleton className="mt-4 h-32 w-full" />
        <Skeleton className="h-48 w-full" />
      </div>
    )
  }

  if (query.error || !query.data) {
    return <ErrorState error={query.error} onRetry={() => void query.refetch()} />
  }

  const consultant = query.data
  const statusMeta = activeStatusMeta(consultant.isActive)
  const canUpdate = hasPermission(Permissions.Consultant.Update)
  const canArchive = hasPermission(Permissions.Consultant.Archive)
  const canCreateAllocation = hasPermission(Permissions.ConsultantAllocation.Create)
  const canEndAllocation = hasPermission(Permissions.ConsultantAllocation.End)
  const canReadReturns = hasPermission(Permissions.EquipmentReturn.Read)
  const visibleEndResult = endResult?.consultantId === consultant.id ? endResult : null
  const currentAllocation = consultant.currentAllocation
  const showNewAllocation = !currentAllocation && consultant.isActive && canCreateAllocation
  const showEndAllocation = Boolean(currentAllocation) && canEndAllocation && currentAllocation?.status === 'Active'
  const equipmentCount = consultant.currentAssignedEquipment.length

  const allocationColumns: DataTableColumn<ConsultantAllocation>[] = [
    // A minimum width keeps the text columns readable; on narrow screens the table scrolls instead of squashing them.
    {
      key: 'company',
      header: 'Empresa',
      render: (row) => <span className="block min-w-36">{row.companyName ?? <span className="text-fg-subtle">—</span>}</span>,
    },
    { key: 'role', header: 'Função', render: (row) => <span className="block min-w-40">{row.roleOrFunction}</span> },
    { key: 'workforceType', header: 'Regime', width: '88px', render: (row) => workforceTypeLabels[row.workforceType] ?? row.workforceType },
    {
      key: 'engagementType',
      header: 'Vínculo',
      width: '160px',
      render: (row) => <span className="whitespace-nowrap">{engagementTypeLabels[row.engagementType] ?? row.engagementType}</span>,
    },
    { key: 'startDate', header: 'Início', width: '96px', render: (row) => <span className="whitespace-nowrap tabular-nums">{formatDate(row.startDate)}</span> },
    {
      key: 'endDate',
      header: 'Fim',
      width: '96px',
      render: (row) => <span className="whitespace-nowrap text-fg-muted tabular-nums">{formatDate(row.endDate)}</span>,
    },
    {
      key: 'status',
      header: 'Estado',
      width: '112px',
      render: (row) => {
        const meta = allocationStatusMeta[row.status]
        return meta ? <StatusBadge label={meta.label} tone={meta.tone} /> : row.status
      },
    },
  ]

  const currentEquipmentColumns: DataTableColumn<EquipmentAssignment>[] = [
    {
      key: 'assetTag',
      header: 'Identificador',
      width: '136px',
      render: (row) => <span className="font-mono text-[12px] font-medium whitespace-nowrap text-fg">{row.assetTag}</span>,
    },
    { key: 'type', header: 'Tipo', render: (row) => <span className="text-fg-muted">{row.equipmentTypeName}</span> },
    {
      key: 'equipamento',
      header: 'Equipamento',
      render: (row) => {
        const parts = [row.manufacturer, row.model].filter(Boolean)
        return parts.length > 0 ? parts.join(' ') : <span className="text-fg-subtle">—</span>
      },
    },
    {
      key: 'startedAtUtc',
      header: 'Atribuído desde',
      width: '124px',
      render: (row) => <span className="whitespace-nowrap text-fg-muted tabular-nums">{formatDate(row.startedAtUtc)}</span>,
    },
    {
      key: 'status',
      header: 'Estado',
      width: '176px',
      // Custody is still the consultant's while a return is pending, so the live equipment status is shown, not a fixed "Atribuído".
      render: (row) => {
        const meta = equipmentStatusMeta[row.equipmentStatus]
        return meta ? <StatusBadge label={meta.label} tone={meta.tone} icon={meta.icon} /> : row.equipmentStatus
      },
    },
  ]

  return (
    <div className="flex flex-col">
      <header className="flex flex-col gap-2 border-b border-border bg-surface px-4 py-5 sm:px-6">
        <nav aria-label="Localização">
          <ol className="flex flex-wrap items-center gap-1.5 text-[12px] text-fg-muted">
            <li>
              <Link to="/consultores" className="hover:text-fg hover:underline">
                Consultores
              </Link>
            </li>
            <li aria-hidden className="text-border-control">
              /
            </li>
            <li className="min-w-0 truncate text-fg" aria-current="page">
              {consultant.fullName}
            </li>
          </ol>
        </nav>
        <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
          <div className="flex min-w-0 flex-wrap items-center gap-x-3.5 gap-y-1">
            <h1 className="text-xl leading-7 font-semibold tracking-[-0.01em] break-words text-fg">{consultant.fullName}</h1>
            <StatusBadge label={statusMeta.label} tone={statusMeta.tone} />
          </div>
          {(canUpdate || canArchive) && (
            <div className="flex flex-wrap items-center gap-2">
              {canUpdate && (
                <Button
                  variant="secondary"
                  size="sm"
                  className="max-lg:h-11"
                  icon={<Pencil className="size-3.5" aria-hidden />}
                  onClick={() => void navigate(`/consultores/${consultant.id}/editar`)}
                >
                  Editar
                </Button>
              )}
              {canArchive && (
                <Button
                  variant={consultant.isActive ? 'danger-outline' : 'secondary'}
                  size="sm"
                  className="max-lg:h-11"
                  onClick={() => setArchiveOpen(true)}
                >
                  {consultant.isActive ? 'Desativar' : 'Ativar'}
                </Button>
              )}
            </div>
          )}
        </div>
      </header>

      <div className="flex flex-col px-4 pb-6 sm:px-6">
        {visibleEndResult && (
          <div className="mt-5 flex items-start gap-2.5 rounded-md border border-success-border bg-success-bg px-3 py-2.5" role="status">
            <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-success" aria-hidden />
            <div className="flex min-w-0 flex-1 flex-col gap-1">
              <p className="text-[13px] leading-5 font-medium text-success">{endAllocationSuccessMessage(visibleEndResult)}</p>
              {canReadReturns &&
                hasReturnCounts(visibleEndResult) &&
                visibleEndResult.returnsCreatedCount + visibleEndResult.returnsAlreadyPendingCount > 0 && (
                <Link
                  to={
                    visibleEndResult.returnGroupId && visibleEndResult.returnsCreatedCount > 1
                      ? `/devolucoes/grupos/${visibleEndResult.returnGroupId}`
                      : '/devolucoes?status=Pending'
                  }
                  className="w-fit text-[13px] leading-5 font-medium text-fg underline decoration-border-strong underline-offset-2 hover:decoration-fg"
                >
                  Ver devoluções
                </Link>
              )}
            </div>
            <IconButton icon={<X className="size-4" />} label="Fechar" size="sm" onClick={() => setEndResult(null)} />
          </div>
        )}

        {/*
          Key-value groups keep a contained reading width on very wide screens;
          the equipment and history tables below keep the full width.
        */}
        <DetailSection variant="ruled" title="Dados do consultor">
          <ContentWidth variant="compact">
            <KeyValueList
              items={[
                { label: 'NIF', value: <span className="font-mono text-[12.5px]">{consultant.nif}</span> },
                { label: 'E-mail', value: consultant.email },
                { label: 'Telefone', value: consultant.phone },
                { label: 'Morada', value: consultant.address },
              ]}
            />
          </ContentWidth>
        </DetailSection>

        <DetailSection
          variant="ruled"
          title="Alocação atual"
          actions={
            (showNewAllocation || showEndAllocation) && (
              <>
                {showNewAllocation && (
                  <Button
                    variant="secondary"
                    size="sm"
                    className="max-lg:h-11"
                    icon={<Plus className="size-3.5" aria-hidden />}
                    onClick={() => setNewAllocationOpen(true)}
                  >
                    Nova alocação
                  </Button>
                )}
                {showEndAllocation && (
                  <Button variant="secondary" size="sm" className="max-lg:h-11" onClick={() => setEndAllocationOpen(true)}>
                    Encerrar alocação
                  </Button>
                )}
              </>
            )
          }
        >
          {currentAllocation ? (
            <ContentWidth variant="compact">
              <KeyValueList
                columns={3}
                items={[
                  { label: 'Regime', value: workforceTypeLabels[currentAllocation.workforceType] ?? currentAllocation.workforceType },
                  { label: 'Empresa', value: currentAllocation.companyName ?? '—' },
                  {
                    label: 'Vínculo',
                    value: engagementTypeLabels[currentAllocation.engagementType] ?? currentAllocation.engagementType,
                  },
                  { label: 'Função', value: currentAllocation.roleOrFunction },
                  { label: 'Data de início', value: <span className="tabular-nums">{formatDate(currentAllocation.startDate)}</span> },
                  { label: 'Data de fim', value: <span className="tabular-nums">{formatDate(currentAllocation.endDate)}</span> },
                ]}
              />
            </ContentWidth>
          ) : (
            <p className="text-[13px] leading-5 text-fg-muted">Este consultor não tem nenhuma alocação ativa.</p>
          )}
        </DetailSection>

        <DetailSection
          variant="ruled"
          title="Equipamentos atribuídos"
          description={
            equipmentCount === 0 ? undefined : equipmentCount === 1 ? '1 equipamento' : `${equipmentCount} equipamentos`
          }
        >
          {equipmentCount === 0 ? (
            <p className="text-[13px] leading-5 text-fg-muted">Sem equipamentos atribuídos atualmente.</p>
          ) : (
            // Below md the rows carry their own 16px inset; bleed them to the page edge
            // so their content lines up with the other sections (as on Return groups).
            <div className="-mx-4 border-t border-border-divider md:mx-0">
              <DataTable
                columns={currentEquipmentColumns}
                rows={consultant.currentAssignedEquipment}
                rowKey={(row) => row.id}
                getRowHref={(row) => `/equipamentos/${row.equipmentId}`}
                renderMobileRow={(row) => <AssignedEquipmentMobileRow row={row} />}
                emptyTitle="Sem equipamentos atribuídos atualmente."
              />
            </div>
          )}
        </DetailSection>

        <DetailSection variant="ruled" title="Histórico de alocações" className="border-b-0">
          {consultant.allocationHistory.length === 0 ? (
            <p className="text-[13px] leading-5 text-fg-muted">Este consultor ainda não teve nenhuma alocação registada.</p>
          ) : (
            // Comparison-heavy (7 columns): stays a table at every width and scrolls horizontally inside its own box on narrow screens.
            <div className="border-t border-border-divider">
              <DataTable
                columns={allocationColumns}
                rows={consultant.allocationHistory}
                rowKey={(row) => row.id}
                emptyTitle="Sem alocações registadas"
              />
            </div>
          )}
        </DetailSection>
      </div>

      <ConfirmDialog
        open={archiveOpen}
        onOpenChange={setArchiveOpen}
        title={consultant.isActive ? 'Desativar consultor' : 'Ativar consultor'}
        description={
          consultant.isActive
            ? `O consultor "${consultant.fullName}" deixa de estar disponível para novas entregas de equipamento. O histórico existente não é afetado.`
            : `O consultor "${consultant.fullName}" volta a estar disponível para novas entregas de equipamento.`
        }
        confirmLabel={consultant.isActive ? 'Desativar' : 'Ativar'}
        destructive={consultant.isActive}
        loading={archiveMutation.isPending}
        onConfirm={() => archiveMutation.mutate()}
      />

      <AllocationFormDialog open={newAllocationOpen} onOpenChange={setNewAllocationOpen} consultantId={consultant.id} />

      {consultant.currentAllocation && (
        <EndAllocationDialog
          open={endAllocationOpen}
          onOpenChange={setEndAllocationOpen}
          consultantId={consultant.id}
          allocationId={consultant.currentAllocation.id}
          startDate={consultant.currentAllocation.startDate}
          activeEquipment={consultant.currentAssignedEquipment}
          onEnded={setEndResult}
        />
      )}
    </div>
  )
}

/** Below 768px: identifier + live status, then the model, then type and since-date — one link per row to the equipment. */
function AssignedEquipmentMobileRow({ row }: { row: EquipmentAssignment }) {
  const meta = equipmentStatusMeta[row.equipmentStatus]
  const model = [row.manufacturer, row.model].filter(Boolean).join(' ')
  return (
    <span className="flex flex-col gap-0.5">
      <span className="flex items-center justify-between gap-3">
        <span className="font-mono text-[13px] font-medium text-fg">{row.assetTag}</span>
        {meta ? <StatusBadge label={meta.label} tone={meta.tone} icon={meta.icon} /> : row.equipmentStatus}
      </span>
      {model && <span className="truncate text-[14px] leading-5 text-fg">{model}</span>}
      <span className="text-[13px] leading-[18px] text-fg-muted">
        {row.equipmentTypeName} · desde {formatDate(row.startedAtUtc)}
      </span>
    </span>
  )
}

/** What ending the engagement will do with one piece of equipment, from its live status. The server decides; this only previews it. */
function offboardingReturnMeta(equipmentStatus: string): { label: string; tone: StatusTone } {
  if (equipmentStatus === 'Assigned') return { label: 'Devolução será iniciada', tone: 'info' }
  if (equipmentStatus === 'ReturnPending') return { label: 'Devolução já iniciada', tone: 'warning' }
  return { label: 'Requer verificação', tone: 'danger' }
}

/** The collection window is required only for an end of engagement with equipment to collect, so the schema depends on it. */
function buildEndAllocationSchema(hasActiveEquipment: boolean) {
  return z
    .object({
      endDate: z.string().min(1, 'Indique a data de fim'),
      isEndOfEngagement: z.enum(['yes', 'no']),
      expectedCollectionFromDate: z.string().optional(),
      expectedCollectionToDate: z.string().optional(),
    })
    .refine((v) => v.isEndOfEngagement !== 'yes' || !hasActiveEquipment || Boolean(v.expectedCollectionFromDate), {
      message: 'Indique a previsão de recolha',
      path: ['expectedCollectionFromDate'],
    })
}

type EndAllocationFormValues = z.infer<ReturnType<typeof buildEndAllocationSchema>>

interface EndAllocationDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  consultantId: string
  allocationId: string
  startDate: string
  /** The consultant's current custody (Active assignments). An end of engagement initiates the return of each item that has none pending yet. */
  activeEquipment: EquipmentAssignment[]
  onEnded: (result: EndAllocationResult) => void
}

function EndAllocationDialog({ open, onOpenChange, consultantId, allocationId, startDate, activeEquipment, onEnded }: EndAllocationDialogProps) {
  const queryClient = useQueryClient()
  const hasActiveEquipment = activeEquipment.length > 0
  const schema = useMemo(() => buildEndAllocationSchema(hasActiveEquipment), [hasActiveEquipment])

  const {
    register,
    handleSubmit,
    watch,
    reset,
    formState: { errors },
  } = useForm<EndAllocationFormValues>({
    resolver: zodResolver(schema),
    defaultValues: { endDate: '', isEndOfEngagement: 'no', expectedCollectionFromDate: '', expectedCollectionToDate: '' },
  })

  const isEndOfEngagement = watch('isEndOfEngagement') === 'yes'
  const showEquipmentSummary = isEndOfEngagement && hasActiveEquipment

  const mutation = useMutation({
    mutationFn: (values: EndAllocationFormValues) =>
      api.endAllocation(consultantId, allocationId, {
        endDate: values.endDate,
        isEndOfEngagement: values.isEndOfEngagement === 'yes',
        expectedCollectionFromDate: showEquipmentSummary ? values.expectedCollectionFromDate : undefined,
        expectedCollectionToDate: showEquipmentSummary ? values.expectedCollectionToDate || undefined : undefined,
      }),
    onSuccess: (result) => {
      // An end of engagement changes equipment status and creates returns, not just the allocation.
      for (const queryKey of [['consultants'], ['equipment'], ['returns'], ['assignments'], ['dashboard']]) {
        void queryClient.invalidateQueries({ queryKey })
      }
      if (result.isEndOfEngagement && !hasReturnCounts(result)) {
        // The API answered without the return counts: it is not the version this frontend expects
        // (e.g. a backend process that was not restarted after an update). The success message
        // falls back to a generic one rather than inventing numbers.
        console.warn('End-of-engagement response has no return counts; the API may be running an older version.')
      }
      onEnded(result)
      onOpenChange(false)
    },
  })
  const { reset: resetMutation } = mutation

  useEffect(() => {
    if (open) {
      reset({ endDate: '', isEndOfEngagement: 'no', expectedCollectionFromDate: '', expectedCollectionToDate: '' })
      resetMutation()
    }
  }, [open, reset, resetMutation])

  // Shown inside the dialog: anything rendered on the page behind it is hidden by the overlay.
  const serverError = mutation.error
    ? mutation.error instanceof ApiError
      ? mutation.error.message
      : 'Não foi possível encerrar a alocação.'
    : null

  const submit = handleSubmit((values) => {
    if (!mutation.isPending) mutation.mutate(values)
  })

  const willInitiateReturns = showEquipmentSummary && activeEquipment.some((item) => item.equipmentStatus === 'Assigned')
  const confirmLabel = willInitiateReturns ? 'Encerrar vínculo e iniciar devoluções' : isEndOfEngagement ? 'Encerrar vínculo' : 'Encerrar alocação'

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={isEndOfEngagement ? 'Encerrar vínculo' : 'Encerrar alocação'}
      description="A alocação atual é encerrada na data indicada. Esta ação não pode ser revertida."
      size={showEquipmentSummary ? 'md' : 'sm'}
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={mutation.isPending}>
            Cancelar
          </Button>
          <Button variant="danger" loading={mutation.isPending} onClick={() => void submit()}>
            {confirmLabel}
          </Button>
        </>
      }
    >
      <form className="flex flex-col gap-4" onSubmit={(e) => void submit(e)}>
        <FormField id="endDate" label="Data de fim" error={errors.endDate?.message} required helperText={`Início registado: ${formatDate(startDate)}`}>
          <DateField id="endDate" invalid={Boolean(errors.endDate)} {...register('endDate')} />
        </FormField>
        <FormField id="isEndOfEngagement" label="Este encerramento corresponde ao fim do vínculo do consultor?" required>
          <Select id="isEndOfEngagement" {...register('isEndOfEngagement')}>
            <option value="no">Não: o consultor mantém-se, muda de alocação</option>
            <option value="yes">Sim: fim do vínculo</option>
          </Select>
        </FormField>
        {isEndOfEngagement && !hasActiveEquipment && (
          <p className="text-[13px] text-fg-muted">Este consultor não tem equipamento atribuído atualmente. Não é necessário agendar recolha.</p>
        )}
        {showEquipmentSummary && (
          <>
            <section className="flex flex-col gap-2" aria-labelledby="offboarding-equipment-title">
              <h3 id="offboarding-equipment-title" className="text-[13px] font-semibold text-fg">
                Equipamentos atualmente atribuídos
              </h3>
              <p className="text-[13px] text-fg-muted">
                {activeEquipment.length === 1
                  ? 'O consultor possui 1 equipamento que deverá ser devolvido:'
                  : `O consultor possui ${activeEquipment.length} equipamentos que deverão ser devolvidos:`}
              </p>
              <ul className="rounded-md border border-border-divider">
                {activeEquipment.map((item) => {
                  const returnMeta = offboardingReturnMeta(item.equipmentStatus)
                  const descriptor = [item.manufacturer, item.model].filter(Boolean).join(' ') || item.equipmentTypeName
                  return (
                    <li
                      key={item.id}
                      className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 border-b border-border-row px-3 py-2 last:border-b-0"
                    >
                      <div className="flex min-w-0 items-baseline gap-3">
                        <span className="shrink-0 font-mono text-[12px] font-medium text-fg">{item.assetTag}</span>
                        <span className="truncate text-[13px] text-fg-muted">{descriptor}</span>
                      </div>
                      <StatusBadge label={returnMeta.label} tone={returnMeta.tone} />
                    </li>
                  )
                })}
              </ul>
              <p className="text-[13px] text-fg-muted">
                Ao confirmar o fim de vínculo, serão iniciadas automaticamente as devoluções dos equipamentos que ainda não tenham uma devolução
                pendente.
              </p>
            </section>
            <FormField
              id="expectedCollectionFromDate"
              label="Previsão de recolha (a partir de)"
              required
              error={errors.expectedCollectionFromDate?.message}
              helperText="O consultor será notificado da previsão de recolha por e-mail."
            >
              <DateField
                id="expectedCollectionFromDate"
                invalid={Boolean(errors.expectedCollectionFromDate)}
                {...register('expectedCollectionFromDate')}
              />
            </FormField>
            <FormField id="expectedCollectionToDate" label="Até" optional>
              <DateField id="expectedCollectionToDate" {...register('expectedCollectionToDate')} />
            </FormField>
          </>
        )}
        {serverError && (
          <div
            className="flex items-start gap-2.5 rounded-md border border-danger-border bg-danger-bg px-3 py-2.5 text-[13px] leading-[19px] text-danger-fg"
            role="alert"
          >
            <CircleAlert className="mt-px size-4 shrink-0 text-danger" aria-hidden />
            <p className="whitespace-pre-line">{serverError}</p>
          </div>
        )}
      </form>
    </Dialog>
  )
}
