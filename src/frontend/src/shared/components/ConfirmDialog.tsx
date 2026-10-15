import { useId, useRef, type ReactNode } from 'react'
import { CircleAlert } from 'lucide-react'
import { Dialog } from './Dialog'
import { Button } from './Button'

interface ConfirmDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: ReactNode
  confirmLabel?: string
  cancelLabel?: string
  destructive?: boolean
  loading?: boolean
  /**
   * An expected business-rule failure from the confirmed action, shown INSIDE
   * the dialog. Anything rendered on the page behind the dialog sits under its
   * overlay and is invisible, which made a rejected action look like a button
   * that did nothing.
   */
  error?: string | null
  onConfirm: () => void
}

/**
 * Calm confirmation for destructive/irreversible actions (Phase 9 scope).
 * An `alertdialog` whose initial focus is the safe choice (Cancelar), never the
 * confirming action or the close button.
 */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel = 'Confirmar',
  cancelLabel = 'Cancelar',
  destructive = false,
  loading = false,
  error,
  onConfirm,
}: ConfirmDialogProps) {
  const cancelRef = useRef<HTMLButtonElement>(null)
  const descriptionId = useId()

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={title}
      size="sm"
      role="alertdialog"
      aria-describedby={descriptionId}
      onOpenAutoFocus={(event) => {
        event.preventDefault()
        cancelRef.current?.focus()
      }}
      footer={
        <>
          <Button
            ref={cancelRef}
            variant="secondary"
            onClick={() => onOpenChange(false)}
            disabled={loading}
          >
            {cancelLabel}
          </Button>
          <Button
            variant={destructive ? 'danger' : 'primary'}
            onClick={onConfirm}
            loading={loading}
          >
            {confirmLabel}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <div id={descriptionId}>
          {typeof description === 'string' ? (
            <p className="text-[13px] leading-5 text-fg-muted">{description}</p>
          ) : (
            description
          )}
        </div>
        {error && (
          <div
            className="flex items-start gap-2.5 rounded-md border border-danger-border bg-danger-bg px-3 py-2.5 text-[13px] leading-[19px] text-danger-fg"
            role="alert"
          >
            <CircleAlert className="mt-px size-4 shrink-0 text-danger" aria-hidden />
            <p className="min-w-0 break-words whitespace-pre-line">{error}</p>
          </div>
        )}
      </div>
    </Dialog>
  )
}
