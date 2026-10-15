import { Outlet } from 'react-router'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'
import { MobileNav } from './MobileNav'

/**
 * Wide viewports (lg, ≥1024px): permanent 224px dark rail + 48px top bar.
 * Narrower: 56px dark header with a navigation drawer — no icon-only rail.
 */
export function AppLayout() {
  return (
    <div className="flex h-dvh flex-col overflow-hidden bg-bg lg:flex-row">
      <Sidebar className="hidden lg:flex" />
      <MobileNav className="flex lg:hidden" />
      <div className="flex min-h-0 min-w-0 flex-1 flex-col">
        <Topbar className="hidden lg:flex" />
        <main className="flex-1 overflow-y-auto">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
