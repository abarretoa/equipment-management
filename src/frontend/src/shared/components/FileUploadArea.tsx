import { useRef, useState, type DragEvent } from 'react'
import { UploadCloud } from 'lucide-react'
import { cn } from '../utils/cn'

interface FileUploadAreaProps {
  accept: string
  hint: string
  onFileSelected: (file: File) => void
  disabled?: boolean
  multiple?: boolean
}

/** Drag/drop + click-to-browse target used for photos, signed PDFs, and Excel import files alike. */
export function FileUploadArea({ accept, hint, onFileSelected, disabled, multiple }: FileUploadAreaProps) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [isDragging, setIsDragging] = useState(false)

  function handleFiles(files: FileList | null) {
    if (!files || files.length === 0) return
    if (multiple) {
      Array.from(files).forEach(onFileSelected)
    } else {
      const file = files[0]
      if (file) onFileSelected(file)
    }
  }

  function handleDrop(event: DragEvent<HTMLDivElement>) {
    event.preventDefault()
    setIsDragging(false)
    if (disabled) return
    handleFiles(event.dataTransfer.files)
  }

  return (
    <div
      role="button"
      tabIndex={disabled ? -1 : 0}
      onClick={() => !disabled && inputRef.current?.click()}
      onKeyDown={(event) => {
        if (!disabled && (event.key === 'Enter' || event.key === ' ')) inputRef.current?.click()
      }}
      onDragOver={(event) => {
        event.preventDefault()
        if (!disabled) setIsDragging(true)
      }}
      onDragLeave={() => setIsDragging(false)}
      onDrop={handleDrop}
      aria-disabled={disabled}
      className={cn(
        'flex min-h-14 cursor-pointer flex-col items-center justify-center gap-1.5 rounded-md border border-dashed px-4 py-6 text-center',
        'transition-colors duration-[120ms] motion-reduce:transition-none',
        disabled && 'cursor-not-allowed opacity-50',
        isDragging
          ? 'border-primary bg-surface-selected'
          : 'border-border-strong bg-surface hover:border-fg-muted hover:bg-surface-sunken',
      )}
    >
      <UploadCloud className="size-5 text-fg-muted" aria-hidden />
      <p className="text-[13px] font-medium text-fg">Arraste um ficheiro ou clique para escolher</p>
      <p className="text-[12px] text-fg-muted">{hint}</p>
      <input
        ref={inputRef}
        type="file"
        accept={accept}
        multiple={multiple}
        disabled={disabled}
        className="hidden"
        onChange={(event) => {
          handleFiles(event.target.files)
          event.target.value = ''
        }}
      />
    </div>
  )
}
