import {
  useRef,
  useState,
  type ButtonHTMLAttributes,
  type KeyboardEvent,
  type ReactNode,
} from 'react'
import * as RadixDialog from '@radix-ui/react-dialog'
import { ChevronLeft, ChevronRight, FileImage, Maximize2, Minus, Plus, X } from 'lucide-react'
import { usePrivateImageUrl } from '../hooks/usePrivateImageUrl'
import { cn } from '../utils/cn'

export interface ViewerPhoto {
  id: string
  /** Accessible description of the image (also its <img alt>). */
  alt: string
  /** Stored file name. Not shown in the viewer: stored names are often generated (hashes). */
  fileName: string
}

interface PhotoViewerProps {
  /** Gallery heading shown in the viewer, e.g. "Fotografias atuais". */
  title: string
  photos: ViewerPhoto[]
  /** Index of the open photo; `null` = closed. */
  index: number | null
  onIndexChange: (index: number | null) => void
  /** Same key the gallery thumbnails use, so the viewer reuses their already-fetched blob. */
  blobKey: (photoId: string) => readonly unknown[]
  /** The gallery's authenticated `.../content` loader — the viewer never builds its own URL. */
  getBlob: (photoId: string) => Promise<Blob>
}

/** Restrained, bounded zoom steps (50%–300%). */
const ZOOM_STEPS = [0.5, 0.75, 1, 1.25, 1.5, 2, 2.5, 3] as const
const DEFAULT_ZOOM_INDEX = ZOOM_STEPS.indexOf(1)

/** Previous/next below lg: a visible filled surface, not a bare glyph. */
const NAV_BUTTON_MOBILE = 'max-lg:border max-lg:border-rail-border max-lg:bg-rail-active'

/**
 * The one shared lightbox for private operational photos (Equipment current
 * photos, Return evidence, historical Handover evidence). Presentation only:
 * it receives the gallery's own authenticated loader and never uploads,
 * deletes or edits anything.
 */
export function PhotoViewer({
  title,
  photos,
  index,
  onIndexChange,
  blobKey,
  getBlob,
}: PhotoViewerProps) {
  const [zoomIndex, setZoomIndex] = useState(DEFAULT_ZOOM_INDEX)
  // Callers open the viewer from an ordinary thumbnail button (not a Radix
  // Trigger), so remember the thumbnail at open time and restore focus to it
  // on close — the same contract as the shared Dialog.
  const returnFocusRef = useRef<HTMLElement | null>(null)

  const open = index !== null && photos.length > 0
  const current = open ? photos[Math.min(index, photos.length - 1)] : null
  const position = open ? Math.min(index, photos.length - 1) : 0
  const hasMany = photos.length > 1
  const zoom = ZOOM_STEPS[zoomIndex] ?? 1

  function go(delta: number) {
    if (!hasMany) return
    setZoomIndex(DEFAULT_ZOOM_INDEX)
    onIndexChange((position + delta + photos.length) % photos.length)
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'ArrowRight') {
      event.preventDefault()
      go(1)
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault()
      go(-1)
    }
  }

  return (
    <RadixDialog.Root
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          setZoomIndex(DEFAULT_ZOOM_INDEX)
          onIndexChange(null)
        }
      }}
    >
      <RadixDialog.Portal>
        <RadixDialog.Overlay className="fixed inset-0 z-40 bg-overlay motion-safe:animate-fade-in" />
        <RadixDialog.Content
          aria-describedby={undefined}
          onKeyDown={handleKeyDown}
          onOpenAutoFocus={() => {
            returnFocusRef.current =
              document.activeElement instanceof HTMLElement ? document.activeElement : null
          }}
          onCloseAutoFocus={(event) => {
            const target = returnFocusRef.current
            if (target?.isConnected) {
              event.preventDefault()
              target.focus()
            }
          }}
          className={cn(
            'fixed inset-0 z-50 flex flex-col bg-rail text-rail-fg-active focus:outline-none',
            'sm:inset-4 sm:rounded-xl sm:shadow-lg',
            '[--color-focus-ring:var(--color-rail-focus)] motion-safe:animate-fade-in',
          )}
        >
          <div className="flex items-center gap-3 border-b border-rail-border py-2 pr-2 pl-4">
            <RadixDialog.Title className="min-w-0 flex-1 truncate text-[14px] leading-5 font-semibold">
              {title}
            </RadixDialog.Title>
            <RadixDialog.Close asChild>
              <ViewerButton label="Fechar visualizador">
                <X className="size-4.5" aria-hidden />
              </ViewerButton>
            </RadixDialog.Close>
          </div>

          <div className="relative min-h-0 flex-1">
            {current && (
              <ViewerImage
                key={current.id}
                photo={current}
                zoom={zoom}
                queryKey={blobKey(current.id)}
                load={() => getBlob(current.id)}
              />
            )}
          </div>

          {/* Below lg, a multi-photo gallery gets its own navigation row — filled
              previous/next buttons at the edges, counter centred — with zoom on a
              second row, so navigation never reads as part of the zoom toolbar. */}
          <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-t border-rail-border px-2 py-2">
            <div
              className={cn(
                'flex items-center gap-1',
                hasMany && 'max-lg:w-full max-lg:justify-between',
              )}
            >
              {hasMany && (
                <ViewerButton
                  label="Fotografia anterior"
                  onClick={() => go(-1)}
                  className={NAV_BUTTON_MOBILE}
                >
                  <ChevronLeft className="size-4.5" aria-hidden />
                </ViewerButton>
              )}
              <p
                className="min-w-14 px-1 text-center text-[13px] tabular-nums max-lg:text-[14px] max-lg:font-medium"
                aria-live="polite"
              >
                {position + 1} de {photos.length}
              </p>
              {hasMany && (
                <ViewerButton
                  label="Fotografia seguinte"
                  onClick={() => go(1)}
                  className={NAV_BUTTON_MOBILE}
                >
                  <ChevronRight className="size-4.5" aria-hidden />
                </ViewerButton>
              )}
            </div>
            <div
              className={cn(
                'flex items-center gap-1',
                hasMany && 'max-lg:w-full max-lg:justify-center',
              )}
              role="group"
              aria-label="Zoom"
            >
              <ViewerButton
                label="Reduzir zoom"
                disabled={zoomIndex === 0}
                onClick={() => setZoomIndex((z) => Math.max(0, z - 1))}
              >
                <Minus className="size-4" aria-hidden />
              </ViewerButton>
              <button
                type="button"
                aria-label={`Repor zoom (atual ${Math.round(zoom * 100)}%)`}
                onClick={() => setZoomIndex(DEFAULT_ZOOM_INDEX)}
                className="h-11 min-w-16 rounded-md px-2 text-[13px] tabular-nums text-rail-fg-active transition-colors hover:bg-rail-hover motion-reduce:transition-none lg:h-8"
              >
                {Math.round(zoom * 100)}%
              </button>
              <ViewerButton
                label="Aumentar zoom"
                disabled={zoomIndex === ZOOM_STEPS.length - 1}
                onClick={() => setZoomIndex((z) => Math.min(ZOOM_STEPS.length - 1, z + 1))}
              >
                <Plus className="size-4" aria-hidden />
              </ViewerButton>
            </div>
          </div>
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  )
}

