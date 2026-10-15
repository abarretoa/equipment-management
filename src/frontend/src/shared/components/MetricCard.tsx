import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { cn } from '../utils/cn'

interface MetricCardProps {
  label: string
  value: string | number
  icon?: ReactNode
  to?: string
  tone?: 'default' | 'warning' | 'danger'
  hint?: string
}

const toneValueClasses: Record<NonNullable<MetricCardProps['tone']>, string> = {
  default: 'text-fg',
  warning: 'text-warning',
  danger: 'text-danger',
}

export function MetricCard({ label, value, icon, to, tone = 'default', hint }: MetricCardProps) {
  const content = (
    <>
      <div className="flex items-center justify-between">
        <span className="text-[13px] font-medium text-fg-muted">{label}</span>
        {icon && <span className="text-fg-subtle">{icon}</span>}
      </div>
      <span className={cn('text-[28px] leading-none font-semibold tabular-nums', toneValueClasses[tone])}>
        {value}
      </span>
      {hint && <span className="text-[12.5px] text-fg-subtle">{hint}</span>}
    </>
  )

  const className =
    'flex flex-col gap-2 rounded-lg border border-border bg-surface p-4 transition-colors'

  if (to) {
    return (
      <Link to={to} className={cn(className, 'hover:border-border-strong hover:bg-surface-sunken')}>
        {content}
      </Link>
    )
  }

  return <div className={className}>{content}</div>
}
