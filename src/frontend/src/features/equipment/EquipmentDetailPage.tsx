import { useState, type ReactNode } from 'react'
import { useNavigate, useParams, Link } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FileWarning, ImageOff, Pencil } from 'lucide-react'
import { DetailSection } from '../../shared/components/DetailSection'
import { KeyValueList } from '../../shared/components/KeyValueList'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { Button } from '../../shared/components/Button'
import { Textarea } from '../../shared/components/Textarea'
import { DateField } from '../../shared/components/DateField'
import { FormField } from '../../shared/components/FormField'
import { Dialog } from '../../shared/components/Dialog'
import { ConfirmDialog } from '../../shared/components/ConfirmDialog'
import { Skeleton } from '../../shared/components/Skeleton'
import { ErrorState } from '../../shared/components/ErrorState'
import {
  equipmentStatusMeta,
  workforceTypeLabels,
  photoSetSourceMeta,
} from '../../shared/status/statusMaps'
import { formatDate, formatDateTime } from '../../shared/utils/format'
import { mobileNumberLabel } from '../../branding/brand.config'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import * as api from './api'
import { EquipmentHistorySection } from './EquipmentHistorySection'
import { PhotoSetGallery } from './PhotoSetGallery'
import { ConditionPhotoPicker } from './ConditionPhotoPicker'
import { ReplaceCurrentPhotosDialog } from './ReplaceCurrentPhotosDialog'
import { listAssignments, initiateReturn } from '../assignments/api'
import { createEquipmentRetirementRequest } from '../equipment-retirement-requests/api'

const REQUIRED_PHOTO_COUNT = 3

/** Identifiers/codes read in Geist Mono (Visual System v1 type rules); everything else stays in the UI face. */
function Mono({ children }: { children: ReactNode }) {
  return <span className="font-mono text-[12.5px] font-medium">{children}</span>
}

