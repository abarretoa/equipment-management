import { NavLink } from 'react-router'
import { Logo } from '../branding/Logo'
import { navSections, type NavSection } from './navigation'
import { useAuth } from '../features/auth/AuthContext'
import { cn } from '../shared/utils/cn'

/** Sections the current user may see — an item needs ALL its permissions; empty sections are dropped. */
function useVisibleNavSections(): NavSection[] {
  const { hasPermission } = useAuth()

  return navSections
    .map((section) => ({
      ...section,
      items: section.items.filter(
        (item) =>
          !item.permission ||
          (typeof item.permission === 'string' ? [item.permission] : item.permission).every((p) =>
            hasPermission(p),
          ),
      ),
    }))
    .filter((section) => section.items.length > 0)
}

interface NavigationListProps {
  /** `rail`: 32px desktop items. `drawer`: 44px touch items. */
  variant: 'rail' | 'drawer'
  /** Called after a destination is chosen (the drawer closes itself with it). */
  onNavigate?: () => void
}

/**
 * The single rendering of `navSections` — used by the desktop rail and the
 * mobile drawer alike, so both always show the same destinations under the
 * same permission rules. Active = filled ink surface + white text + weight 500
 * + brand marker + aria-current="page" (NavLink); the marker is never the only cue.
 */
export function NavigationList({ variant, onNavigate }: NavigationListProps) {
  const sections = useVisibleNavSections()
  const isDrawer = variant === 'drawer'

  return (
    <nav
      aria-label="Navegação principal"
      className={cn('flex flex-col px-2', isDrawer ? 'pt-1 pb-4' : 'pt-1.5 pb-3')}
    >
      {sections.map((section, index) => (
        <div
          key={section.label || 'root'}
          className={cn('flex flex-col gap-px', index > 0 && 'pt-2.5')}
        >
          {section.label && (
            <p
              className={cn(
                'flex px-2.5 text-[11px] font-medium tracking-[0.02em] text-rail-fg-muted',
                isDrawer ? 'h-7 items-end pb-1' : 'h-6 items-center',
              )}
            >
              {section.label}
            </p>
          )}
          <ul className="flex flex-col gap-px">
            {section.items.map((item) => (
              <li key={item.to}>
                <NavLink
                  to={item.to}
                  end={item.to === '/'}
                  onClick={onNavigate}
                  className={({ isActive }) =>
                    cn(
                      'relative flex items-center rounded-md px-2.5 transition-colors duration-[120ms] focus-visible:-outline-offset-2 motion-reduce:transition-none',
                      isDrawer ? 'h-11 gap-3 text-[14px]' : 'h-8 gap-2.5 text-[13px]',
                      isActive
                        ? 'bg-rail-active font-medium text-rail-fg-active'
                        : 'text-rail-fg hover:bg-rail-hover hover:text-rail-fg-active',
                    )
                  }
                >
                  {({ isActive }) => (
                    <>
                      <span
                        aria-hidden
                        className={cn(
                          'absolute -left-2 w-0.5',
                          isDrawer ? 'top-3 h-5' : 'top-2 h-4',
                          isActive ? 'bg-brand-marker' : 'bg-transparent',
                        )}
                      />
                      <item.icon
                        className={cn('shrink-0', isDrawer ? 'size-[18px]' : 'size-4')}
                        aria-hidden
                      />
                      <span className="min-w-0 flex-1 truncate">{item.label}</span>
                    </>
                  )}
                </NavLink>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </nav>
  )
}

/** Desktop navigation rail: permanent 224px dark ink column. */
export function Sidebar({ className }: { className?: string }) {
  return (
    <aside
      className={cn(
        'h-full w-56 shrink-0 flex-col bg-rail [--color-focus-ring:var(--color-rail-focus)]',
        className,
      )}
    >
      <div className="flex h-12 shrink-0 items-center border-b border-rail-border px-4">
        <Logo size="sm" tone="dark" />
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto">
        <NavigationList variant="rail" />
      </div>
    </aside>
  )
}
