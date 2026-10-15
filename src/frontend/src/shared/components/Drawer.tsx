import { useRef, type ReactNode } from 'react'
import * as RadixDialog from '@radix-ui/react-dialog'
import { X } from 'lucide-react'
import { IconButton } from './IconButton'

interface DrawerProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  children: ReactNode
  footer?: ReactNode
}

/** Right-side sheet for a workflow step that needs more room than a Dialog but shouldn't leave the list context. */
export function Drawer({ open, onOpenChange, title, children, footer }: DrawerProps) {
  // Opened from row clicks / plain buttons, not a Radix Trigger, so Radix has
  // nothing to return focus to: remember the opener and restore it (as Dialog does).
  const returnFocusRef = useRef<HTMLElement | null>(null)

  return (
    <RadixDialog.Root open={open} onOpenChange={onOpenChange}>
      <RadixDialog.Portal>
        <RadixDialog.Overlay className="fixed inset-0 z-40 bg-black/40" />
        <RadixDialog.Content
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
          className="fixed top-0 right-0 z-50 flex h-full w-full max-w-md flex-col border-l border-border bg-surface-raised shadow-lg"
        >
          <div className="flex items-center justify-between gap-4 border-b border-border px-5 py-4">
            <RadixDialog.Title className="text-sm font-semibold text-fg">{title}</RadixDialog.Title>
            <RadixDialog.Close asChild>
              <IconButton icon={<X className="size-4" />} label="Fechar" />
            </RadixDialog.Close>
          </div>
          <div className="flex-1 overflow-y-auto px-5 py-4">{children}</div>
          {footer && (
            <div className="flex justify-end gap-2 border-t border-border px-5 py-3.5">
              {footer}
            </div>
          )}
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  )
}
