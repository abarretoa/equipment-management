import { useRef, type ReactNode } from 'react'
import * as RadixDialog from '@radix-ui/react-dialog'
import { X } from 'lucide-react'
import { IconButton } from './IconButton'
import { cn } from '../utils/cn'

interface DialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: string
  children: ReactNode
  footer?: ReactNode
  size?: 'sm' | 'md' | 'lg'
  /** `alertdialog` for confirmations that interrupt the user (see ConfirmDialog). */
  role?: 'dialog' | 'alertdialog'
  /** Overrides where focus lands on open (Radix default: the first focusable element). */
  onOpenAutoFocus?: (event: Event) => void
  /** Explicit description id, for dialogs whose description is rich content rendered in the body. */
  'aria-describedby'?: string
}

const sizeClasses = { sm: 'max-w-[440px]', md: 'max-w-lg', lg: 'max-w-2xl' }

/** Focused confirmation / small-edit surface (Radix gives correct focus trap + ARIA for free). */
export function Dialog({
  open,
  onOpenChange,
  title,
  description,
  children,
  footer,
  size = 'md',
  role,
  onOpenAutoFocus,
  'aria-describedby': ariaDescribedBy,
}: DialogProps) {
  // Every caller opens its dialog from an ordinary button, not a Radix Trigger,
  // so Radix has no trigger to return focus to. Remember what had focus at open
  // time (FocusScope fires onOpenAutoFocus before moving focus) and restore it.
  const returnFocusRef = useRef<HTMLElement | null>(null)

  return (
    <RadixDialog.Root open={open} onOpenChange={onOpenChange}>
      <RadixDialog.Portal>
        <RadixDialog.Overlay className="fixed inset-0 z-40 bg-overlay motion-safe:animate-fade-in" />
        <RadixDialog.Content
          // Only override Radix's own role/describedby when asked: passing undefined would erase them.
          {...(role ? { role } : {})}
          {...(ariaDescribedBy ? { 'aria-describedby': ariaDescribedBy } : {})}
          onOpenAutoFocus={(event) => {
            returnFocusRef.current =
              document.activeElement instanceof HTMLElement ? document.activeElement : null
            onOpenAutoFocus?.(event)
          }}
          onCloseAutoFocus={(event) => {
            const target = returnFocusRef.current
            if (target?.isConnected) {
              event.preventDefault()
              target.focus()
            }
          }}
          className={cn(
            'fixed top-1/2 left-1/2 z-50 flex max-h-[calc(100dvh-2rem)] w-[calc(100vw-2rem)] -translate-x-1/2 -translate-y-1/2 flex-col',
            'rounded-xl bg-surface-raised shadow-lg focus:outline-none motion-safe:animate-fade-in',
            sizeClasses[size],
          )}
        >
          <div className="flex items-start justify-between gap-3 px-5 pt-5">
            <div className="min-w-0">
              <RadixDialog.Title className="text-base leading-6 font-semibold text-fg">
                {title}
              </RadixDialog.Title>
              {description && (
                <RadixDialog.Description className="mt-1 text-[13px] leading-5 text-fg-muted">
                  {description}
                </RadixDialog.Description>
              )}
            </div>
            <RadixDialog.Close asChild>
              <IconButton
                icon={<X className="size-4" />}
                label="Fechar"
                size="sm"
                className="-mt-1 -mr-1"
              />
            </RadixDialog.Close>
          </div>
          <div className="min-h-0 overflow-y-auto px-5 pt-3 pb-5">{children}</div>
          {footer && (
            <div className="flex flex-wrap justify-end gap-2 border-t border-border px-5 py-3">
              {footer}
            </div>
          )}
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  )
}
