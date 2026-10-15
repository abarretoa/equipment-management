import {
  CircleAlert,
  CircleCheck,
  CircleDot,
  CircleMinus,
  Info,
  TriangleAlert,
  type LucideIcon,
} from 'lucide-react'
import { cn } from '../utils/cn'

export type StatusTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral' | 'primary'

/**
 * Visual System v1 two-tier rule, chosen from the tone (no screen picks colours):
 * - quiet (success, primary, neutral): icon + ink label, no container — the
 *   common, settled states stay calm;
 * - badge (warning, info, danger): 22px, radius 3, tinted border and fill — the
 *   states that need attention.
 */
const QUIET_TONES: ReadonlySet<StatusTone> = new Set(['success', 'primary', 'neutral'])

const quietIconClasses: Record<StatusTone, string> = {
  success: 'text-success',
  primary: 'text-fg-muted',
  neutral: 'text-fg-subtle',
  warning: 'text-warning',
  info: 'text-info',
  danger: 'text-danger',
}

const badgeClasses: Record<StatusTone, string> = {
  warning: 'border-warning-border bg-warning-bg text-warning',
  info: 'border-info-border bg-info-bg text-info',
  danger: 'border-danger-border bg-danger-bg text-danger-fg',
  success: 'border-success-border bg-success-bg text-success',
  primary: 'border-border bg-surface-selected text-fg',
  neutral: 'border-neutral-border bg-neutral-bg text-neutral',
}

/** Fallback when a status has no icon of its own — distinct per tone, so status never relies on colour alone. */
const toneIcon: Record<StatusTone, LucideIcon> = {
  success: CircleCheck,
  primary: CircleDot,
  neutral: CircleMinus,
  warning: CircleAlert,
  info: Info,
  danger: TriangleAlert,
}

interface StatusBadgeProps {
  label: string
  tone: StatusTone
  /** The status's own icon (see `StatusMeta.icon`); falls back to the tone's icon. */
  icon?: LucideIcon
  className?: string
}

/**
 * The single status-rendering component for the whole product (Phase 9 scope).
 * Status is never conveyed by color alone — every status always pairs an
 * icon AND text (WCAG 1.4.1). Labels never truncate.
 */
export function StatusBadge({ label, tone, icon, className }: StatusBadgeProps) {
  const Icon = icon ?? toneIcon[tone]
  const quiet = QUIET_TONES.has(tone)

  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center gap-1.5 whitespace-nowrap',
        quiet
          ? cn('text-[13px]', tone === 'neutral' ? 'text-fg-muted' : 'text-fg')
          : cn('h-[22px] rounded-sm border px-[7px] text-[12px] font-medium', badgeClasses[tone]),
        className,
      )}
    >
      <Icon className={cn('size-[13px] shrink-0', quiet && quietIconClasses[tone])} aria-hidden />
      {label}
    </span>
  )
}
