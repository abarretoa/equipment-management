import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, Plus, Truck } from 'lucide-react'
import { DetailSection } from '../../shared/components/DetailSection'
import { KeyValueList } from '../../shared/components/KeyValueList'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { StepIndicator, type Step } from '../../shared/components/StepIndicator'
import { Button } from '../../shared/components/Button'
import { DateField } from '../../shared/components/DateField'
import { FormField } from '../../shared/components/FormField'
import { Textarea } from '../../shared/components/Textarea'
import { Dialog } from '../../shared/components/Dialog'
import { ConfirmDialog } from '../../shared/components/ConfirmDialog'
import { Skeleton } from '../../shared/components/Skeleton'
import { ErrorState } from '../../shared/components/ErrorState'
import { ApiError } from '../../shared/api/httpClient'
import { handoverDeliveryMethodLabels, handoverOperationalStatusMeta } from '../../shared/status/statusMaps'
import { formatDate } from '../../shared/utils/format'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import { ResponsibilityTermSection } from '../responsibility-terms/ResponsibilityTermSection'
import { AddItemDialog } from './AddItemDialog'
import { HandoverItemCard } from './HandoverItemCard'
import { DeliveryMethodPicker } from './DeliveryMethodPicker'
import type { HandoverDeliveryMethod } from './types'
import * as api from './api'

/**
 * Operational progression (Iteration 3). "Envio" only exists for
 * a Carrier delivery (or a legacy handover that happens to have a dispatch):
 * an in-person delivery has no shipping event.
 */
function buildSteps(showDispatchStep: boolean): Step[] {
  return [
    { key: 'preparation', label: 'Preparação' },
    ...(showDispatchStep ? [{ key: 'dispatch', label: 'Envio' }] : []),
    { key: 'delivery', label: 'Entrega' },
  ]
}

const CONFIRM_DESCRIPTION = (
  <div className="flex flex-col gap-2 text-[13px] leading-5 text-fg-muted">
    <p>Confirme apenas quando os equipamentos tiverem sido efetivamente entregues ao consultor.</p>
    <p>Ao confirmar, os equipamentos ficarão atribuídos ao consultor e a entrega deixará de poder ser editada.</p>
  </div>
)

const DELETE_DRAFT_DESCRIPTION = (
  <div className="flex flex-col gap-2 text-[13px] leading-5 text-fg-muted">
    <p>Esta entrega em rascunho será eliminada.</p>
    <p>Os equipamentos incluídos continuarão disponíveis para outras entregas.</p>
    <p>Esta ação não pode ser anulada.</p>
  </div>
)

