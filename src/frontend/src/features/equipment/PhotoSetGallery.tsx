import { useState } from 'react'
import { Skeleton } from '../../shared/components/Skeleton'
import { PhotoThumbnailButton, PhotoViewer } from '../../shared/components/PhotoViewer'
import { usePrivateImageUrl } from '../../shared/hooks/usePrivateImageUrl'
import type { EquipmentPhoto } from './types'

const blobKey = (photoId: string) => ['equipment-photo-blob', photoId] as const

function PhotoThumbnail({
  photo,
  label,
  getBlob,
  onOpen,
}: {
  photo: EquipmentPhoto
  label: string
  getBlob: (photoId: string) => Promise<Blob>
  onOpen: () => void
}) {
  const { url } = usePrivateImageUrl(blobKey(photo.id), () => getBlob(photo.id))

  return (
    <div className="aspect-[4/3] overflow-hidden rounded-md border border-border bg-surface-sunken">
      <PhotoThumbnailButton
        label={`Ampliar ${label}`}
        onOpen={onOpen}
        className="focus-visible:-outline-offset-2"
      >
        {url ? (
          <img src={url} alt="" className="size-full object-cover" />
        ) : (
          <Skeleton className="size-full" />
        )}
      </PhotoThumbnailButton>
    </div>
  )
}

/**
 * Read-only display of an equipment's 3 current condition photos; each opens
 * the shared PhotoViewer. Historical (superseded) photo sets are preserved in
 * the backend but are not surfaced here.
 */
export function PhotoSetGallery({
  photos,
  getBlob,
}: {
  photos: EquipmentPhoto[]
  getBlob: (photoId: string) => Promise<Blob>
}) {
  const [viewerIndex, setViewerIndex] = useState<number | null>(null)
  const labelOf = (photo: EquipmentPhoto, index: number) =>
    `Fotografia ${index + 1} de ${photos.length} (${photo.originalFileName})`

  return (
    <>
      <div className="grid max-w-xl grid-cols-3 gap-2.5">
        {photos.map((photo, index) => (
          <PhotoThumbnail
            key={photo.id}
            photo={photo}
            label={labelOf(photo, index)}
            getBlob={getBlob}
            onOpen={() => setViewerIndex(index)}
          />
        ))}
      </div>
      <PhotoViewer
        title="Fotografias atuais"
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
    </>
  )
}
