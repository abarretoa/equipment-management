import { useState } from 'react'
import { Trash2 } from 'lucide-react'
import { IconButton } from '../../shared/components/IconButton'
import { FileUploadArea } from '../../shared/components/FileUploadArea'
import { PhotoPrivacyHint } from '../../shared/components/PhotoPrivacyHint'
import { PhotoThumbnailButton, PhotoViewer } from '../../shared/components/PhotoViewer'
import { Skeleton } from '../../shared/components/Skeleton'
import { usePrivateImageUrl } from '../../shared/hooks/usePrivateImageUrl'
import type { InspectionPhoto } from './types'

const blobKey = (photoId: string) => ['photo-blob', photoId] as const

interface PhotoThumbnailProps {
  photo: InspectionPhoto
  label: string
  getBlob: (photoId: string) => Promise<Blob>
  onOpen: () => void
  onDelete?: (photoId: string) => void
  deleting?: boolean
}

function PhotoThumbnail({ photo, label, getBlob, onOpen, onDelete, deleting }: PhotoThumbnailProps) {
  const { url } = usePrivateImageUrl(blobKey(photo.id), () => getBlob(photo.id))

  return (
    <div className="relative aspect-square overflow-hidden rounded-md border border-border bg-surface-sunken">
      <PhotoThumbnailButton label={`Ampliar ${label}`} onOpen={onOpen} className="focus-visible:-outline-offset-2">
        {url ? <img src={url} alt="" className="size-full object-cover" /> : <Skeleton className="size-full" />}
      </PhotoThumbnailButton>
      {/* A sibling of the thumbnail button (never nested in it), and always visible — never hover-only. */}
      {onDelete && (
        <IconButton
          icon={<Trash2 className="size-3.5" />}
          label={`Remover fotografia ${photo.originalFileName}`}
          size="sm"
          disabled={deleting}
          onClick={() => onDelete(photo.id)}
          className="absolute top-1 right-1 bg-surface/90 shadow-sm"
        />
      )}
    </div>
  )
}

interface PhotoGalleryProps {
  photos: InspectionPhoto[]
  getBlob: (photoId: string) => Promise<Blob>
  onUpload?: (file: File) => void
  onDelete?: (photoId: string) => void
  uploading?: boolean
  deletingId?: string
  hint: string
  editable: boolean
  /** Heading shown in the PhotoViewer. */
  viewerTitle?: string
}

export function PhotoGallery({
  photos,
  getBlob,
  onUpload,
  onDelete,
  uploading,
  deletingId,
  hint,
  editable,
  viewerTitle = 'Fotografias de evidência',
}: PhotoGalleryProps) {
  const [viewerIndex, setViewerIndex] = useState<number | null>(null)
  const labelOf = (photo: InspectionPhoto, index: number) =>
    `Fotografia ${index + 1} de ${photos.length} (${photo.originalFileName})`

  return (
    <div className="flex flex-col gap-3">
      {photos.length > 0 && (
        <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 md:grid-cols-5">
          {photos.map((photo, index) => (
            <PhotoThumbnail
              key={photo.id}
              photo={photo}
              label={labelOf(photo, index)}
              getBlob={getBlob}
              onOpen={() => setViewerIndex(index)}
              onDelete={editable ? onDelete : undefined}
              deleting={deletingId === photo.id}
            />
          ))}
        </div>
      )}
      {editable && onUpload && (
        <>
          <FileUploadArea
            accept="image/jpeg,image/png,image/webp"
            hint={hint}
            disabled={uploading}
            onFileSelected={onUpload}
          />
          <PhotoPrivacyHint />
        </>
      )}
      <PhotoViewer
        title={viewerTitle}
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