export function HandoverDetailPage() {
  const { id } = useParams<{ id: string }>()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  const [addItemOpen, setAddItemOpen] = useState(false)
  const [finalizeOpen, setFinalizeOpen] = useState(false)
  const [finalizeError, setFinalizeError] = useState<string | null>(null)
  const [deliveredOn, setDeliveredOn] = useState<string | undefined>(undefined)
  const [dateError, setDateError] = useState<string | null>(null)
  const [dispatchOpen, setDispatchOpen] = useState(false)
  const [expectedDeliveryFromDate, setExpectedDeliveryFromDate] = useState('')
  const [expectedDeliveryToDate, setExpectedDeliveryToDate] = useState('')
  const [dispatchError, setDispatchError] = useState<string | null>(null)
  const [methodError, setMethodError] = useState<string | null>(null)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [cancelDispatchOpen, setCancelDispatchOpen] = useState(false)
  const [cancelReason, setCancelReason] = useState('')
  const [cancelDispatchError, setCancelDispatchError] = useState<string | null>(null)

  const query = useQuery({
    queryKey: ['handover', id],
    queryFn: () => api.getHandover(id!),
    enabled: Boolean(id),
  })

  const dateMutation = useMutation({
    // The header PUT replaces DeliveredOn and Notes together: send the current
    // notes back, or saving the date would silently erase them.
    mutationFn: (value: string) => api.updateHandoverHeader(id!, value, query.data?.notes ?? undefined),
    onMutate: () => setDateError(null),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['handover', id] }),
    onError: (e) => setDateError(e instanceof ApiError ? e.message : 'Não foi possível guardar a data.'),
  })

  const methodMutation = useMutation({
    mutationFn: (value: HandoverDeliveryMethod) => api.setHandoverDeliveryMethod(id!, value),
    onMutate: () => setMethodError(null),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['handover', id] }),
    onError: (e) =>
      setMethodError(e instanceof ApiError && e.status < 500 ? e.message : 'Não foi possível guardar o método de entrega.'),
  })

  const finalizeMutation = useMutation({
    mutationFn: () => api.finalizeHandover(id!),
    onMutate: () => setFinalizeError(null),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['handover', id] })
      // Custody moved: equipment/consultant/assignment views must not show stale state.
      void queryClient.invalidateQueries({ queryKey: ['handovers'] })
      void queryClient.invalidateQueries({ queryKey: ['equipment'] })
      void queryClient.invalidateQueries({ queryKey: ['consultants'] })
      void queryClient.invalidateQueries({ queryKey: ['consultant'] })
      void queryClient.invalidateQueries({ queryKey: ['assignments'] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      setFinalizeOpen(false)
    },
    // Expected 4xx business-rule rejections carry a safe pt-PT title from the
    // API; anything else (5xx, network) gets a generic message, never raw text.
    onError: (e) =>
      setFinalizeError(
        e instanceof ApiError && e.status < 500 ? e.message : 'Não foi possível confirmar a entrega. Tente novamente.',
      ),
  })

  const dispatchMutation = useMutation({
    mutationFn: () =>
      api.registerDispatch(id!, {
        expectedDeliveryFromDate,
        expectedDeliveryToDate: expectedDeliveryToDate || undefined,
      }),
    onMutate: () => setDispatchError(null),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['handover', id] })
      setDispatchOpen(false)
      setExpectedDeliveryFromDate('')
      setExpectedDeliveryToDate('')
    },
    onError: (e) =>
      setDispatchError(e instanceof ApiError && e.status < 500 ? e.message : 'Não foi possível registar o envio.'),
  })

  const cancelDispatchMutation = useMutation({
    mutationFn: () => api.cancelDispatch(id!, cancelReason.trim()),
    onMutate: () => setCancelDispatchError(null),
    onSuccess: () => {
      // The items are Available again and a prepared term was superseded.
      void queryClient.invalidateQueries({ queryKey: ['handover', id] })
      void queryClient.invalidateQueries({ queryKey: ['handovers'] })
      void queryClient.invalidateQueries({ queryKey: ['responsibility-terms', id] })
      void queryClient.invalidateQueries({ queryKey: ['equipment'] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      setCancelDispatchOpen(false)
      setCancelReason('')
    },
    onError: (e) =>
      setCancelDispatchError(e instanceof ApiError && e.status < 500 ? e.message : 'Não foi possível anular o envio. Tente novamente.'),
  })

  const deleteMutation = useMutation({
    mutationFn: () => api.deleteHandoverDraft(id!),
    onMutate: () => setDeleteError(null),
    onSuccess: () => {
      // The Draft no longer exists: drop its cached detail (a refetch would
      // only 404) and refresh every list it could still appear in.
      queryClient.removeQueries({ queryKey: ['handover', id] })
      void queryClient.invalidateQueries({ queryKey: ['handovers'] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      setDeleteOpen(false)
      void navigate('/entregas')
    },
    onError: (e) =>
      setDeleteError(e instanceof ApiError && e.status < 500 ? e.message : 'Não foi possível eliminar o rascunho. Tente novamente.'),
  })

  if (query.isLoading) {
    return (
      <div className="flex flex-col gap-3 px-4 py-6 sm:px-6" aria-busy="true">
        <Skeleton className="h-7 w-72" />
        <Skeleton className="h-64 w-full" />
      </div>
    )
  }

  if (query.error || !query.data) {
    return (
      <div className="px-4 py-6 sm:px-6">
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </div>
    )
  }

  const handover = query.data
  const isDraft = handover.status === 'Draft'
  const hasItems = handover.items.length > 0
  const allInspected = hasItems && handover.items.every((item) => item.inspection)
  const hasDeliveryDate = Boolean(handover.deliveredOn)
  const canUpdate = hasPermission(Permissions.EquipmentHandover.Update)
  const canFinalize = hasPermission(Permissions.Equipment.Assign)
  const canDispatch = hasPermission(Permissions.EquipmentHandover.Dispatch)
  const canOpenEquipment = hasPermission(Permissions.Equipment.Read)
  const isDispatched = Boolean(handover.dispatch)
  const canEditComposition = isDraft && canUpdate && !isDispatched
  const deliveryMethod = handover.deliveryMethod ?? null
  const isCarrier = deliveryMethod === 'Carrier'
  // Carrier before dispatch: "Registar envio" is the one next step; delivery
  // cannot be confirmed yet (the backend enforces this too).
  const awaitingDispatch = isDraft && isCarrier && !isDispatched
  const canEditMethod = isDraft && canUpdate && !isDispatched
  // Only before the operational lock: dispatch (carrier) or confirmation. The
  // server re-checks, so a stale page can never delete a locked handover.
  const canDeleteDraft = isDraft && canUpdate && !isDispatched
  // "Anular envio": only an active carrier dispatch, before delivery is
  // confirmed. The server re-checks every precondition.
  const canCancelDispatch =
    isDraft && isCarrier && isDispatched && hasPermission(Permissions.EquipmentHandover.CancelDispatch)
  const cancelledDispatches = handover.cancelledDispatches ?? []
  // Carrier: the term can be prepared once the dispatch locks the composition;
  // in person, only after "Confirmar entrega" (as before).
  const showTermSection = !isDraft || (isCarrier && isDispatched)

  // A Draft progresses only with items still free for it: Available before
  // "Registar envio", InTransit after it. The same item may sit in another
  // Draft that dispatched or delivered it first; it stays listed here, marked,
  // and the next step is blocked (the server enforces this too).
  const progressableStatus = isDispatched ? 'InTransit' : 'Available'
  const unavailableItemIds = new Set(
    isDraft
      ? handover.items
          .filter((item) => item.equipmentStatus && item.equipmentStatus !== progressableStatus)
          .map((item) => item.id)
      : [],
  )
  const unavailableBlocker =
    unavailableItemIds.size > 0 ? 'Há equipamentos que já não estão disponíveis para esta entrega.' : null

  const preparationDone = !isDraft || allInspected
  const steps = buildSteps(isCarrier || isDispatched)
  const completedKeys = [
    preparationDone && 'preparation',
    isDispatched && 'dispatch',
    !isDraft && 'delivery',
  ].filter((v): v is string => Boolean(v))
  const currentKey = !preparationDone ? 'preparation' : awaitingDispatch ? 'dispatch' : 'delivery'

  const confirmBlocker = !hasItems
    ? 'Adicione pelo menos um equipamento.'
    : unavailableBlocker
      ? unavailableBlocker
      : !allInspected
      ? 'Todos os itens precisam de inspeção registada.'
      : !hasDeliveryDate
        ? 'Defina a data de entrega.'
        : null

  const showNextAction = isDraft && (canFinalize || ((!deliveryMethod || awaitingDispatch) && canDispatch))
  const statusMeta = handoverOperationalStatusMeta[handover.operationalStatus]
  const itemCount = handover.items.length
  const subtitle = [
    deliveryMethod ? handoverDeliveryMethodLabels[deliveryMethod] : 'Método de entrega não registado',
    itemCount === 1 ? '1 equipamento' : `${itemCount} equipamentos`,
  ].join(' · ')

  return (
    <div className="flex flex-col">
      <header className="flex flex-col gap-2 border-b border-border bg-surface px-4 py-5 sm:px-6">
        <nav aria-label="Localização">
          <ol className="flex flex-wrap items-center gap-1.5 text-[12px] text-fg-muted">
            <li>
              <Link to="/entregas" className="hover:text-fg hover:underline">
                Entregas
              </Link>
            </li>
            <li aria-hidden className="text-border-control">
              /
            </li>
            <li className="min-w-0 truncate text-fg" aria-current="page">
              {handover.consultantFullName}
            </li>
          </ol>
        </nav>
        <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
          <div className="flex min-w-0 flex-wrap items-center gap-x-3.5 gap-y-1">
            <h1 className="text-xl leading-7 font-semibold tracking-[-0.01em] break-words text-fg">
              Entrega: {handover.consultantFullName}
            </h1>
            <StatusBadge
              label={statusMeta?.label ?? handover.operationalStatus}
              tone={statusMeta?.tone ?? 'neutral'}
              icon={statusMeta?.icon}
            />
          </div>
          {canDeleteDraft && (
            <Button variant="danger-outline" size="sm" className="max-lg:h-11" onClick={() => setDeleteOpen(true)}>
              Eliminar rascunho
            </Button>
          )}
        </div>
        <p className="text-[13px] leading-5 text-fg-muted">{subtitle}</p>
      </header>

      <div className="border-b border-border px-4 py-3.5 sm:px-6">
        <StepIndicator steps={steps} currentKey={currentKey} completedKeys={completedKeys} />
      </div>

      {/* ≥1280px: ruled main column + 336px side panel. Below, the panel stacks first, so the next step is never buried. */}
      <div className="grid grid-cols-1 gap-x-6 px-4 pb-6 sm:px-6 xl:grid-cols-[minmax(0,1fr)_336px]">
        <aside aria-label="Próximo passo" className="flex flex-col gap-4 pt-5 xl:col-start-2 xl:row-start-1">
          <section aria-labelledby="handover-next-heading" className="rounded-lg border border-border bg-surface">
            <h2
              id="handover-next-heading"
              className="border-b border-border px-4 py-3 text-[12px] leading-4 font-medium text-fg-muted"
            >
              {isDraft ? 'Próximo passo' : 'Estado da entrega'}
            </h2>
            <div className="flex flex-col items-start gap-2.5 px-4 py-3.5">
              {!isDraft ? (
                handover.operationalStatus === 'AwaitingSignedTerm' ? (
                  <p className="rounded-md border border-warning-border bg-warning-bg px-3 py-2 text-[13px] leading-5 text-warning">
                    Entrega efetuada. A aguardar submissão do termo de responsabilidade assinado.
                  </p>
                ) : (
                  <p className="text-[13px] leading-5 text-fg">
                    Entrega concluída. O termo de responsabilidade assinado está registado.
                  </p>
                )
              ) : !showNextAction ? (
                <p className="text-[13px] leading-5 text-fg-muted">Rascunho em preparação.</p>
              ) : !deliveryMethod ? (
                <p className="text-[13px] leading-5 text-fg-muted">Selecione o método de entrega para continuar.</p>
              ) : awaitingDispatch ? (
                <>
                  {canDispatch && (
                    <Button
                      variant="primary"
                      className="max-lg:h-11"
                      icon={<Truck className="size-4" aria-hidden />}
                      disabled={!hasItems || unavailableBlocker !== null}
                      onClick={() => setDispatchOpen(true)}
                    >
                      Registar envio
                    </Button>
                  )}
                  <p className="text-[12.5px] leading-[18px] text-fg-muted">
                    {!hasItems
                      ? 'Adicione pelo menos um equipamento para poder registar o envio.'
                      : (unavailableBlocker ?? 'Registe o envio antes de confirmar a entrega.')}
                  </p>
                </>
              ) : (
                canFinalize && (
                  <>
                    <Button
                      variant="primary"
                      className="max-lg:h-11"
                      icon={<CheckCircle2 className="size-4" aria-hidden />}
                      disabled={confirmBlocker !== null}
                      onClick={() => setFinalizeOpen(true)}
                    >
                      Confirmar entrega
                    </Button>
                    {confirmBlocker && <p className="text-[12.5px] leading-[18px] text-fg-muted">{confirmBlocker}</p>}
                  </>
                )
              )}
              {handover.finalizedAtUtc && (
                <p className="text-[12.5px] leading-[18px] text-fg-muted">
                  Entregue e atribuído ao consultor em{' '}
                  <span className="text-fg tabular-nums">{formatDate(handover.finalizedAtUtc)}</span>.
                </p>
              )}
            </div>
          </section>
        </aside>

        <div className="min-w-0 xl:col-start-1 xl:row-start-1">
          <DetailSection
            variant="ruled"
            title="Equipamentos"
            description={
              isDraft
                ? isDispatched
                  ? 'O envio já foi registado. A composição desta entrega (consultor, equipamento e método de entrega) já não pode ser alterada.'
                  : 'Adicione o equipamento disponível e registe a inspeção de entrega de cada item.'
                : undefined
            }
            actions={
              canEditComposition && (
                <Button
                  size="sm"
                  variant="secondary"
                  className="max-lg:h-11"
                  icon={<Plus className="size-4" aria-hidden />}
                  onClick={() => setAddItemOpen(true)}
                >
                  Adicionar equipamento
                </Button>
              )
            }
          >
            {handover.items.length === 0 ? (
              <p className="text-[13px] text-fg-muted">Nenhum equipamento adicionado ainda.</p>
            ) : (
              <ul aria-label="Equipamentos da entrega" className="flex flex-col">
                {handover.items.map((item) => (
                  <HandoverItemCard
                    key={item.id}
                    handoverId={handover.id}
                    item={item}
                    editable={isDraft && canUpdate}
                    canRemove={canEditComposition}
                    canOpenEquipment={canOpenEquipment}
                    unavailable={unavailableItemIds.has(item.id)}
                  />
                ))}
              </ul>
            )}
            {isDraft && (
              <p className="mt-3 text-[12px] leading-4 text-fg-muted">
                Portáteis e telemóveis só podem ser entregues com as 3 fotografias atuais registadas no próprio equipamento.
              </p>
            )}
          </DetailSection>

          {showTermSection && <ResponsibilityTermSection handoverId={handover.id} deliveryConfirmed={!isDraft} />}

          <DetailSection
            variant="ruled"
            title="Método de entrega"
            description={
              canEditMethod
                ? deliveryMethod
                  ? 'Pode ser alterado até o envio ser registado.'
                  : 'Esta entrega foi criada antes do registo do método de entrega. Selecione o método para poder continuar.'
                : isDraft && isDispatched
                  ? 'O envio já foi registado. O método de entrega já não pode ser alterado.'
                  : undefined
            }
          >
            {canEditMethod ? (
              <DeliveryMethodPicker
                value={deliveryMethod}
                onChange={(value) => value !== deliveryMethod && methodMutation.mutate(value)}
                disabled={methodMutation.isPending}
              />
            ) : (
              <p className="text-[13px] text-fg">
                {deliveryMethod ? handoverDeliveryMethodLabels[deliveryMethod] : 'Não registado'}
              </p>
            )}
            {methodError && (
              <p className="mt-2 text-[12.5px] text-danger" role="alert">
                {methodError}
              </p>
            )}
          </DetailSection>

          {(isCarrier || isDispatched || cancelledDispatches.length > 0) && (
            <DetailSection
              variant="ruled"
              title="Envio"
              actions={
                canCancelDispatch && (
                  <Button
                    variant="danger-outline"
                    size="sm"
                    className="max-lg:h-11"
                    onClick={() => setCancelDispatchOpen(true)}
                  >
                    Anular envio
                  </Button>
                )
              }
            >
              {isDispatched ? (
                <KeyValueList
                  columns={3}
                  items={[
                    { label: 'Registado por', value: handover.dispatch!.dispatchedByDisplayNameSnapshot },
                    { label: 'Registado em', value: formatDate(handover.dispatch!.dispatchedAtUtc) },
                    {
                      label: 'Previsão de entrega',
                      value:
                        handover.dispatch!.expectedDeliveryToDate &&
                        handover.dispatch!.expectedDeliveryToDate !== handover.dispatch!.expectedDeliveryFromDate
                          ? `${formatDate(handover.dispatch!.expectedDeliveryFromDate)} a ${formatDate(handover.dispatch!.expectedDeliveryToDate)}`
                          : formatDate(handover.dispatch!.expectedDeliveryFromDate),
                    },
                  ]}
                />
              ) : isDraft ? (
                <p className="text-[13px] text-fg-muted">
                  Pendente. Ao registar o envio, o consultor recebe um email com a previsão de entrega.
                </p>
              ) : null}
              {cancelledDispatches.length > 0 && (
                <ul aria-label="Envios anulados" className="mt-3 flex flex-col gap-2 border-t border-border pt-3">
                  {cancelledDispatches.map((d) => (
                    <li key={d.id} className="text-[12.5px] leading-[18px] text-fg-muted">
                      <span className="font-medium text-fg">Envio anulado</span> em{' '}
                      <span className="tabular-nums">{formatDate(d.cancelledAtUtc)}</span> por {d.cancelledByDisplayNameSnapshot}{' '}
                      (registado em <span className="tabular-nums">{formatDate(d.dispatchedAtUtc)}</span> por{' '}
                      {d.dispatchedByDisplayNameSnapshot}).
                      <span className="block whitespace-pre-wrap break-words text-fg">Motivo: {d.cancellationReason}</span>
                    </li>
                  ))}
                </ul>
              )}
            </DetailSection>
          )}

          <DetailSection variant="ruled" title="Data de entrega">
            {isDraft && canUpdate ? (
              <div className="flex flex-wrap items-end gap-3">
                <FormField id="delivered-on" label="Data" className="w-full max-w-[200px]">
                  <DateField
                    id="delivered-on"
                    max={new Date().toISOString().slice(0, 10)}
                    defaultValue={handover.deliveredOn ?? ''}
                    onChange={(e) => setDeliveredOn(e.target.value)}
                  />
                </FormField>
                <Button
                  variant="secondary"
                  className="max-lg:h-11"
                  loading={dateMutation.isPending}
                  disabled={!deliveredOn}
                  onClick={() => deliveredOn && dateMutation.mutate(deliveredOn)}
                >
                  Guardar data
                </Button>
              </div>
            ) : (
              <p className="text-[13px] text-fg tabular-nums">
                {handover.deliveredOn ? formatDate(handover.deliveredOn) : 'Data não definida.'}
              </p>
            )}
            {dateError && (
              <p className="mt-2 text-[12.5px] text-danger" role="alert">
                {dateError}
              </p>
            )}
          </DetailSection>

          <DetailSection variant="ruled" title="Detalhes">
            <KeyValueList
              columns={3}
              items={[
                { label: 'Criada por', value: handover.createdByDisplayNameSnapshot },
                { label: 'Criada em', value: formatDate(handover.createdAtUtc) },
                { label: 'Entrega confirmada em', value: handover.finalizedAtUtc ? formatDate(handover.finalizedAtUtc) : '—' },
              ]}
            />
            {handover.notes && (
              <div className="mt-3.5">
                <p className="text-[12px] leading-4 text-fg-muted">Notas</p>
                <p className="mt-0.5 text-[13px] leading-5 whitespace-pre-wrap text-fg">{handover.notes}</p>
              </div>
            )}
          </DetailSection>
        </div>
      </div>

      <AddItemDialog handoverId={handover.id} open={addItemOpen} onOpenChange={setAddItemOpen} />

      <Dialog
        open={dispatchOpen}
        onOpenChange={(open) => { setDispatchOpen(open); if (!open) setDispatchError(null) }}
        title="Registar envio"
        description="O consultor recebe um email com a previsão de entrega. Após o registo, o consultor, o equipamento e o método de entrega deixam de poder ser alterados."
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setDispatchOpen(false)}>Cancelar</Button>
            <Button
              variant="primary"
              loading={dispatchMutation.isPending}
              disabled={!expectedDeliveryFromDate}
              onClick={() => dispatchMutation.mutate()}
            >
              Registar envio
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <FormField id="expected-delivery-from" label="Previsão de entrega (a partir de)" required>
            <DateField
              id="expected-delivery-from"
              value={expectedDeliveryFromDate}
              onChange={(e) => setExpectedDeliveryFromDate(e.target.value)}
            />
          </FormField>
          <FormField id="expected-delivery-to" label="Até" optional>
            <DateField
              id="expected-delivery-to"
              min={expectedDeliveryFromDate || undefined}
              value={expectedDeliveryToDate}
              onChange={(e) => setExpectedDeliveryToDate(e.target.value)}
            />
          </FormField>
          {dispatchError && (
            <p
              className="rounded-md border border-danger-border bg-danger-bg px-3 py-2 text-[13px] leading-[19px] whitespace-pre-line text-danger-fg"
              role="alert"
            >
              {dispatchError}
            </p>
          )}
        </div>
      </Dialog>

      <Dialog
        open={cancelDispatchOpen}
        onOpenChange={(open) => {
          setCancelDispatchOpen(open)
          if (!open) {
            setCancelReason('')
            setCancelDispatchError(null)
          }
        }}
        title="Anular envio?"
        description={
          itemCount === 1
            ? 'O equipamento voltará a ficar disponível e poderá ser utilizado noutra entrega. A anulação ficará registada no histórico.'
            : 'Os equipamentos voltarão a ficar disponíveis e poderão ser utilizados noutra entrega. A anulação ficará registada no histórico.'
        }
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setCancelDispatchOpen(false)} disabled={cancelDispatchMutation.isPending}>
              Voltar
            </Button>
            <Button
              variant="danger"
              loading={cancelDispatchMutation.isPending}
              disabled={!cancelReason.trim()}
              onClick={() => cancelDispatchMutation.mutate()}
            >
              Anular envio
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">
          <FormField id="cancel-dispatch-reason" label="Motivo da anulação" required>
            <Textarea
              id="cancel-dispatch-reason"
              value={cancelReason}
              maxLength={500}
              onChange={(e) => setCancelReason(e.target.value)}
            />
          </FormField>
          {cancelDispatchError && (
            <p
              className="rounded-md border border-danger-border bg-danger-bg px-3 py-2 text-[13px] leading-[19px] whitespace-pre-line text-danger-fg"
              role="alert"
            >
              {cancelDispatchError}
            </p>
          )}
        </div>
      </Dialog>

      <ConfirmDialog
        open={finalizeOpen}
        onOpenChange={(open) => {
          setFinalizeOpen(open)
          if (!open) setFinalizeError(null)
        }}
        title="Confirmar entrega"
        description={CONFIRM_DESCRIPTION}
        confirmLabel="Confirmar entrega"
        loading={finalizeMutation.isPending}
        error={finalizeError}
        onConfirm={() => finalizeMutation.mutate()}
      />

      <ConfirmDialog
        open={deleteOpen}
        onOpenChange={(open) => {
          setDeleteOpen(open)
          if (!open) setDeleteError(null)
        }}
        title="Eliminar rascunho?"
        description={DELETE_DRAFT_DESCRIPTION}
        confirmLabel="Eliminar rascunho"
        destructive
        loading={deleteMutation.isPending}
        error={deleteError}
        onConfirm={() => deleteMutation.mutate()}
      />
    </div>
  )
}
