import type { ReactNode } from 'react'
import { Inbox } from 'lucide-react'
import { cn } from '../utils/cn'

interface EmptyStateProps {
  icon?: ReactNode
  title: string
  description?: string
  /** A real, optional next step — never decoration. */
  action?: ReactNode
  /** Icon tile tint: `warning` / `danger` for blocking states. */
  tone?: 'neutral' | 'warning' | 'danger'
}

const toneClasses = {
  neutral: 'border-border bg-surface text-fg-muted',
  warning: 'border-warning-border bg-warning-bg text-warning',
  danger: 'border-danger-border bg-danger-bg text-danger',
} as const

/** Explains what's missing and what to do next — never a bare "no data" (Phase 9 scope). Icon tile + title + guidance + optional action; no illustration. */
export function EmptyState({
  icon,
  title,
  description,
  action,
  tone = 'neutral',
}: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center px-6 py-12 text-center">
      <div
        className={cn(
          'flex size-10 items-center justify-center rounded-lg border [&_svg]:size-[18px]',
          toneClasses[tone],
        )}
      >
        {icon ?? <Inbox aria-hidden />}
      </div>
      <p className="mt-4 text-[15px] leading-[21px] font-semibold text-fg">{title}</p>
      {description && (
        <p className="mt-1 max-w-sm text-[13px] leading-5 text-fg-muted">{description}</p>
      )}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}
