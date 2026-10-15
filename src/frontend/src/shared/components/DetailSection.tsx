import type { ReactNode } from 'react'
import { cn } from '../utils/cn'

interface DetailSectionProps {
  title: string
  description?: string
  actions?: ReactNode
  children: ReactNode
  /**
   * `panel` (default): bordered panel with a header row — every current caller.
   * `ruled`: Visual System v1 detail layout — no panel, a hairline between
   * sections, title column (≈184px) beside the content on wide screens and
   * stacked above it on narrow ones.
   */
  variant?: 'panel' | 'ruled'
  className?: string
}

/** Titled section wrapper for a detail page (e.g. "Inspeção de entrega", "Fotografias"). */
export function DetailSection({
  title,
  description,
  actions,
  children,
  variant = 'panel',
  className,
}: DetailSectionProps) {
  if (variant === 'ruled') {
    return (
      <section
        className={cn(
          'grid gap-x-6 gap-y-3 border-b border-border py-5 md:grid-cols-[184px_minmax(0,1fr)]',
          className,
        )}
      >
        <div className="flex flex-col gap-1">
          <h2 className="text-[14px] leading-5 font-semibold text-fg">{title}</h2>
          {description && <p className="text-[12px] leading-[17px] text-fg-muted">{description}</p>}
          {actions && <div className="mt-2 flex flex-wrap gap-2">{actions}</div>}
        </div>
        <div className="min-w-0">{children}</div>
      </section>
    )
  }

  return (
    <section className={cn('rounded-lg border border-border bg-surface', className)}>
      <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2 border-b border-border px-4 py-3">
        <div className="min-w-0">
          <h2 className="text-[14px] leading-5 font-semibold text-fg">{title}</h2>
          {description && (
            <p className="mt-0.5 text-[12px] leading-[17px] text-fg-muted">{description}</p>
          )}
        </div>
        {actions}
      </div>
      <div className="p-4">{children}</div>
    </section>
  )
}
