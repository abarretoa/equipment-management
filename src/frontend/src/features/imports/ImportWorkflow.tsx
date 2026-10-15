import type { ReactNode } from 'react'
import { CircleAlert, CircleCheck, TriangleAlert } from 'lucide-react'
import { StepIndicator } from '../../shared/components/StepIndicator'
import { cn } from '../../shared/utils/cn'

/**
 * Presentation pieces shared by the three Excel import pages (Consultores,
 * Equipamentos, Inventário atual). Each page keeps its own API calls and
 * rules; these only give the same Upload → Preview → Confirm flow the same
 * visual language.
 */

export type ImportStage = 'upload' | 'preview' | 'done'

const STEPS = [
  { key: 'upload', label: 'Carregar ficheiro' },
  { key: 'preview', label: 'Rever e confirmar' },
  { key: 'done', label: 'Concluída' },
]

/** The real stages of every import: a file produces a preview; nothing is written until it is confirmed. */
export function ImportSteps({ stage }: { stage: ImportStage }) {
  const completed = stage === 'upload' ? [] : stage === 'preview' ? ['upload'] : ['upload', 'preview', 'done']
  return <StepIndicator steps={STEPS} currentKey={stage} completedKeys={completed} />
}

/** A titled, ruled block of the page (import or export) — no decorative card. */
export function ImportSection({
  id,
  title,
  description,
  actions,
  children,
}: {
  id: string
  title: string
  description?: ReactNode
  actions?: ReactNode
  children: ReactNode
}) {
  return (
    <section aria-labelledby={id} className="flex flex-col gap-4 border-b border-border py-5 last:border-b-0">
      <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
        <div className="flex min-w-0 flex-col gap-1">
          <h2 id={id} className="text-[15px] leading-5 font-semibold text-fg">
            {title}
          </h2>
          {description && <div className="max-w-2xl text-[13px] leading-5 text-fg-muted">{description}</div>}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
      {children}
    </section>
  )
}

const alertStyles = {
  danger: { icon: CircleAlert, className: 'border-danger-border bg-danger-bg text-danger-fg', iconClass: 'text-danger' },
  warning: { icon: TriangleAlert, className: 'border-warning-border bg-warning-bg text-warning', iconClass: 'text-warning' },
  success: { icon: CircleCheck, className: 'border-success-border bg-success-bg text-fg', iconClass: 'text-success' },
} as const

/** Icon + text feedback block; failures are announced (`alert`), outcomes politely (`status`). */
export function ImportAlert({
  tone,
  title,
  children,
}: {
  tone: keyof typeof alertStyles
  title?: string
  children?: ReactNode
}) {
  const style = alertStyles[tone]
  const Icon = style.icon
  return (
    <div
      role={tone === 'success' ? 'status' : 'alert'}
      className={cn('flex items-start gap-2.5 rounded-md border px-3.5 py-2.5 text-[13px] leading-[19px]', style.className)}
    >
      <Icon className={cn('mt-px size-4 shrink-0', style.iconClass)} aria-hidden />
      <div className="flex min-w-0 flex-col gap-1">
        {title && <p className="font-semibold">{title}</p>}
        {children && <div className="whitespace-pre-line">{children}</div>}
      </div>
    </div>
  )
}