function ViewerImage({
  photo,
  zoom,
  queryKey,
  load,
}: {
  photo: ViewerPhoto
  zoom: number
  queryKey: readonly unknown[]
  load: () => Promise<Blob>
}) {
  const { url, failed } = usePrivateImageUrl(queryKey, load)

  if (failed) {
    return (
      <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-[13px] text-rail-fg">
        <FileImage className="size-6" aria-hidden />
        <p>Não foi possível carregar a fotografia.</p>
      </div>
    )
  }
  if (!url) {
    return (
      <div className="absolute inset-0 flex items-center justify-center" role="status">
        <span className="sr-only">A carregar fotografia…</span>
        <div className="size-8 animate-pulse rounded bg-rail-active" aria-hidden />
      </div>
    )
  }
  return (
    // Scrolls only when zoomed past the viewport; the image itself is always
    // `object-contain`, so at 100% it is never cropped to fill the screen.
    <div
      className="absolute inset-0 flex overflow-auto p-2 sm:p-4"
      data-testid="photo-viewer-stage"
    >
      <img
        src={url}
        alt={photo.alt}
        className="m-auto max-w-none shrink-0 object-contain"
        style={{ width: `${zoom * 100}%`, height: `${zoom * 100}%` }}
      />
    </div>
  )
}

function ViewerButton({
  label,
  children,
  className,
  ...props
}: { label: string; children: ReactNode } & ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      className={cn(
        'inline-flex size-11 shrink-0 items-center justify-center rounded-md text-rail-fg-active lg:size-8',
        'transition-colors hover:bg-rail-hover motion-reduce:transition-none',
        'disabled:cursor-not-allowed disabled:text-rail-fg-muted disabled:hover:bg-transparent',
        className,
      )}
      {...props}
    >
      {children}
    </button>
  )
}

/**
 * A thumbnail as a real button that opens the PhotoViewer: pointer, keyboard
 * focus, a subtle hover/focus treatment and a small expand mark in the
 * corner — never a large overlay covering the image.
 */
export function PhotoThumbnailButton({
  label,
  onOpen,
  className,
  children,
}: {
  /** Accessible name, e.g. "Ampliar fotografia 1 de 3 (frente.jpg)". */
  label: string
  onOpen: () => void
  className?: string
  children: ReactNode
}) {
  return (
    <button
      type="button"
      aria-label={label}
      onClick={onOpen}
      className={cn(
        'group/thumb relative block size-full cursor-zoom-in overflow-hidden',
        'transition-[filter] duration-[120ms] hover:brightness-95 motion-reduce:transition-none',
        className,
      )}
    >
      {children}
      <span
        aria-hidden
        className="absolute right-1.5 bottom-1.5 inline-flex size-6 items-center justify-center rounded bg-surface/90 text-fg shadow-sm opacity-80 transition-opacity group-hover/thumb:opacity-100 group-focus-visible/thumb:opacity-100"
      >
        <Maximize2 className="size-3.5" />
      </span>
    </button>
  )
}
