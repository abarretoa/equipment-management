import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { format } from 'date-fns'
import { DetailSection } from '../../shared/components/DetailSection'
import { KeyValueList } from '../../shared/components/KeyValueList'
import { StepIndicator, type Step } from '../../shared/components/StepIndicator'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { Button } from '../../shared/components/Button'
import { Dialog } from '../../shared/components/Dialog'
import { ConfirmDialog } from '../../shared/components/ConfirmDialog'
import { FormField } from '../../shared/components/FormField'
import { DateField } from '../../shared/components/DateField'
import { Select } from '../../shared/components/Select'
import { Textarea } from '../../shared/components/Textarea'
import { Checkbox } from '../../shared/components/Checkbox'
import { Skeleton } from '../../shared/components/Skeleton'
import { ErrorState } from '../../shared/components/ErrorState'
import { ApiError } from '../../shared/api/httpClient'
import { formatDate, formatDateTime } from '../../shared/utils/format'
import { returnStatusMeta, damageStatusMeta, equipmentConditionMeta } from '../../shared/status/statusMaps'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import * as usersApi from '../users/api'
import * as api from './api'
import type { EquipmentCondition } from '../handovers/types'
import { PhotoGallery } from './PhotoGallery'
import { getEquipment } from '../equipment/api'
import { ConditionPhotoPicker } from '../equipment/ConditionPhotoPicker'

const REQUIRED_RECEIVE_PHOTO_COUNT = 3

const STEPS: Step[] = [
  { key: 'pending', label: 'Pendente' },
  { key: 'received', label: 'Recebida' },
  { key: 'inspection', label: 'Inspeção' },
  { key: 'completed', label: 'Finalizada' },
]

const textLinkClass = 'text-fg underline decoration-border-strong underline-offset-2 hover:decoration-fg'

const receiveSchema = z.object({
  receivedOn: z.string().min(1, 'Indique a data de receção'),
  receivedByUserId: z.string().min(1, 'Indique quem recebeu o equipamento'),
})
type ReceiveFormValues = z.infer<typeof receiveSchema>

const cancelSchema = z.object({ reason: z.string().optional() })
type CancelFormValues = z.infer<typeof cancelSchema>

const inspectionSchema = z
  .object({
    condition: z.enum(['Excellent', 'Good', 'Fair', 'Poor']),
    notes: z.string().optional(),
    hasDamage: z.boolean(),
    damageDescription: z.string().optional(),
  })
  .refine((v) => !v.hasDamage || Boolean(v.damageDescription?.trim()), {
    message: 'Descreva a avaria encontrada',
    path: ['damageDescription'],
  })
type InspectionFormValues = z.infer<typeof inspectionSchema>

