import { useEffect, useMemo, useRef } from 'react'
import { X } from 'lucide-react'
import { FileUploadArea } from '../../shared/components/FileUploadArea'
import { IconButton } from '../../shared/components/IconButton'
import { PhotoPrivacyHint } from '../../shared/components/PhotoPrivacyHint'

const REQUIRED_PHOTO_COUNT = 3

function Thumbnail({ file, onRemove }: { file: File; onRemove: () => void }) {
  const url = useMemo(() => URL.createObjectURL(file), [file])
  useEffect(() => () => URL.revokeObjectURL(url), [url])

  return (
    <div className="relative aspect-square overflow-hidden rounded-md border border-border bg-surface-sunken">
      <img src={url} alt={file.name} className="size-full object-cover" />
      {/* Always visible (never hover-only): touch and keyboard users must be able to find it. */}
      <IconButton
        icon={<X className="size-3.5" aria-hidden />}
        label={`Remover fotografia ${file.name}`}
        size="sm"
        onClick={onRemove}
        className="absolute top-1 right-1 border-border bg-surface text-fg shadow-sm"
      />
    </div>
  )
}

interface ConditionPhotoPickerProps {
  photos: File[]
  onChange: (photos: File[]) => void
  disabled?: boolean
  error?: string
}

/**
 * Picks exactly 3 condition photos for a new Laptop/MobilePhone equipment (or
 * the legacy backfill action) — a small, focused component, deliberately not
 * a refactor of the existing Handover/Return `PhotoGallery` components (those
 * display already-uploaded server-side photos by id; this one collects local
 * `File`s before the equipment even exists).
 */
export function ConditionPhotoPicker({ photos, onChange, disabled, error }: ConditionPhotoPickerProps) {
  // FileUploadArea invokes onFileSelected once per file, synchronously, when
  // multiple files are chosen at once — several calls land before React
  // re-renders with the new `photos` prop, so accumulating from that prop
  // directly would silently drop all but the last file. This ref mirrors the
  // latest known selection across that synchronous burst.
  const photosRef = useRef(photos)
  useEffect(() => {
    photosRef.current = photos
  }, [photos])

  function addFiles(newFiles: File[]) {
    const merged = [...photosRef.current, ...newFiles].slice(0, REQUIRED_PHOTO_COUNT)
    photosRef.current = merged
    onChange(merged)
  }

  function removeAt(index: number) {
    onChange(photos.filter((_, i) => i !== index))
  }

  return (
    <div className="flex flex-col gap-3">
      {photos.length > 0 && (
        <div className="grid grid-cols-3 gap-2 sm:max-w-md">
          {photos.map((file, index) => (
            <Thumbnail key={`${file.name}-${index}`} file={file} onRemove={() => removeAt(index)} />
          ))}
        </div>
      )}
      {photos.length < REQUIRED_PHOTO_COUNT && (
        <>
          <FileUploadArea
            accept="image/jpeg,image/png,image/webp"
            hint={`São exigidas exatamente ${REQUIRED_PHOTO_COUNT} fotografias (${photos.length}/${REQUIRED_PHOTO_COUNT} selecionadas)`}
            disabled={disabled}
            multiple
            onFileSelected={(file) => addFiles([file])}
          />
          <PhotoPrivacyHint />
        </>
      )}
      {error && (
        <p className="text-[13px] text-danger" role="alert">
          {error}
        </p>
      )}
    </div>
  )
}
