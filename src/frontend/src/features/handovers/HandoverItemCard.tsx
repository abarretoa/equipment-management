import { useState } from 'react'
import { Link } from 'react-router'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Trash2 } from 'lucide-react'
import { FormField } from '../../shared/components/FormField'
import { Select } from '../../shared/components/Select'
import { Textarea } from '../../shared/components/Textarea'
import { Button } from '../../shared/components/Button'
import { ConfirmDialog } from '../../shared/components/ConfirmDialog'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { ApiError } from '../../shared/api/httpClient'
import { equipmentConditionMeta, equipmentStatusMeta } from '../../shared/status/statusMaps'
import { formatDateTime } from '../../shared/utils/format'
import { PhotoGallery } from './PhotoGallery'
import * as api from './api'
import type { EquipmentCondition, HandoverItem } from './types'

const CONDITIONS: EquipmentCondition[] = ['Excellent', 'Good', 'Fair', 'Poor']

interface HandoverItemCardProps {
  handoverId: string
  item: HandoverItem
  editable: boolean
  canRemove: boolean
  /** Links the asset tag to the equipment's own page (where its current photos are managed). */
  canOpenEquipment?: boolean
  /** In a Draft: the item was dispatched or delivered by another Handover and can no longer progress here. */
  unavailable?: boolean
}

