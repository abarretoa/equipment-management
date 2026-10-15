import { useState } from 'react'
import { Link } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Archive } from 'lucide-react'
import { PageHeader } from '../../shared/components/PageHeader'
import { FilterBar } from '../../shared/components/FilterBar'
import { Select } from '../../shared/components/Select'
import { Textarea } from '../../shared/components/Textarea'
import { FormField } from '../../shared/components/FormField'
import { DataTable, type DataTableColumn } from '../../shared/components/DataTable'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { ConfirmDialog } from '../../shared/components/ConfirmDialog'
import { Dialog } from '../../shared/components/Dialog'
import { Button } from '../../shared/components/Button'
import { ApiError } from '../../shared/api/httpClient'
import { formatDateTime } from '../../shared/utils/format'
import { equipmentRetirementRequestStatusMeta, equipmentStatusMeta } from '../../shared/status/statusMaps'
import * as api from './api'
import type { EquipmentRetirementRequest, EquipmentRetirementRequestStatus } from './types'

const PAGE_SIZE = 20
const STATUS_OPTIONS: EquipmentRetirementRequestStatus[] = ['Pending', 'Approved', 'Rejected']

const textLinkClass = 'text-fg underline decoration-border-strong underline-offset-2 hover:decoration-fg'

function RequestStatusBadge({ status }: { status: EquipmentRetirementRequestStatus }) {
  const meta = equipmentRetirementRequestStatusMeta[status]
  return meta ? <StatusBadge label={meta.label} tone={meta.tone} icon={meta.icon} /> : <>{status}</>
}

function EquipmentStatusBadge({ row }: { row: EquipmentRetirementRequest }) {
  const meta = equipmentStatusMeta[row.equipmentStatus]
  return meta ? <StatusBadge label={meta.label} tone={meta.tone} icon={meta.icon} /> : <>{row.equipmentStatus}</>
}

const equipmentDescriptor = (row: EquipmentRetirementRequest) =>
  [row.equipmentTypeName, row.manufacturer, row.model].filter(Boolean).join(' · ')

