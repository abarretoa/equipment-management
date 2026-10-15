import { ChevronDown, LogOut, UserRound } from 'lucide-react'
import { useNavigate } from 'react-router'
import { DropdownMenu } from '../shared/components/DropdownMenu'
import { useAuth } from '../features/auth/AuthContext'
import { roleLabels } from '../shared/status/statusMaps'

/**
 * The account menu — the top bar's only content (no global search, no
 * notifications: neither exists in the product). `compact` is the avatar-only
 * 44px trigger used on the dark mobile header.
 */
export function UserMenu({ compact = false }: { compact?: boolean }) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  if (!user) return null

  const initials = `${user.firstName.charAt(0)}${user.lastName.charAt(0)}`.toUpperCase()
  const fullName = `${user.firstName} ${user.lastName}`
  const roleLabel = roleLabels[user.role] ?? user.role

  return (
    <DropdownMenu
      align="end"
      trigger={
        compact ? (
          <button
            type="button"
            aria-label={`Menu do utilizador: ${fullName}`}
            className="flex size-11 items-center justify-center rounded-md hover:bg-rail-hover"
          >
            <span className="flex size-7 items-center justify-center rounded-md bg-rail-active text-[11px] font-semibold text-rail-fg-active">
              {initials}
            </span>
          </button>
        ) : (
          <button
            type="button"
            className="flex h-9 items-center gap-2 rounded-md px-2 transition-colors duration-[120ms] hover:bg-surface-selected motion-reduce:transition-none"
          >
            <span
              aria-hidden
              className="flex size-7 items-center justify-center rounded-md bg-primary text-[11px] font-semibold text-primary-fg"
            >
              {initials}
            </span>
            <span className="flex flex-col items-start leading-[15px]">
              <span className="text-[13px] font-medium text-fg">{fullName}</span>
              <span className="text-[11px] text-fg-muted">{roleLabel}</span>
            </span>
            <ChevronDown className="size-3.5 text-fg-muted" aria-hidden />
          </button>
        )
      }
      items={[
        {
          label: 'Meu perfil',
          icon: <UserRound className="size-4" aria-hidden />,
          onSelect: () => void navigate('/perfil'),
        },
        {
          label: 'Terminar sessão',
          icon: <LogOut className="size-4" aria-hidden />,
          destructive: true,
          onSelect: () => {
            void logout().then(() => navigate('/login', { replace: true }))
          },
        },
      ]}
    />
  )
}
