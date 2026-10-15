import { cn } from '../shared/utils/cn'
import { UserMenu } from './UserMenu'

/** Desktop top bar: 48px, hairline bottom, the account menu only. */
export function Topbar({ className }: { className?: string }) {
  return (
    <header
      className={cn(
        'h-12 shrink-0 items-center justify-end border-b border-border bg-surface px-4',
        className,
      )}
    >
      <UserMenu />
    </header>
  )
}