/** Administration-only review area for HR's equipment retirement requests — approval executes the existing "Abater equipamento" workflow; it never re-implements retirement. */
export function SolicitacoesAbatePage() {
  const [status, setStatus] = useState<EquipmentRetirementRequestStatus | ''>('Pending')
  const [page, setPage] = useState(1)
  const [approveTarget, setApproveTarget] = useState<EquipmentRetirementRequest | null>(null)
  const [rejectTarget, setRejectTarget] = useState<EquipmentRetirementRequest | null>(null)
  const [rejectReason, setRejectReason] = useState('')
  const [actionError, setActionError] = useState<string | null>(null)
  const queryClient = useQueryClient()

  const query = useQuery({
    queryKey: ['equipment-retirement-requests', { status, page }],
    queryFn: () => api.listEquipmentRetirementRequests({ status: status || undefined, page, pageSize: PAGE_SIZE }),
  })

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['equipment-retirement-requests'] })
    void queryClient.invalidateQueries({ queryKey: ['equipment'] })
  }

  const approveMutation = useMutation({
    mutationFn: (id: string) => api.approveEquipmentRetirementRequest(id),
    onSuccess: () => { invalidate(); setApproveTarget(null); setActionError(null) },
    onError: (error) => setActionError(error instanceof ApiError ? error.message : 'Não foi possível abater o equipamento.'),
  })

  const rejectMutation = useMutation({
    mutationFn: () => api.rejectEquipmentRetirementRequest(rejectTarget!.id, rejectReason),
    onSuccess: () => { invalidate(); setRejectTarget(null); setRejectReason(''); setActionError(null) },
    onError: (error) => setActionError(error instanceof ApiError ? error.message : 'Não foi possível rejeitar a solicitação.'),
  })

  function openApprove(row: EquipmentRetirementRequest) {
    setActionError(null)
    setApproveTarget(row)
  }

  function openReject(row: EquipmentRetirementRequest) {
    setActionError(null)
    setRejectTarget(row)
  }

  function clearFilters() {
    setStatus('Pending')
    setPage(1)
  }

  // The decision only exists once a request has been decided; on the default
  // "Pendente" view the column would be all dashes.
  const showDecision = status !== 'Pending'
  const showActions = status === 'Pending' || status === ''

  const columns: DataTableColumn<EquipmentRetirementRequest>[] = [
    {
      key: 'assetTag',
      header: 'Equipamento',
      render: (row) => (
        <span className="flex flex-col">
          <Link to={`/equipamentos/${row.equipmentId}`} className={`font-mono text-[12px] font-medium ${textLinkClass}`}>
            {row.assetTag}
          </Link>
          <span className="text-[12px] text-fg-muted">{equipmentDescriptor(row)}</span>
        </span>
      ),
    },
    { key: 'equipmentStatus', header: 'Estado atual', width: '150px', render: (row) => <EquipmentStatusBadge row={row} /> },
    {
      key: 'requested',
      header: 'Solicitação',
      width: '170px',
      render: (row) => (
        <span className="flex flex-col">
          <span>{row.requestedByDisplayName}</span>
          <span className="text-[12px] text-fg-muted tabular-nums">{formatDateTime(row.requestedAtUtc)}</span>
        </span>
      ),
    },
    {
      key: 'requestReason',
      header: 'Motivo',
      render: (row) => <span className="line-clamp-2 text-fg-muted">{row.requestReason}</span>,
    },
    { key: 'status', header: 'Estado', width: '120px', render: (row) => <RequestStatusBadge status={row.status} /> },
    ...(showDecision
      ? [
          {
            key: 'decision',
            header: 'Decisão',
            width: '200px',
            render: (row: EquipmentRetirementRequest) =>
              row.decidedByDisplayName ? (
                <span className="flex flex-col">
                  <span>{row.decidedByDisplayName}</span>
                  {row.decidedAtUtc && (
                    <span className="text-[12px] text-fg-muted tabular-nums">{formatDateTime(row.decidedAtUtc)}</span>
                  )}
                  {row.decisionReason && <span className="line-clamp-2 text-[12px] text-fg-muted">{row.decisionReason}</span>}
                </span>
              ) : (
                <span className="text-fg-subtle">—</span>
              ),
          },
        ]
      : []),
    ...(showActions
      ? [
          {
            key: 'actions',
            header: '',
            width: '232px',
            align: 'right' as const,
            render: (row: EquipmentRetirementRequest) =>
              row.status === 'Pending' ? (
                <div className="flex justify-end gap-2">
                  <Button variant="danger-outline" size="sm" onClick={() => openApprove(row)}>
                    Abater equipamento
                  </Button>
                  <Button variant="secondary" size="sm" onClick={() => openReject(row)}>
                    Rejeitar solicitação
                  </Button>
                </div>
              ) : null,
          },
        ]
      : []),
  ]

  return (
    <div className="flex flex-col">
      <PageHeader title="Solicitações de abate" description="Pedidos de RH para abater equipamento, aguardando decisão da Administração." />
      <div className="flex flex-col pb-6 sm:px-3">
        <FilterBar
          hasActiveFilters={status !== 'Pending'}
          onClear={clearFilters}
          summary={
            query.data && (
              <span>
                <b className="font-semibold text-fg tabular-nums">{query.data.totalCount}</b>{' '}
                {query.data.totalCount === 1 ? 'solicitação' : 'solicitações'}
              </span>
            )
          }
        >
          <Select
            aria-label="Filtrar por estado"
            className="w-full sm:w-44"
            value={status}
            onChange={(e) => { setStatus(e.target.value as EquipmentRetirementRequestStatus | ''); setPage(1) }}
          >
            <option value="">Todos os estados</option>
            {STATUS_OPTIONS.map((s) => (
              <option key={s} value={s}>
                {equipmentRetirementRequestStatusMeta[s]?.label ?? s}
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
          renderMobileRow={(row) => <RequestMobileRow row={row} onApprove={() => openApprove(row)} onReject={() => openReject(row)} />}
          emptyIcon={<Archive aria-hidden />}
          emptyTitle="Nenhuma solicitação de abate"
          emptyDescription={
            status === 'Pending'
              ? 'Pedidos submetidos pelo RH aparecem aqui para revisão.'
              : 'Nenhuma solicitação corresponde ao filtro aplicado.'
          }
          emptyAction={
            status !== 'Pending' ? (
              <Button variant="secondary" size="sm" onClick={clearFilters}>
                Limpar filtros
              </Button>
            ) : undefined
          }
          pagination={query.data ? { page, pageSize: PAGE_SIZE, totalCount: query.data.totalCount, onPageChange: setPage } : undefined}
        />
      </div>

      <ConfirmDialog
        open={Boolean(approveTarget)}
        onOpenChange={(open) => {
          if (!open) {
            setApproveTarget(null)
            setActionError(null)
          }
        }}
        title="Abater equipamento"
        description={
          approveTarget
            ? `O equipamento "${approveTarget.assetTag}" deixa permanentemente de estar disponível para atribuição. Esta ação não pode ser revertida por esta aplicação.`
            : ''
        }
        confirmLabel="Abater"
        destructive
        loading={approveMutation.isPending}
        error={approveTarget ? actionError : null}
        onConfirm={() => approveMutation.mutate(approveTarget!.id)}
      />

      <Dialog
        open={Boolean(rejectTarget)}
        onOpenChange={(open) => {
          if (!open) {
            setRejectTarget(null)
            setRejectReason('')
            setActionError(null)
          }
        }}
        title="Rejeitar solicitação"
        description={rejectTarget ? `A solicitação de abate para "${rejectTarget.assetTag}" será rejeitada. O equipamento mantém o estado atual.` : ''}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setRejectTarget(null)}>Cancelar</Button>
            <Button
              variant="danger"
              loading={rejectMutation.isPending}
              disabled={!rejectReason.trim()}
              onClick={() => rejectMutation.mutate()}
            >
              Rejeitar
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">
          <FormField id="reject-reason" label="Motivo da rejeição" required>
            <Textarea id="reject-reason" value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} />
          </FormField>
          {actionError && rejectTarget && (
            <p
              className="rounded-md border border-danger-border bg-danger-bg px-3 py-2 text-[13px] leading-[19px] whitespace-pre-line text-danger-fg"
              role="alert"
            >
              {actionError}
            </p>
          )}
        </div>
      </Dialog>
    </div>
  )
}