export function ReturnDetailPage() {
  const { id } = useParams<{ id: string }>()
  const { user, hasPermission } = useAuth()
  const queryClient = useQueryClient()

  const detailKey = ['returns', id]
  const query = useQuery({ queryKey: detailKey, queryFn: () => api.getReturn(id!), enabled: Boolean(id) })

  // Never decided by equipmentTypeName (a display string) — the same real
  // Laptop/MobilePhone discriminator used everywhere else in this app.
  const equipmentQuery = useQuery({
    queryKey: ['equipment', query.data?.equipmentId],
    queryFn: () => getEquipment(query.data!.equipmentId),
    enabled: Boolean(query.data?.equipmentId),
  })
  const requiresReceivePhotos = Boolean(equipmentQuery.data?.laptopSpecification || equipmentQuery.data?.mobilePhoneSpecification)

  const [receiveOpen, setReceiveOpen] = useState(false)
  const [cancelOpen, setCancelOpen] = useState(false)
  const [finalizeOpen, setFinalizeOpen] = useState(false)
  const [finalizeError, setFinalizeError] = useState<string | null>(null)
  const [receivePhotos, setReceivePhotos] = useState<File[]>([])
  const [receivePhotosError, setReceivePhotosError] = useState<string | undefined>()

  const canReturn = hasPermission(Permissions.Equipment.Return)
  const canUpdateInspection = hasPermission(Permissions.EquipmentReturn.Update)
  const canPickReceiver = hasPermission(Permissions.User.Read)
  const canReadEquipment = hasPermission(Permissions.Equipment.Read)
  const canReadConsultants = hasPermission(Permissions.Consultant.Read)

  // Only active users can be recorded as the receiver (the backend rejects any other).
  const usersQuery = useQuery({
    queryKey: ['users', 'picker', 'active'],
    queryFn: () => usersApi.listUsers({ isActive: true, page: 1, pageSize: 100 }),
    enabled: canPickReceiver && receiveOpen,
  })

  // The signed-in user is always offered, from the authenticated identity itself: the default
  // must never depend on the users list having loaded yet, on its order, or on which page of
  // it the current user happens to be in.
  const otherReceivers = (usersQuery.data?.items ?? []).filter((u) => u.id !== user?.id)
  const receiverOptions = user
    ? [{ id: user.id, firstName: user.firstName, lastName: user.lastName, email: user.email }, ...otherReceivers]
    : otherReceivers

  const currentUserId = user?.id ?? ''
  const receiveForm = useForm<ReceiveFormValues>({
    resolver: zodResolver(receiveSchema),
    defaultValues: { receivedOn: format(new Date(), 'yyyy-MM-dd'), receivedByUserId: currentUserId },
  })

  // Each time the dialog opens it is a fresh receive: today's date and the signed-in user as
  // receiver. Keyed on the user's id, not the user object, so a background refresh of the
  // session while the dialog is open cannot discard a receiver chosen by hand.
  useEffect(() => {
    if (receiveOpen) {
      receiveForm.reset({ receivedOn: format(new Date(), 'yyyy-MM-dd'), receivedByUserId: currentUserId })
      setReceivePhotos([])
      setReceivePhotosError(undefined)
    }
  }, [receiveOpen, currentUserId, receiveForm])

  const receiveMutation = useMutation({
    mutationFn: (values: ReceiveFormValues) => api.receiveReturn(id!, { ...values, photos: requiresReceivePhotos ? receivePhotos : undefined }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: detailKey })
      void queryClient.invalidateQueries({ queryKey: ['returns'] })
      // Receipt moves the equipment to UnderInspection and, for a Laptop/MobilePhone,
      // makes these photos its current set: its own views must not show the old state.
      void queryClient.invalidateQueries({ queryKey: ['equipment'] })
      setReceiveOpen(false)
    },
    onError: (error) => {
      receiveForm.setError('receivedOn', {
        message: error instanceof ApiError ? error.message : 'Não foi possível registar a receção.',
      })
    },
  })

  function submitReceive(values: ReceiveFormValues) {
    if (requiresReceivePhotos && receivePhotos.length !== REQUIRED_RECEIVE_PHOTO_COUNT) {
      setReceivePhotosError(
        `São exigidas exatamente ${REQUIRED_RECEIVE_PHOTO_COUNT} fotografias do estado do equipamento (${receivePhotos.length}/${REQUIRED_RECEIVE_PHOTO_COUNT} selecionadas).`,
      )
      return
    }
    setReceivePhotosError(undefined)
    receiveMutation.mutate(values)
  }

  const cancelForm = useForm<CancelFormValues>({ resolver: zodResolver(cancelSchema), defaultValues: { reason: '' } })

  const cancelMutation = useMutation({
    mutationFn: (values: CancelFormValues) => api.cancelReturn(id!, values.reason || undefined),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: detailKey })
      void queryClient.invalidateQueries({ queryKey: ['returns'] })
      void queryClient.invalidateQueries({ queryKey: ['equipment'] })
      setCancelOpen(false)
    },
  })

  const cancelError = cancelMutation.error
    ? cancelMutation.error instanceof ApiError && cancelMutation.error.status < 500
      ? cancelMutation.error.message
      : 'Não foi possível cancelar a devolução.'
    : null

  const ret = query.data

  const inspectionForm = useForm<InspectionFormValues>({
    resolver: zodResolver(inspectionSchema),
    defaultValues: { condition: 'Good', notes: '', hasDamage: false, damageDescription: '' },
  })

  useEffect(() => {
    if (ret?.inspection) {
      inspectionForm.reset({
        condition: ret.inspection.condition,
        notes: ret.inspection.notes ?? '',
        hasDamage: ret.inspection.hasDamage,
        damageDescription: ret.inspection.damageDescription ?? '',
      })
    }
  }, [ret?.inspection, inspectionForm])

  const hasDamage = inspectionForm.watch('hasDamage')

  const inspectionMutation = useMutation({
    mutationFn: (values: InspectionFormValues) =>
      api.updateReturnInspection(id!, {
        condition: values.condition,
        notes: values.notes || undefined,
        hasDamage: values.hasDamage,
        damageDescription: values.hasDamage ? values.damageDescription : undefined,
      }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: detailKey }),
    onError: (error) => {
      inspectionForm.setError('condition', {
        message: error instanceof ApiError ? error.message : 'Não foi possível guardar a inspeção.',
      })
    },
  })

  const finalizeMutation = useMutation({
    mutationFn: () => api.finalizeReturnInspection(id!),
    onMutate: () => setFinalizeError(null),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: detailKey })
      void queryClient.invalidateQueries({ queryKey: ['returns'] })
      void queryClient.invalidateQueries({ queryKey: ['equipment'] })
      setFinalizeOpen(false)
      setFinalizeError(null)
    },
    onError: (error) => {
      setFinalizeError(error instanceof ApiError ? error.message : 'Não foi possível finalizar a inspeção.')
    },
  })

  if (query.isLoading) {
    return (
      <div className="flex flex-col gap-3 px-4 py-6 sm:px-6" aria-busy="true">
        <Skeleton className="h-7 w-64" />
        <Skeleton className="h-40 w-full" />
      </div>
    )
  }

  if (query.error || !ret) {
    return (
      <div className="px-4 py-6 sm:px-6">
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </div>
    )
  }

  const statusMeta = returnStatusMeta[ret.status]
  const hasInspectionStarted = Boolean(ret.inspection)
  const currentStepKey =
    ret.status === 'Pending'
      ? 'pending'
      : ret.status === 'Received'
        ? hasInspectionStarted
          ? 'inspection'
          : 'received'
        : ret.status === 'Completed'
          ? 'completed'
          : 'pending'
  const completedKeys: string[] =
    ret.status === 'Pending'
      ? []
      : ret.status === 'Received'
        ? hasInspectionStarted
          ? ['pending', 'received']
          : ['pending']
        : ret.status === 'Completed'
          ? ['pending', 'received', 'inspection']
          : []
  const inGroup = Boolean(ret.returnGroupId && ret.returnGroupSize > 1)
  const isOpen = ret.status === 'Pending' || ret.status === 'Received'
  const nextStep =
    ret.status === 'Pending'
      ? 'Aguarda a receção física do equipamento. A responsabilidade do consultor só termina na receção.'
      : ret.status === 'Received'
        ? 'Equipamento recebido e em inspeção. Registe a condição e finalize a inspeção.'
        : ret.status === 'Completed'
          ? ret.inspection?.damage
            ? 'Inspeção finalizada com avaria registada.'
            : 'Inspeção finalizada sem avaria.'
          : 'Devolução cancelada.'

  return (
    <div className="flex flex-col">
      <header className="flex flex-col gap-2 border-b border-border bg-surface px-4 py-5 sm:px-6">
        <nav aria-label="Localização">
          <ol className="flex flex-wrap items-center gap-1.5 text-[12px] text-fg-muted">
            <li>
              <Link to="/devolucoes" className="hover:text-fg hover:underline">
                Devoluções
              </Link>
            </li>
            {/* Part of a multi-item collection: one step back is that collection, not the whole list. */}
            {inGroup && (
              <>
                <li aria-hidden className="text-border-control">
                  /
                </li>
                <li className="min-w-0">
                  <Link to={`/devolucoes/grupos/${ret.returnGroupId}`} className="hover:text-fg hover:underline">
                    {ret.consultantFullName}
                  </Link>
                </li>
              </>
            )}
            <li aria-hidden className="text-border-control">
              /
            </li>
            <li className="font-mono text-fg" aria-current="page">
              {ret.assetTag}
            </li>
          </ol>
        </nav>
        <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-x-3.5 gap-y-1">
              <h1 className="text-xl leading-7 font-semibold tracking-[-0.01em] break-words text-fg">
                Devolução: <span className="font-mono text-[19px] font-medium">{ret.assetTag}</span>
              </h1>
              {statusMeta && <StatusBadge label={statusMeta.label} tone={statusMeta.tone} icon={statusMeta.icon} />}
            </div>
            <p className="mt-0.5 text-[13px] leading-5 text-fg-muted">
              {ret.consultantFullName} · {ret.equipmentTypeName}
            </p>
          </div>
          {isOpen && canReturn && (
            <div className="flex flex-wrap items-center gap-2">
              {ret.status === 'Pending' ? (
                <>
                  <Button variant="secondary" size="sm" className="max-lg:h-11" onClick={() => setCancelOpen(true)}>
                    Cancelar devolução
                  </Button>
                  <Button variant="primary" size="sm" className="max-lg:h-11" onClick={() => setReceiveOpen(true)}>
                    Receber equipamento
                  </Button>
                </>
              ) : (
                <Button variant="primary" size="sm" className="max-lg:h-11" onClick={() => setFinalizeOpen(true)}>
                  Finalizar inspeção
                </Button>
              )}
            </div>
          )}
        </div>
      </header>

      {ret.status !== 'Cancelled' && (
        <div className="border-b border-border px-4 py-3.5 sm:px-6">
          <StepIndicator steps={STEPS} currentKey={currentStepKey} completedKeys={completedKeys} />
        </div>
      )}

      {/* ≥1280px: ruled main column + 336px side panel; below, the panel stacks first. */}
      <div className="grid grid-cols-1 gap-x-6 px-4 pb-6 sm:px-6 xl:grid-cols-[minmax(0,1fr)_336px]">
        <aside aria-label="Estado e contexto" className="flex flex-col gap-4 pt-5 xl:col-start-2 xl:row-start-1">
          <section aria-labelledby="return-context-heading" className="rounded-lg border border-border bg-surface">
            <h2
              id="return-context-heading"
              className="border-b border-border px-4 py-3 text-[12px] leading-4 font-medium text-fg-muted"
            >
              {isOpen ? 'Próximo passo' : 'Resultado'}
            </h2>
            <p className="px-4 pt-3.5 text-[13px] leading-5 text-fg">{nextStep}</p>
            <div className="px-4 pt-3 pb-3.5">
              <KeyValueList
                items={[
                  {
                    label: 'Equipamento',
                    value: canReadEquipment ? (
                      <Link to={`/equipamentos/${ret.equipmentId}`} className={textLinkClass}>
                        <span className="font-mono text-[12.5px]">{ret.assetTag}</span>
                      </Link>
                    ) : (
                      <span className="font-mono text-[12.5px]">{ret.assetTag}</span>
                    ),
                  },
                  {
                    label: 'Consultor',
                    value: canReadConsultants ? (
                      <Link to={`/consultores/${ret.consultantId}`} className={textLinkClass}>
                        {ret.consultantFullName}
                      </Link>
                    ) : (
                      ret.consultantFullName
                    ),
                  },
                ]}
              />
            </div>
            {inGroup && (
              <div className="border-t border-border px-4 py-2.5">
                <Link to={`/devolucoes/grupos/${ret.returnGroupId}`} className={`text-[13px] ${textLinkClass}`}>
                  Ver conjunto ({ret.returnGroupSize} equipamentos)
                </Link>
              </div>
            )}
          </section>
        </aside>

        <div className="min-w-0 xl:col-start-1 xl:row-start-1">
          <DetailSection variant="ruled" title="Detalhes">
            <KeyValueList
              columns={3}
              items={[
                { label: 'Consultor', value: ret.consultantFullName },
                { label: 'Equipamento', value: `${ret.assetTag} · ${ret.equipmentTypeName}` },
                { label: 'Motivo', value: ret.reason ?? '—' },
                { label: 'Data prevista', value: formatDate(ret.expectedReturnDate) },
                { label: 'Iniciada por', value: ret.createdByDisplayNameSnapshot },
                { label: 'Iniciada em', value: formatDateTime(ret.createdAtUtc) },
                ...(ret.receivedOn
                  ? [
                      { label: 'Recebida em', value: formatDate(ret.receivedOn) },
                      { label: 'Recebida por', value: ret.receivedByDisplayNameSnapshot ?? '—' },
                    ]
                  : []),
              ]}
            />
          </DetailSection>

          {ret.status === 'Cancelled' && (
            <DetailSection variant="ruled" title="Devolução cancelada">
              <KeyValueList
                columns={3}
                items={[
                  { label: 'Cancelada em', value: formatDateTime(ret.cancelledAtUtc) },
                  { label: 'Cancelada por', value: ret.cancelledByDisplayNameSnapshot ?? '—' },
                  { label: 'Motivo', value: ret.cancellationReason ?? '—' },
                ]}
              />
            </DetailSection>
          )}

          {(ret.status === 'Received' || ret.status === 'Completed') && (
            <>
              <DetailSection
                variant="ruled"
                title="Inspeção de devolução"
                description={
                  ret.status === 'Completed'
                    ? `Finalizada por ${ret.inspection?.inspectedByDisplayNameSnapshot ?? '—'} em ${formatDateTime(ret.inspection?.finalizedAtUtc)}`
                    : 'Registe a condição em que o equipamento foi devolvido.'
                }
              >
                {ret.status === 'Completed' ? (
                  <KeyValueList
                    items={[
                      {
                        label: 'Condição',
                        value: (
                          <StatusBadge
                            label={equipmentConditionMeta[ret.inspection?.condition ?? '']?.label ?? '—'}
                            tone={equipmentConditionMeta[ret.inspection?.condition ?? '']?.tone ?? 'neutral'}
                          />
                        ),
                      },
                      { label: 'Notas', value: ret.inspection?.notes ?? '—' },
                      {
                        label: 'Avaria',
                        value: ret.inspection?.damage ? (
                          <StatusBadge
                            label={damageStatusMeta[ret.inspection.damage.status]?.label ?? ret.inspection.damage.status}
                            tone={damageStatusMeta[ret.inspection.damage.status]?.tone ?? 'neutral'}
                          />
                        ) : (
                          'Sem avaria'
                        ),
                      },
                      ...(ret.inspection?.damage
                        ? [{ label: 'Descrição da avaria', value: ret.inspection.damage.description }]
                        : []),
                    ]}
                  />
                ) : (
                  <form className="flex flex-col gap-4" onSubmit={(e) => void inspectionForm.handleSubmit((v) => inspectionMutation.mutate(v))(e)}>
                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-[200px_minmax(0,1fr)]">
                      <FormField id="condition" label="Condição" required error={inspectionForm.formState.errors.condition?.message}>
                        <Select id="condition" disabled={!canUpdateInspection} {...inspectionForm.register('condition')}>
                          {(['Excellent', 'Good', 'Fair', 'Poor'] as EquipmentCondition[]).map((c) => (
                            <option key={c} value={c}>
                              {equipmentConditionMeta[c]?.label ?? c}
                            </option>
                          ))}
                        </Select>
                      </FormField>
                      <FormField id="notes" label="Notas" optional>
                        <Textarea id="notes" rows={1} disabled={!canUpdateInspection} {...inspectionForm.register('notes')} />
                      </FormField>
                    </div>
                    <Checkbox
                      id="hasDamage"
                      label="Equipamento com avaria"
                      disabled={!canUpdateInspection}
                      {...inspectionForm.register('hasDamage')}
                    />
                    {hasDamage && (
                      <FormField
                        id="damageDescription"
                        label="Descrição da avaria"
                        required
                        error={inspectionForm.formState.errors.damageDescription?.message}
                      >
                        <Textarea
                          id="damageDescription"
                          disabled={!canUpdateInspection}
                          invalid={Boolean(inspectionForm.formState.errors.damageDescription)}
                          {...inspectionForm.register('damageDescription')}
                        />
                      </FormField>
                    )}
                    {canUpdateInspection && (
                      <div>
                        <Button type="submit" variant="secondary" className="max-lg:h-11" loading={inspectionMutation.isPending}>
                          Guardar inspeção
                        </Button>
                      </div>
                    )}
                  </form>
                )}
              </DetailSection>

              <DetailSection
                variant="ruled"
                title="Fotografias da devolução"
                description="Evidência adicional opcional. O registo fotográfico oficial do estado do equipamento já foi feito na receção."
              >
                <PhotoGallery
                  title="Fotografias da devolução"
                  photos={ret.inspection?.photos ?? []}
                  editable={ret.status === 'Received' && canUpdateInspection}
                  hint="JPEG, PNG ou WebP"
                  getBlob={(photoId) => api.getReturnPhotoBlob(id!, photoId)}
                  onUpload={(file) => api.uploadReturnPhoto(id!, file)}
                  onDelete={(photoId) => api.deleteReturnPhoto(id!, photoId)}
                  invalidateKey={detailKey}
                />
              </DetailSection>

              {(hasDamage || (ret.status === 'Completed' && ret.inspection?.damage)) && (
                <DetailSection variant="ruled" title="Fotografias da avaria" description="Pelo menos 1 fotografia.">
                  <PhotoGallery
                    title="Fotografias da avaria"
                    photos={ret.inspection?.damagePhotos ?? []}
                    editable={ret.status === 'Received' && canUpdateInspection}
                    hint="JPEG, PNG ou WebP"
                    getBlob={(photoId) => api.getDamagePhotoBlob(id!, photoId)}
                    onUpload={(file) => api.uploadDamagePhoto(id!, file)}
                    onDelete={(photoId) => api.deleteDamagePhoto(id!, photoId)}
                    invalidateKey={detailKey}
                  />
                </DetailSection>
              )}
            </>
          )}
        </div>
      </div>

      <Dialog
        open={receiveOpen}
        onOpenChange={setReceiveOpen}
        title="Receber equipamento"
        description="Regista o momento em que a responsabilidade pelo equipamento termina."
        size={requiresReceivePhotos ? 'md' : 'sm'}
        footer={
          <>
            <Button variant="secondary" onClick={() => setReceiveOpen(false)}>
              Cancelar
            </Button>
            <Button
              variant="primary"
              loading={receiveMutation.isPending}
              onClick={() => void receiveForm.handleSubmit(submitReceive)()}
            >
              Confirmar receção
            </Button>
          </>
        }
      >
        <form className="flex flex-col gap-4" onSubmit={(e) => void receiveForm.handleSubmit(submitReceive)(e)}>
          <FormField id="receivedOn" label="Data de receção" required error={receiveForm.formState.errors.receivedOn?.message}>
            <DateField
              id="receivedOn"
              max={format(new Date(), 'yyyy-MM-dd')}
              invalid={Boolean(receiveForm.formState.errors.receivedOn)}
              {...receiveForm.register('receivedOn')}
            />
          </FormField>
          {canPickReceiver && (
            <FormField
              id="receivedByUserId"
              label="Recebido por"
              helperText="Por predefinição, o próprio utilizador."
              error={receiveForm.formState.errors.receivedByUserId?.message}
            >
              {/* Controlled: an uncontrolled select lost the default as soon as its options loaded
                  (the browser fell back to the first option, and that is what was submitted). */}
              <Controller
                control={receiveForm.control}
                name="receivedByUserId"
                render={({ field }) => (
                  <Select id="receivedByUserId" invalid={Boolean(receiveForm.formState.errors.receivedByUserId)} {...field}>
                    {/* Shown only if there is no value: never silently stands in for a real user. */}
                    {!field.value && <option value="">Selecione…</option>}
                    {receiverOptions.map((u) => (
                      <option key={u.id} value={u.id}>
                        {u.firstName} {u.lastName} ({u.email})
                      </option>
                    ))}
                  </Select>
                )}
              />
            </FormField>
          )}
          {requiresReceivePhotos && (
            <div role="group" aria-labelledby="receive-photos-label" className="flex flex-col gap-1.5">
              <p id="receive-photos-label" className="text-[13px] leading-[18px] font-medium text-fg">
                Fotografias do estado atual
                <span className="text-danger" aria-hidden>
                  {' '}
                  *
                </span>
              </p>
              <p className="text-[12px] leading-4 text-fg-muted">
                Exatamente {REQUIRED_RECEIVE_PHOTO_COUNT}. Passam a ser as fotografias atuais do equipamento.
              </p>
              <ConditionPhotoPicker
                photos={receivePhotos}
                onChange={setReceivePhotos}
                disabled={receiveMutation.isPending}
                error={receivePhotosError}
              />
            </div>
          )}
        </form>
      </Dialog>

      <Dialog
        open={cancelOpen}
        onOpenChange={(open) => {
          setCancelOpen(open)
          if (!open) cancelMutation.reset()
        }}
        title="Cancelar devolução"
        description="O equipamento volta a ficar atribuído ao consultor. Pode iniciar uma nova devolução mais tarde."
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setCancelOpen(false)}>
              Voltar
            </Button>
            <Button
              variant="danger"
              loading={cancelMutation.isPending}
              onClick={() => void cancelForm.handleSubmit((v) => cancelMutation.mutate(v))()}
            >
              Cancelar devolução
            </Button>
          </>
        }
      >
        <form className="flex flex-col gap-4" onSubmit={(e) => void cancelForm.handleSubmit((v) => cancelMutation.mutate(v))(e)}>
          <FormField id="cancelReason" label="Motivo" optional>
            <Textarea id="cancelReason" {...cancelForm.register('reason')} />
          </FormField>
          {cancelError && (
            <p
              className="rounded-md border border-danger-border bg-danger-bg px-3 py-2 text-[13px] leading-[19px] whitespace-pre-line text-danger-fg"
              role="alert"
            >
              {cancelError}
            </p>
          )}
        </form>
      </Dialog>

      <ConfirmDialog
        open={finalizeOpen}
        onOpenChange={(open) => {
          setFinalizeOpen(open)
          if (!open) setFinalizeError(null)
        }}
        title="Finalizar inspeção"
        description="Esta ação é irreversível. Sem avaria, o equipamento fica Disponível; com avaria, fica Avariado e a devolução não pode mais ser alterada."
        confirmLabel="Finalizar"
        loading={finalizeMutation.isPending}
        error={finalizeError}
        onConfirm={() => finalizeMutation.mutate()}
      />
    </div>
  )
}
