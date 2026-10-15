import { useState } from 'react'
import * as RadixDialog from '@radix-ui/react-dialog'
import { Menu, X } from 'lucide-react'
import { Logo } from '../branding/Logo'
import { cn } from '../shared/utils/cn'
import { NavigationList } from './Sidebar'
import { UserMenu } from './UserMenu'

/**
 * Narrow-viewport shell: a 56px dark header (menu, lockup, account) and a
 * left navigation drawer with exactly the rail's sections and permission
 * filtering. Radix Dialog provides the focus trap, Escape to close and focus
 * return to the menu button.
 */
export function MobileNav({ className }: { className?: string }) {
  const [open, setOpen] = useState(false)

  return (
    <header
      className={cn(
        'h-14 shrink-0 items-center gap-1 bg-rail px-1 [--color-focus-ring:var(--color-rail-focus)]',
        className,
      )}
    >
      <RadixDialog.Root open={open} onOpenChange={setOpen}>
        <RadixDialog.Trigger asChild>
          <button
            type="button"
            aria-label="Abrir navegação"
            className="flex size-11 items-center justify-center rounded-md text-rail-fg-active hover:bg-rail-hover"
          >
            <Menu className="size-5" aria-hidden />
          </button>
        </RadixDialog.Trigger>
        <RadixDialog.Portal>
          <RadixDialog.Overlay className="fixed inset-0 z-40 bg-overlay motion-safe:animate-fade-in" />
          <RadixDialog.Content
            aria-describedby={undefined}
            className="fixed inset-y-0 left-0 z-50 flex w-74 max-w-[calc(100vw-3rem)] flex-col bg-rail [--color-focus-ring:var(--color-rail-focus)] focus:outline-none motion-safe:animate-drawer-in"
          >
            <RadixDialog.Title className="sr-only">Navegação principal</RadixDialog.Title>
            <div className="flex h-14 shrink-0 items-center border-b border-rail-border pr-2 pl-4">
              <Logo size="sm" tone="dark" />
              <RadixDialog.Close asChild>
                <button
                  type="button"
                  aria-label="Fechar navegação"
                  className="ml-auto flex size-11 items-center justify-center rounded-md text-rail-fg-active hover:bg-rail-hover"
                >
                  <X className="size-[18px]" aria-hidden />
                </button>
              </RadixDialog.Close>
            </div>
            <div className="min-h-0 flex-1 overflow-y-auto">
              <NavigationList variant="drawer" onNavigate={() => setOpen(false)} />
            </div>
          </RadixDialog.Content>
        </RadixDialog.Portal>
      </RadixDialog.Root>

      <Logo size="md" tone="dark" className="ml-1" />

      <div className="ml-auto">
        <UserMenu compact />
      </div>
    </header>
  )
}
