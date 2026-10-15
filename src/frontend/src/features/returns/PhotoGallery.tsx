import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Trash2, FileImage } from 'lucide-react'
import { FileUploadArea } from '../../shared/components/FileUploadArea'
import { IconButton } from '../../shared/components/IconButton'
import { PhotoPrivacyHint } from '../../shared/components/PhotoPrivacyHint'
import { PhotoThumbnailButton, PhotoViewer } from '../../shared/components/PhotoViewer'
import { usePrivateImageUrl } from '../../shared/hooks/usePrivateImageUrl'
import { ApiError } from '../../shared/api/httpClient'
import { formatBytes } from '../../shared/utils/format'
import type { InspectionPhoto } from '../handovers/types'

interface PhotoGalleryProps {
  /** Gallery name (e.g. "Fotografias da avaria"): the PhotoViewer heading and the blob cache scope. */
  title: string
  photos: InspectionPhoto[]
  editable: boolean
  hint: string
  getBlob: (photoId: string) => Promise<Blob>
  onUpload: (file: File) => Promise<unknown>
  onDelete?: (photoId: string) => Promise<unknown>
  invalidateKey: unknown[]
}

const failureMessage = (error: unknown, fallback: string) =>
  error instanceof ApiError && error.status < 500 ? error.message : fallback

/** Photo grid backed by the authenticated `.../content` endpoints — never a plain <img src> against a private storage key. */
export function PhotoGallery({ title, photos, editable, hint, getBlob, onUpload, onDelete, invalidateKey }: PhotoGalleryProps) {
  const queryClient = useQueryClient()
  const [viewerIndex, setViewerIndex] = useState<number | null>(null)
  const blobKey = (photoId: string) => ['return-photo-blob', title, photoId] as const
  const labelOf = (photo: InspectionPhoto, index: number) =>
    `Fotografia ${index + 1} de ${photos.length} (${photo.originalFileName})`

  const uploadMutation = useMutation({
    mutationFn: onUpload,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: invalidateKey }),
  })

  const deleteMutation = useMutation({
    mutationFn: (photoId: string) => onDelete!(photoId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: invalidateKey }),
  })

  const error = uploadMutation.error
    ? failureMessage(uploadMutation.error, 'Não foi possível carregar a fotografia.')
    : deleteMutation.error
      ? failureMessage(deleteMutation.error, 'Não foi possível remover a fotografia.')
      : null

  return (
    <div className="flex flex-col gap-3">
      {photos.length === 0 ? (
        <p className="text-[13px] text-fg-muted">Nenhuma fotografia carregada.</p>
      ) : (
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4">
          {photos.map((photo, index) => (
            <PhotoThumbnail
              key={photo.id}
              photo={photo}
              label={labelOf(photo, index)}
              blobKey={blobKey(photo.id)}
              getBlob={getBlob}
              onOpen={() => setViewerIndex(index)}
              onDelete={editable && onDelete ? () => { uploadMutation.reset(); deleteMutation.mutate(photo.id) } : undefined}
              deleting={deleteMutation.isPending && deleteMutation.variables === photo.id}
            />
          ))}
        </ul>
      )}
      {editable && (
        <>
          <FileUploadArea
            accept="image/jpeg,image/png,image/webp"
            hint={hint}
            disabled={uploadMutation.isPending}
            onFileSelected={(file) => { deleteMutation.reset(); uploadMutation.mutate(file) }}
          />
          <PhotoPrivacyHint />
        </>
      )}
      {error && (
        <p className="text-[12.5px] text-danger" role="alert">
          {error}
        </p>
      )}
      <PhotoViewer
        title={title}
        photos={photos.map((photo, index) => ({
          id: photo.id,
          alt: labelOf(photo, index),
          fileName: photo.originalFileName,
        }))}
        index={viewerIndex}
        onIndexChange={setViewerIndex}
        blobKey={blobKey}
        getBlob={getBlob}
      />
    </div>
  )
}

function PhotoThumbnail({
  photo,
  label,
  blobKey,
  getBlob,
  onOpen,
  onDelete,
  deleting,
}: {
  photo: InspectionPhoto
  label: string
  blobKey: readonly unknown[]
  getBlob: (photoId: string) => Promise<Blob>
  onOpen: () => void
  onDelete?: () => void
  deleting?: boolean
}) {
  const { url, failed } = usePrivateImageUrl(blobKey, () => getBlob(photo.id))

  return (
    <li className="relative overflow-hidden rounded-md border border-border bg-surface-sunken">
      <div className="aspect-square">
        {failed ? (
          <div className="flex size-full items-center justify-center">
            <FileImage className="size-6 text-fg-subtle" aria-label={`${photo.originalFileName} (indisponível)`} />
          </div>
        ) : (
          <PhotoThumbnailButton label={`Ampliar ${label}`} onOpen={onOpen} className="focus-visible:-outline-offset-2">
            {url ? (
              <img src={url} alt="" className="size-full object-cover" />
            ) : (
              <span className="flex size-full items-center justify-center">
                <span className="size-6 animate-pulse rounded bg-border" />
              </span>
            )}
          </PhotoThumbnailButton>
        )}
      </div>
      <div className="flex items-center gap-1 border-t border-border bg-surface py-1 pr-1 pl-2">
        <div className="min-w-0 flex-1">
          <p className="truncate text-[11px] text-fg-muted" title={photo.originalFileName}>
            {photo.originalFileName}
          </p>
          <p className="text-[10.5px] text-fg-muted">{formatBytes(photo.sizeBytes)}</p>
        </div>
        {/* Always visible (never hover-only): touch and keyboard users must be able to find it. */}
        {onDelete && (
          <IconButton
            icon={<Trash2 className="size-3.5" aria-hidden />}
            label={`Remover fotografia ${photo.originalFileName}`}
            variant="danger"
            size="sm"
            className="max-lg:size-11"
            disabled={deleting}
            onClick={onDelete}
          />
        )}
      </div>
    </li>
  )
}