/** One equipment line of a handover: identity, the delivery inspection, and (read-only) any historical evidence. */
export function HandoverItemCard({
  handoverId,
  item,
  editable,
  canRemove,
  canOpenEquipment = false,
  unavailable = false,
}: HandoverItemCardProps) {
  const queryClient = useQueryClient()
  const invalidate = () => void queryClient.invalidateQueries({ queryKey: ['handover', handoverId] })

  const [condition, setCondition] = useState<EquipmentCondition>(item.inspection?.condition ?? 'Good')
  const [notes, setNotes] = useState(item.inspection?.notes ?? '')
  const [error, setError] = useState<string | null>(null)
  const [confirmRemoveOpen, setConfirmRemoveOpen] = useState(false)
  const [removeError, setRemoveError] = useState<string | null>(null)

  const inspectionMutation = useMutation({
    mutationFn: () => api.recordInspection(handoverId, item.id, { condition, notes: notes || undefined }),
    onMutate: () => setError(null),
    onSuccess: invalidate,
    onError: (e) => setError(e instanceof ApiError ? e.message : 'Não foi possível guardar a inspeção.'),
  })

  const removeMutation = useMutation({
    mutationFn: () => api.removeHandoverItem(handoverId, item.id),
    onMutate: () => setRemoveError(null),
    onSuccess: () => {
      setConfirmRemoveOpen(false)
      invalidate()
    },
    // Shown inside the confirmation dialog: anything behind its overlay would be invisible.
    onError: (e) =>
      setRemoveError(e instanceof ApiError && e.status < 500 ? e.message : 'Não foi possível remover o equipamento da entrega.'),
  })

  const identity = [item.manufacturer, item.model].filter(Boolean).join(' ')
  const conditionMeta = item.inspection ? equipmentConditionMeta[item.inspection.condition] : undefined

  return (
    <li className="flex flex-col gap-3 border-b border-border-row py-4 first:pt-0 last:border-b-0 last:pb-0">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="flex flex-wrap items-baseline gap-x-2 text-[13px] leading-5">
            {canOpenEquipment ? (
              <Link
                to={`/equipamentos/${item.equipmentId}`}
                className="font-mono text-[12.5px] font-medium text-fg underline decoration-border-strong underline-offset-2 hover:decoration-fg"
              >
                {item.assetTag}
              </Link>
            ) : (
              <span className="font-mono text-[12.5px] font-medium text-fg">{item.assetTag}</span>
            )}
            <span className="text-fg-muted">{item.equipmentTypeName}</span>
          </p>
          <p className="text-[12.5px] leading-[18px] text-fg-muted">
            {identity || 'Sem fabricante/modelo registado'}
            {item.serialNumber && (
              <>
                {' · Nº série '}
                <span className="font-mono text-[12px]">{item.serialNumber}</span>
              </>
            )}
          </p>
          {unavailable && item.equipmentStatus && (
            <p className="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-1 text-[12.5px] leading-[18px] text-danger">
              <StatusBadge {...(equipmentStatusMeta[item.equipmentStatus] ?? { label: item.equipmentStatus, tone: 'neutral' })} />
              Já não disponível para esta entrega.
            </p>
          )}
        </div>
        {!editable && (
          <div className="flex shrink-0 items-center gap-1">
            {conditionMeta ? (
              <StatusBadge label={conditionMeta.label} tone={conditionMeta.tone} />
            ) : (
              <StatusBadge label="Sem inspeção" tone="neutral" />
            )}
          </div>
        )}
      </div>

      {editable ? (
        <div className="flex flex-col gap-3 rounded-md bg-surface-sunken px-3 py-3">
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-[176px_minmax(0,1fr)]">
            <FormField id={`condition-${item.id}`} label="Condição na entrega">
              <Select
                id={`condition-${item.id}`}
                value={condition}
                onChange={(e) => setCondition(e.target.value as EquipmentCondition)}
              >
                {CONDITIONS.map((c) => (
                  <option key={c} value={c}>
                    {equipmentConditionMeta[c]?.label ?? c}
                  </option>
                ))}
              </Select>
            </FormField>
            <FormField id={`notes-${item.id}`} label="Notas da inspeção" optional>
              <Textarea id={`notes-${item.id}`} rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
            </FormField>
          </div>
          <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
            <Button
              size="sm"
              variant="secondary"
              className="max-lg:h-11"
              loading={inspectionMutation.isPending}
              onClick={() => inspectionMutation.mutate()}
            >
              Guardar inspeção
            </Button>
            {item.inspection ? (
              <span className="text-[12px] text-fg-muted">
                Registada por {item.inspection.inspectedByDisplayNameSnapshot} em {formatDateTime(item.inspection.inspectedAtUtc)}
              </span>
            ) : (
              <span className="text-[12px] text-warning">Inspeção de entrega por registar.</span>
            )}
            {/*
              Removal stays available until the composition locks (Registar envio
              for a carrier, Confirmar entrega in person) — also after the
              inspection was saved. Visible text, but secondary to the inspection.
            */}
            {canRemove && (
              <Button
                size="sm"
                variant="danger-outline"
                className="max-lg:h-11 sm:ml-auto"
                icon={<Trash2 className="size-3.5" aria-hidden />}
                aria-label={`Remover equipamento ${item.assetTag}`}
                onClick={() => {
                  setRemoveError(null)
                  setConfirmRemoveOpen(true)
                }}
              >
                Remover equipamento
              </Button>
            )}
          </div>
        </div>
      ) : item.inspection ? (
        <p className="text-[12.5px] leading-[18px] text-fg-muted">
          {item.inspection.notes && <span className="text-fg">{item.inspection.notes} · </span>}
          Inspecionado por {item.inspection.inspectedByDisplayNameSnapshot} em {formatDateTime(item.inspection.inspectedAtUtc)}
        </p>
      ) : null}

      {error && (
        <p className="text-[12.5px] text-danger" role="alert">
          {error}
        </p>
      )}

      {/*
        New Handovers no longer require or accept delivery-condition
        photos — that concept moved to the equipment's own current photo
        set (see Equipment Detail). Historical evidence attached to older
        Handovers (finalized before this change) remains visible here,
        read-only, for as long as it exists.
      */}
      {item.inspection && item.inspection.photos.length > 0 && (
        <div>
          <p className="mb-1.5 text-[12px] text-fg-muted">Fotografias de evidência (histórico)</p>
          <PhotoGallery
            photos={item.inspection.photos}
            getBlob={(photoId) => api.getInspectionPhotoBlob(handoverId, item.id, photoId)}
            hint="JPEG, PNG ou WebP"
            editable={false}
            viewerTitle={`Evidência da entrega · ${item.assetTag}`}
          />
        </div>
      )}
      {canRemove && (
        <ConfirmDialog
          open={confirmRemoveOpen}
          onOpenChange={(open) => {
            if (!removeMutation.isPending) setConfirmRemoveOpen(open)
          }}
          title="Remover equipamento da entrega?"
          description={
            <div className="flex flex-col gap-2 text-[13px] leading-5 text-fg-muted">
              <p>
                O equipamento <span className="font-mono text-[12.5px] font-medium text-fg">{item.assetTag}</span> será
                removido desta entrega.
              </p>
              {item.inspection && <p>A inspeção registada para esta entrega deixará de ser utilizada.</p>}
              <p>O equipamento continuará disponível para uma nova entrega.</p>
            </div>
          }
          confirmLabel="Remover equipamento"
          destructive
          loading={removeMutation.isPending}
          error={removeError}
          onConfirm={() => removeMutation.mutate()}
        />
      )}
    </li>
  )
}