export function EquipmentDetailPage() {
  const { id } = useParams<{ id: string }>()
  const { hasPermission } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [retireOpen, setRetireOpen] = useState(false)
  const [maintenanceOpen, setMaintenanceOpen] = useState(false)
  const [maintenanceNotes, setMaintenanceNotes] = useState('')
  const [returnOpen, setReturnOpen] = useState(false)
  const [expectedReturnDate, setExpectedReturnDate] = useState('')
  const [returnReason, setReturnReason] = useState('')
  const [retirementRequestOpen, setRetirementRequestOpen] = useState(false)
  const [retirementRequestReason, setRetirementRequestReason] = useState('')
  const [establishPhotosOpen, setEstablishPhotosOpen] = useState(false)
  const [newPhotos, setNewPhotos] = useState<File[]>([])
  const [newPhotosError, setNewPhotosError] = useState<string | undefined>()
  const [establishConfirmOpen, setEstablishConfirmOpen] = useState(false)
  const [replacePhotosOpen, setReplacePhotosOpen] = useState(false)

  const query = useQuery({
    queryKey: ['equipment', id],
    queryFn: () => api.getEquipment(id!),
    enabled: Boolean(id),
  })

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['equipment', id] })
    void queryClient.invalidateQueries({ queryKey: ['equipment'] })
  }

  const retireMutation = useMutation({
    mutationFn: () => api.retireEquipment(id!),
    onSuccess: () => {
      invalidate()
      setRetireOpen(false)
    },
  })

  const retirementRequestMutation = useMutation({
    mutationFn: () =>
      createEquipmentRetirementRequest({ equipmentId: id!, reason: retirementRequestReason }),
    onSuccess: () => {
      invalidate()
      setRetirementRequestOpen(false)
      setRetirementRequestReason('')
    },
  })

  const maintenanceMutation = useMutation({
    mutationFn: () => api.startMaintenance(id!, maintenanceNotes || undefined),
    onSuccess: () => {
      invalidate()
      setMaintenanceOpen(false)
      setMaintenanceNotes('')
    },
  })

  const establishPhotosMutation = useMutation({
    mutationFn: () => api.establishEquipmentPhotos(id!, newPhotos),
    onSuccess: () => {
      invalidate()
      setEstablishConfirmOpen(false)
      setEstablishPhotosOpen(false)
      setNewPhotos([])
      setNewPhotosError(undefined)
    },
  })

  function submitEstablishPhotos() {
    if (newPhotos.length !== REQUIRED_PHOTO_COUNT) {
      setNewPhotosError(
        `São exigidas exatamente ${REQUIRED_PHOTO_COUNT} fotografias (${newPhotos.length}/${REQUIRED_PHOTO_COUNT} selecionadas).`,
      )
      return
    }
    setNewPhotosError(undefined)
    establishPhotosMutation.reset()
    setEstablishConfirmOpen(true)
  }

  const returnMutation = useMutation({
    mutationFn: async () => {
      const assignments = await listAssignments({
        equipmentId: id!,
        status: 'Active',
        page: 1,
        pageSize: 1,
      })
      const assignment = assignments.items[0]
      if (!assignment)
        throw new Error('Não foi encontrada uma atribuição ativa para este equipamento.')
      return initiateReturn(assignment.id, {
        expectedReturnDate: expectedReturnDate || undefined,
        reason: returnReason || undefined,
      })
    },
    onSuccess: (created) => {
      invalidate()
      setReturnOpen(false)
      void navigate(`/devolucoes/${created.id}`)
    },
  })

  if (query.isLoading) {
    return (
      <div className="flex flex-col gap-3 p-6">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-48 w-full" />
      </div>
    )
  }

  if (query.error || !query.data) {
    return <ErrorState error={query.error} onRetry={() => void query.refetch()} />
  }

  const equipment = query.data
  const statusMeta = equipmentStatusMeta[equipment.status]
  const canUpdate = hasPermission(Permissions.Equipment.Update)
  const canRetire = hasPermission(Permissions.Equipment.Retire)
  const canRequestRetirement = hasPermission(Permissions.EquipmentRetirementRequest.Create)
  const canManageMaintenance = hasPermission(Permissions.EquipmentMaintenance.Manage)
  const canReturn = hasPermission(Permissions.Equipment.Return)
  const canReadConsultants = hasPermission(Permissions.Consultant.Read)
  const canReadHandovers = hasPermission(Permissions.EquipmentHandover.Read)
  const canManagePhotos = hasPermission(Permissions.Equipment.PhotosManage)
  // Administration only, and only when the server says the equipment is out
  // of custody and not on a dispatched delivery — the endpoint re-checks.
  const canReplacePhotos = hasPermission(Permissions.Equipment.PhotosReplace)
  const isPhotoRequiredKind = Boolean(
    equipment.laptopSpecification || equipment.mobilePhoneSpecification,
  )
  const isRetireEligible = equipment.status === 'Available' || equipment.status === 'Damaged'
  const hasPendingRetirementRequest = Boolean(equipment.pendingRetirementRequest)
  const showRetire = canRetire && isRetireEligible
  const showRequestRetirement =
    !canRetire && canRequestRetirement && isRetireEligible && !hasPendingRetirementRequest
  const showMaintenance = canManageMaintenance && equipment.status === 'Damaged'
  const showReturn = canReturn && equipment.status === 'Assigned'

  const hasAnyActionPermission =
    canRetire || canRequestRetirement || canManageMaintenance || canReturn
  const hasNoAvailableAction =
    !showRetire && !showRequestRetirement && !showMaintenance && !showReturn
  const subtitle = [
    equipment.equipmentTypeName,
    [equipment.manufacturer, equipment.model].filter(Boolean).join(' '),
  ]
    .filter(Boolean)
    .join(' · ')
  const custody = equipment.currentCustody
  const pendingRequest = equipment.pendingRetirementRequest

  return (
    <div className="flex flex-col">
      <header className="flex flex-col gap-2 border-b border-border bg-surface px-4 py-5 sm:px-6">
        <nav aria-label="Localização">
          <ol className="flex flex-wrap items-center gap-1.5 text-[12px] text-fg-muted">
            <li>
              <Link to="/equipamentos" className="hover:text-fg hover:underline">
                Equipamentos
              </Link>
            </li>
            <li aria-hidden className="text-border-control">
              /
            </li>
            <li className="font-mono text-fg" aria-current="page">
              {equipment.assetTag}
            </li>
          </ol>
        </nav>
        <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-x-3.5 gap-y-1">
              <h1 className="font-mono text-[20px] leading-7 font-medium tracking-[-0.01em] break-all text-fg">
                {equipment.assetTag}
              </h1>
              {statusMeta && (
                <StatusBadge
                  label={statusMeta.label}
                  tone={statusMeta.tone}
                  icon={statusMeta.icon}
                />
              )}
            </div>
            {subtitle && <p className="mt-0.5 text-[13px] leading-5 text-fg-muted">{subtitle}</p>}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {canUpdate && (
              <Button
                variant="secondary"
                size="sm"
                className="max-lg:h-11"
                icon={<Pencil className="size-3.5" aria-hidden />}
                onClick={() => void navigate(`/equipamentos/${equipment.id}/editar`)}
              >
                Editar
              </Button>
            )}
            {showRetire && (
              <Button
                variant="danger-outline"
                size="sm"
                className="max-lg:h-11"
                onClick={() => setRetireOpen(true)}
              >
                Abater equipamento
              </Button>
            )}
            {showRequestRetirement && (
              <Button
                variant="danger-outline"
                size="sm"
                className="max-lg:h-11"
                onClick={() => setRetirementRequestOpen(true)}
              >
                Solicitar abate
              </Button>
            )}
            {showMaintenance && (
              <Button
                variant="primary"
                size="sm"
                className="max-lg:h-11"
                onClick={() => setMaintenanceOpen(true)}
              >
                Iniciar manutenção
              </Button>
            )}
            {showReturn && (
              <Button
                variant="primary"
                size="sm"
                className="max-lg:h-11"
                onClick={() => setReturnOpen(true)}
              >
                Iniciar devolução
              </Button>
            )}
          </div>
        </div>
        {hasAnyActionPermission && hasNoAvailableAction && (
          <p className="text-[12px] text-fg-muted">Sem ações disponíveis para o estado atual.</p>
        )}
      </header>

      {/* ≥1280px: ruled main column + 336px side panel. Below, the panel stacks under the header (Spec breakpoints). */}
      <div className="grid grid-cols-1 gap-x-6 px-4 pb-6 sm:px-6 xl:grid-cols-[minmax(0,1fr)_336px]">
        <aside
          aria-label="Custódia e pedidos"
          className="flex flex-col gap-4 pt-5 xl:col-start-2 xl:row-start-1"
        >
          <section
            aria-labelledby="custody-heading"
            className="rounded-lg border border-border bg-surface"
          >
            <h2
              id="custody-heading"
              className="border-b border-border px-4 py-3 text-[12px] leading-4 font-medium text-fg-muted"
            >
              Custódia atual
            </h2>
            {custody ? (
              <>
                <div className="flex items-center gap-2.5 px-4 pt-3.5 pb-1">
                  <span
                    aria-hidden
                    className="flex size-9 shrink-0 items-center justify-center rounded-md border border-border bg-surface-selected text-[12px] font-semibold text-fg"
                  >
                    {initials(custody.consultantFullName)}
                  </span>
                  <p className="min-w-0 text-[14px] leading-5 font-semibold break-words text-fg">
                    {custody.consultantFullName}
                  </p>
                </div>
                <div className="px-4 pt-2.5 pb-3.5">
                  <KeyValueList
                    items={[
                      {
                        label: 'Regime',
                        value: custody.workforceType
                          ? (workforceTypeLabels[custody.workforceType] ?? custody.workforceType)
                          : '—',
                      },
                      { label: 'Empresa', value: custody.companyName || '—' },
                      { label: 'Função', value: custody.roleOrFunction || '—' },
                      { label: 'Atribuído desde', value: formatDate(custody.assignedAtUtc) },
                      ...(custody.origin === 'CurrentInventoryImport'
                        ? [{ label: 'Origem', value: 'Importação do inventário atual' }]
                        : []),
                    ]}
                  />
                </div>
                {((canReadHandovers && custody.equipmentHandoverId) || canReadConsultants) && (
                  <div className="flex flex-wrap gap-2 border-t border-border px-4 py-2.5">
                    {canReadHandovers && custody.equipmentHandoverId && (
                      <Button
                        variant="secondary"
                        size="sm"
                        className="max-lg:h-11"
                        onClick={() => void navigate(`/entregas/${custody.equipmentHandoverId}`)}
                      >
                        Ver entrega
                      </Button>
                    )}
                    {canReadConsultants && (
                      <Button
                        variant="secondary"
                        size="sm"
                        className="max-lg:h-11"
                        onClick={() => void navigate(`/consultores/${custody.consultantId}`)}
                      >
                        Ver consultor
                      </Button>
                    )}
                  </div>
                )}
              </>
            ) : equipment.status === 'InTransit' ? (
              // Shipped by carrier but not yet confirmed: never shown as custody.
              <p className="px-4 py-3.5 text-[13px] text-fg-muted">
                Em trânsito por transportadora. Sem atribuição até à confirmação da entrega.
              </p>
            ) : (
              <p className="px-4 py-3.5 text-[13px] text-fg-muted">Sem atribuição atual.</p>
            )}
          </section>

          {pendingRequest && (
            <section
              aria-labelledby="retirement-request-heading"
              className="rounded-lg border border-warning-border bg-surface"
            >
              <h2
                id="retirement-request-heading"
                className="flex items-center gap-2 border-b border-warning-border bg-warning-bg px-4 py-2.5 text-[13px] leading-5 font-semibold text-warning"
              >
                <FileWarning className="size-4 shrink-0" aria-hidden />
                Abate solicitado
              </h2>
              <div className="flex flex-col gap-3 px-4 py-3.5">
                <p className="text-[13px] text-fg-muted">Aguardando análise da Administração.</p>
                <KeyValueList
                  items={[
                    { label: 'Solicitado por', value: pendingRequest.requestedByDisplayName },
                    { label: 'Data', value: formatDateTime(pendingRequest.requestedAtUtc) },
                  ]}
                />
                <div className="border-t border-border pt-3">
                  <p className="text-[12px] leading-4 text-fg-muted">Motivo</p>
                  <p className="mt-0.5 text-[13px] leading-5 whitespace-pre-wrap text-fg">
                    {pendingRequest.requestReason}
                  </p>
                </div>
              </div>
            </section>
          )}
        </aside>

        <div className="min-w-0 xl:col-start-1 xl:row-start-1">
          <DetailSection variant="ruled" title="Detalhes">
            <KeyValueList
              columns={3}
              items={[
                { label: 'Tipo', value: equipment.equipmentTypeName },
                { label: 'Fabricante', value: equipment.manufacturer || '—' },
                { label: 'Modelo', value: equipment.model || '—' },
                {
                  label: 'Número de série',
                  value: equipment.serialNumber ? <Mono>{equipment.serialNumber}</Mono> : '—',
                },
                { label: 'Registado em', value: formatDate(equipment.createdAtUtc) },
                { label: 'Última atualização', value: formatDateTime(equipment.updatedAtUtc) },
              ]}
            />
            {equipment.notes && (
              <div className="mt-3.5">
                <p className="text-[12px] leading-4 text-fg-muted">Notas</p>
                <p className="mt-0.5 text-[13px] leading-5 whitespace-pre-wrap text-fg">
                  {equipment.notes}
                </p>
              </div>
            )}
          </DetailSection>

          {equipment.laptopSpecification && (
            <DetailSection variant="ruled" title="Especificações do portátil">
              <KeyValueList
                columns={3}
                items={[
                  { label: 'Processador', value: equipment.laptopSpecification.processor },
                  { label: 'Memória RAM', value: equipment.laptopSpecification.memory },
                  { label: 'Armazenamento', value: equipment.laptopSpecification.storage },
                ]}
              />
            </DetailSection>
          )}

          {equipment.mobilePhoneSpecification && (
            <DetailSection variant="ruled" title="Especificações do telemóvel">
              <KeyValueList
                columns={3}
                items={[
                  { label: 'IMEI', value: <Mono>{equipment.mobilePhoneSpecification.imei}</Mono> },
                  {
                    label: mobileNumberLabel,
                    value: equipment.mobilePhoneSpecification.mobileNumber || '—',
                  },
                  {
                    label: 'Número SIM',
                    value: equipment.mobilePhoneSpecification.simSerialNumber ? (
                      <Mono>{equipment.mobilePhoneSpecification.simSerialNumber}</Mono>
                    ) : (
                      '—'
                    ),
                  },
                ]}
              />
            </DetailSection>
          )}

          <DetailSection variant="ruled" title="Informações de compra">
            <KeyValueList
              columns={3}
              items={[
                {
                  label: 'Data de compra',
                  value: equipment.purchaseDate ? formatDate(equipment.purchaseDate) : '—',
                },
                {
                  label: 'N.º da fatura',
                  value: equipment.invoiceNumber ? <Mono>{equipment.invoiceNumber}</Mono> : '—',
                },
                { label: 'Loja / Fornecedor', value: equipment.purchaseVendor || '—' },
              ]}
            />
          </DetailSection>

          {isPhotoRequiredKind && (
            <DetailSection
              variant="ruled"
              title="Fotografias atuais"
              description="Pertencem ao equipamento. Conjunto de exatamente 3."
              actions={
                canReplacePhotos &&
                equipment.currentPhotoSet &&
                equipment.currentPhotosCorrectable && (
                  <Button
                    size="sm"
                    variant="secondary"
                    className="max-lg:h-11"
                    onClick={() => setReplacePhotosOpen(true)}
                  >
                    Alterar fotografias
                  </Button>
                )
              }
            >
              {equipment.currentPhotoSet ? (
                <div className="flex flex-col gap-2.5">
                  <PhotoSetGallery
                    photos={equipment.currentPhotoSet.photos}
                    getBlob={(photoId) => api.getEquipmentPhotoBlob(equipment.id, photoId)}
                  />
                  <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1 text-[13px] text-fg-muted">
                    <span>
                      Atualizadas em{' '}
                      <span className="text-fg tabular-nums">
                        {formatDate(equipment.currentPhotoSet.createdAtUtc)}
                      </span>
                    </span>
                    <span aria-hidden className="text-border-control">
                      ·
                    </span>
                    <span className="inline-flex items-center gap-1.5">
                      Origem:
                      {(() => {
                        const sourceMeta = photoSetSourceMeta[equipment.currentPhotoSet.source]
                        return sourceMeta ? (
                          <StatusBadge
                            label={sourceMeta.label}
                            tone={sourceMeta.tone}
                            icon={sourceMeta.icon}
                          />
                        ) : (
                          equipment.currentPhotoSet.source
                        )
                      })()}
                    </span>
                  </div>
                </div>
              ) : (
                <div className="flex items-start gap-3">
                  <span className="flex size-10 shrink-0 items-center justify-center rounded-lg border border-warning-border bg-warning-bg text-warning">
                    <ImageOff className="size-[18px]" aria-hidden />
                  </span>
                  <div className="flex min-w-0 flex-col items-start gap-1">
                    <p className="text-[14px] leading-5 font-semibold text-fg">
                      Fotografias atuais não registadas.
                    </p>
                    <p className="text-[13px] leading-5 text-fg-muted">
                      Equipamento anterior ao registo fotográfico. São exigidas exatamente 3
                      fotografias.
                    </p>
                    {canManagePhotos && (
                      <Button
                        variant="secondary"
                        size="sm"
                        className="mt-2 max-lg:h-11"
                        onClick={() => setEstablishPhotosOpen(true)}
                      >
                        Registar fotografias
                      </Button>
                    )}
                  </div>
                </div>
              )}
            </DetailSection>
          )}

          <EquipmentHistorySection equipmentId={equipment.id} />
        </div>
      </div>

      <ConfirmDialog
        open={retireOpen}
        onOpenChange={(open) => {
          setRetireOpen(open)
          if (!open) retireMutation.reset()
        }}
        title="Abater equipamento"
        description={`O equipamento "${equipment.assetTag}" deixa permanentemente de estar disponível para atribuição. Esta ação não pode ser revertida por esta aplicação.`}
        confirmLabel="Abater"
        destructive
        loading={retireMutation.isPending}
        error={
          retireMutation.isError
            ? retireMutation.error instanceof Error
              ? retireMutation.error.message
              : 'Não foi possível abater o equipamento.'
            : null
        }
        onConfirm={() => retireMutation.mutate()}
      />

      <Dialog
        open={retirementRequestOpen}
        onOpenChange={(open) => {
          setRetirementRequestOpen(open)
          if (!open) setRetirementRequestReason('')
        }}
        title="Solicitar abate"
        description={`A Administração será notificada e decidirá se o equipamento "${equipment.assetTag}" deve ser abatido.`}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setRetirementRequestOpen(false)}>
              Cancelar
            </Button>
            <Button
              variant="danger"
              loading={retirementRequestMutation.isPending}
              disabled={!retirementRequestReason.trim()}
              onClick={() => retirementRequestMutation.mutate()}
            >
              Solicitar abate
            </Button>
          </>
        }
      >
        <FormField id="retirement-request-reason" label="Motivo da solicitação" required>
          <Textarea
            id="retirement-request-reason"
            value={retirementRequestReason}
            onChange={(e) => setRetirementRequestReason(e.target.value)}
            placeholder="Descreva o motivo do pedido de abate."
          />
        </FormField>
        {retirementRequestMutation.isError && (
          <p className="mt-3 text-[13px] text-danger" role="alert">
            {retirementRequestMutation.error instanceof Error
              ? retirementRequestMutation.error.message
              : 'Não foi possível submeter a solicitação.'}
          </p>
        )}
      </Dialog>

      <Dialog
        open={maintenanceOpen}
        onOpenChange={(open) => {
          setMaintenanceOpen(open)
          if (!open) maintenanceMutation.reset()
        }}
        title="Iniciar manutenção"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setMaintenanceOpen(false)}>
              Cancelar
            </Button>
            <Button
              variant="primary"
              loading={maintenanceMutation.isPending}
              onClick={() => maintenanceMutation.mutate()}
            >
              Iniciar
            </Button>
          </>
        }
      >
        <FormField id="maintenance-notes" label="Observações" optional>
          <Textarea
            id="maintenance-notes"
            value={maintenanceNotes}
            onChange={(e) => setMaintenanceNotes(e.target.value)}
            placeholder="Descreva a intervenção prevista, se aplicável."
          />
        </FormField>
        {maintenanceMutation.isError && (
          <p className="mt-3 text-[13px] text-danger" role="alert">
            {maintenanceMutation.error instanceof Error
              ? maintenanceMutation.error.message
              : 'Não foi possível iniciar a manutenção.'}
          </p>
        )}
      </Dialog>

      <Dialog
        open={returnOpen}
        onOpenChange={setReturnOpen}
        title="Iniciar devolução"
        description="A devolução ficará pendente até o equipamento ser fisicamente recebido."
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setReturnOpen(false)}>
              Cancelar
            </Button>
            <Button
              variant="primary"
              loading={returnMutation.isPending}
              onClick={() => returnMutation.mutate()}
            >
              Iniciar devolução
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <FormField id="expected-return-date" label="Data prevista de devolução" optional>
            <DateField
              id="expected-return-date"
              value={expectedReturnDate}
              onChange={(e) => setExpectedReturnDate(e.target.value)}
            />
          </FormField>
          <FormField id="return-reason" label="Motivo" optional>
            <Textarea
              id="return-reason"
              value={returnReason}
              onChange={(e) => setReturnReason(e.target.value)}
            />
          </FormField>
          {returnMutation.isError && (
            <p className="text-[13px] text-danger" role="alert">
              {returnMutation.error instanceof Error
                ? returnMutation.error.message
                : 'Não foi possível iniciar a devolução.'}
            </p>
          )}
        </div>
      </Dialog>

      <Dialog
        open={establishPhotosOpen}
        onOpenChange={(open) => {
          setEstablishPhotosOpen(open)
          if (!open) {
            setNewPhotos([])
            setNewPhotosError(undefined)
          }
        }}
        title="Registar fotografias atuais"
        description="Este equipamento ainda não tem um conjunto de fotografias atuais registado. Selecione exatamente 3 fotografias do seu estado atual."
        footer={
          <>
            <Button variant="secondary" onClick={() => setEstablishPhotosOpen(false)}>
              Cancelar
            </Button>
            <Button variant="primary" onClick={submitEstablishPhotos}>
              Guardar fotografias
            </Button>
          </>
        }
      >
        <ConditionPhotoPicker
          photos={newPhotos}
          onChange={setNewPhotos}
          disabled={establishPhotosMutation.isPending}
          error={newPhotosError}
        />
      </Dialog>

      <ConfirmDialog
        open={establishConfirmOpen}
        onOpenChange={setEstablishConfirmOpen}
        title="Guardar fotografias atuais?"
        description="Estas 3 fotografias serão registadas como o estado atual do equipamento."
        confirmLabel="Guardar fotografias"
        loading={establishPhotosMutation.isPending}
        error={
          establishPhotosMutation.isError
            ? establishPhotosMutation.error instanceof Error
              ? establishPhotosMutation.error.message
              : 'Não foi possível registar as fotografias.'
            : null
        }
        onConfirm={() => establishPhotosMutation.mutate()}
      />

      {equipment.currentPhotoSet && (
        <ReplaceCurrentPhotosDialog
          key={equipment.currentPhotoSet.id}
          equipmentId={equipment.id}
          photoSet={equipment.currentPhotoSet}
          open={replacePhotosOpen}
          onOpenChange={setReplacePhotosOpen}
        />
      )}
    </div>
  )
}

function initials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/)
  const first = parts[0]?.[0] ?? ''
  const last = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : ''
  return (first + last).toUpperCase()
}
