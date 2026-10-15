import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router'
import { Loader2 } from 'lucide-react'
import { useAuth } from './AuthContext'
import { ForbiddenState } from './ForbiddenState'

/** Full-screen loading guard while the session bootstraps. */
export function AuthGate({ children }: { children: ReactNode }) {
  const { isLoading } = useAuth()

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-bg">
        <Loader2 className="size-6 animate-spin text-fg-subtle" aria-label="A carregar" />
      </div>
    )
  }

  return children
}

/** Redirects to /login (preserving the intended destination) when unauthenticated; forces the change-password flow first. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const location = useLocation()

  if (!user) {
    return <Navigate to="/login" replace state={{ from: location }} />
  }

  if (user.mustChangePassword && location.pathname !== '/alterar-password') {
    return <Navigate to="/alterar-password" replace />
  }

  return children
}

/** Gates a route on a permission (or on ALL of several) — the frontend mirrors access for UX, the API remains the real authority. */
export function RequirePermission({ permission, children }: { permission: string | readonly string[]; children: ReactNode }) {
  const { hasPermission } = useAuth()
  const required = typeof permission === 'string' ? [permission] : permission

  if (!required.every((p) => hasPermission(p))) {
    return <ForbiddenState />
  }

  return children
}
