import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Dialog } from '../../shared/components/Dialog'
import { ConfirmDialog } from '../../shared/components/ConfirmDialog'
import { Button } from '../../shared/components/Button'
import { PhotoPrivacyHint } from '../../shared/components/PhotoPrivacyHint'
import { Skeleton } from '../../shared/components/Skeleton'
import { ApiError } from '../../shared/api/httpClient'
import { usePrivateImageUrl } from '../../shared/hooks/usePrivateImageUrl'
import * as api from './api'
import type { EquipmentPhotoSet } from './types'

type Slot = 1 | 2 | 3
const SLOTS: Slot[] = [1, 2, 3]

function CurrentImage({ equipmentId, photoId, alt }: { equipmentId: string; photoId: string; alt: string }) {
  const { url } = usePrivateImageUrl(['equipment-photo-blob', photoId], () => api.getEquipmentPhotoBlob(equipmentId, photoId))
  return url ? <img src={url} alt={alt} className="size-full object-cover" /> : <Skeleton className="size-full" />
}

function ReplacementImage({ file, alt }: { file: File; alt: string }) {
  const url = useMemo(() => URL.createObjectURL(file), [file])
  useEffect(() => () => URL.revokeObjectURL(url), [url])
  return <img src={url} alt={alt} className="size-full object-cover" />
}

/**
 * "Alterar fotografias" — Administration's correction of the current set.
 * Each slot shows the current photo, or the chosen replacement (which can be
 * reverted before saving). Saving goes through a confirmation, and the server
 * always produces one complete new set of 3; the previous set is kept as
 * history. Never offers deleting a photo.
 */
export function ReplaceCurrentPhotosDialog({
  equipmentId,
  photoSet,
  open,
  onOpenChange,
}: {
  equipmentId: string
  photoSet: EquipmentPhotoSet
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const queryClient = useQueryClient()
  const [replacements, setReplacements] = useState<Partial<Record<Slot, File>>>({})
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const inputs = useRef<Partial<Record<Slot, HTMLInputElement | null>>>({})

  const replacedCount = SLOTS.filter((slot) => replacements[slot]).length

  function reset() {
    setReplacements({})
    setConfirmOpen(false)
    setError(null)
  }

  const mutation = useMutation({
    mutationFn: () => api.replaceCurrentEquipmentPhotos(equipmentId, photoSet.id, replacements),
    onMutate: () => setError(null),
    onSuccess: () => {
      // Prefix match: the detail and its history both refresh.
      void queryClient.invalidateQueries({ queryKey: ['equipment', equipmentId] })
      reset()
      onOpenChange(false)
    },
    onError: (e) =>
      setError(e instanceof ApiError && e.status < 500 ? e.message : 'Não foi possível alterar as fotografias. Tente novamente.'),
  })

  return (
    <>
      <Dialog
        open={open}
        onOpenChange={(next) => {
          if (!next) reset()
          onOpenChange(next)
        }}
        title="Alterar fotografias atuais"
        description="Substitua as fotografias necessárias. O equipamento continuará sempre com um conjunto de 3 fotografias atuais."
        footer={
          <>
            <Button variant="secondary" onClick={() => onOpenChange(false)}>
              Cancelar
            </Button>
            <Button variant="primary" disabled={replacedCount === 0} onClick={() => setConfirmOpen(true)}>
              Guardar alterações
            </Button>
          </>
        }
      >
        <div className="mb-3">
          <PhotoPrivacyHint />
        </div>
        <ul aria-label="Fotografias atuais" className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          {photoSet.photos.map((photo, index) => {
            const slot = SLOTS[index]!
            const replacement = replacements[slot]
            const label = `Fotografia ${slot}`
            return (
              <li key={photo.id} className="flex flex-col gap-2">
                <div className="aspect-[4/3] overflow-hidden rounded-md border border-border bg-surface-sunken">
                  {replacement ? (
                    <ReplacementImage file={replacement} alt={`${label} — nova`} />
                  ) : (
                    <CurrentImage equipmentId={equipmentId} photoId={photo.id} alt={`${label} — atual`} />
                  )}
                </div>
                <p className="text-[12.5px] leading-[18px] text-fg-muted">
                  {replacement ? `${label}: será substituída` : `${label}: atual`}
                </p>
                <input
                  ref={(element) => {
                    inputs.current[slot] = element
                  }}
                  type="file"
                  accept="image/jpeg,image/png,image/webp"
                  className="sr-only"
                  tabIndex={-1}
                  aria-label={`Escolher nova ${label.toLowerCase()}`}
                  onChange={(event) => {
                    const file = event.target.files?.[0]
                    if (file) setReplacements((current) => ({ ...current, [slot]: file }))
                    event.target.value = ''
                  }}
                />
                <div className="flex flex-wrap gap-2">
                  <Button
                    size="sm"
                    variant="secondary"
                    className="max-lg:h-11"
                    aria-label={`Alterar ${label.toLowerCase()}`}
                    onClick={() => inputs.current[slot]?.click()}
                  >
                    Alterar
                  </Button>
                  {replacement && (
                    <Button
                      size="sm"
                      variant="ghost"
                      className="max-lg:h-11"
                      aria-label={`Repor ${label.toLowerCase()} atual`}
                      onClick={() =>
                        setReplacements((current) => {
                          const next = { ...current }
                          delete next[slot]
                          return next
                        })
                      }
                    >
                      Repor
                    </Button>
                  )}
                </div>
              </li>
            )
          })}
        </ul>
      </Dialog>

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={(next) => {
          setConfirmOpen(next)
          if (!next) setError(null)
        }}
        title="Alterar fotografias atuais?"
        description={
          <div className="flex flex-col gap-2 text-[13px] leading-5 text-fg-muted">
            <p>O novo conjunto substituirá as fotografias atuais deste equipamento.</p>
            <p>Registos e documentos históricos continuarão associados às fotografias utilizadas na altura.</p>
          </div>
        }
        confirmLabel="Guardar fotografias"
        loading={mutation.isPending}
        error={error}
        onConfirm={() => mutation.mutate()}
      />
    </>
  )
}