/** Below 768px: asset tag + request state, the equipment and its current state, the request and its reason, then the decision actions. */
function RequestMobileRow({
  row,
  onApprove,
  onReject,
}: {
  row: EquipmentRetirementRequest
  onApprove: () => void
  onReject: () => void
}) {
  return (
    <div className="flex flex-col gap-1">
      <span className="flex items-center justify-between gap-3">
        <Link to={`/equipamentos/${row.equipmentId}`} className={`font-mono text-[12.5px] leading-5 font-medium ${textLinkClass}`}>
          {row.assetTag}
        </Link>
        <RequestStatusBadge status={row.status} />
      </span>
      <span className="flex flex-wrap items-center gap-x-2 gap-y-1 text-[13px] leading-[18px] text-fg-muted">
        <span className="min-w-0 truncate">{equipmentDescriptor(row)}</span>
        <EquipmentStatusBadge row={row} />
      </span>
      <span className="text-[13px] leading-[18px] text-fg-muted">
        {row.requestedByDisplayName} · <span className="tabular-nums">{formatDateTime(row.requestedAtUtc)}</span>
      </span>
      <span className="line-clamp-3 text-[13px] leading-[18px] text-fg">{row.requestReason}</span>
      {row.decidedByDisplayName && (
        <span className="text-[13px] leading-[18px] text-fg-muted">
          Decidido por {row.decidedByDisplayName}
          {row.decidedAtUtc && <span className="tabular-nums"> · {formatDateTime(row.decidedAtUtc)}</span>}
          {row.decisionReason && ` — ${row.decisionReason}`}
        </span>
      )}
      {row.status === 'Pending' && (
        <span className="mt-1.5 flex flex-wrap gap-2">
          <Button variant="danger-outline" size="touch" onClick={onApprove}>
            Abater equipamento
          </Button>
          <Button variant="secondary" size="touch" onClick={onReject}>
            Rejeitar solicitação
          </Button>
        </span>
      )}
    </div>
  )
}
