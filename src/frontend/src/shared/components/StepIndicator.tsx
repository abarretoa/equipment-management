import { Check } from 'lucide-react'
import { cn } from '../utils/cn'

export interface Step {
  key: string
  label: string
}

interface StepIndicatorProps {
  steps: Step[]
  currentKey: string
  completedKeys: string[]
}

/** Compact horizontal progress for a status-driven workflow (handover/return) — not a consumer onboarding wizard. */
export function StepIndicator({ steps, currentKey, completedKeys }: StepIndicatorProps) {
  return (
    <ol className="flex flex-wrap items-center gap-x-2 gap-y-2" aria-label="Progresso do processo">
      {steps.map((step, index) => {
        const isDone = completedKeys.includes(step.key)
        const isCurrent = step.key === currentKey

        return (
          <li key={step.key} className="flex items-center gap-2">
            <div className="flex items-center gap-2" aria-current={isCurrent ? 'step' : undefined}>
              <span
                className={cn(
                  'flex size-5 shrink-0 items-center justify-center rounded-full border text-[11px] font-semibold tabular-nums',
                  isDone && 'border-primary bg-primary text-primary-fg',
                  isCurrent && !isDone && 'border-primary bg-surface text-fg',
                  !isDone && !isCurrent && 'border-border-control bg-surface text-fg-subtle',
                )}
                aria-hidden
              >
                {isDone ? <Check className="size-3" strokeWidth={3} /> : index + 1}
              </span>
              <span
                className={cn(
                  'text-[13px]',
                  isCurrent ? 'font-semibold text-fg' : isDone ? 'text-fg' : 'text-fg-muted',
                )}
              >
                {step.label}
                {isDone && <span className="sr-only"> (concluído)</span>}
              </span>
            </div>
            {index < steps.length - 1 && (
              <span
                className={cn('h-px w-8', isDone ? 'bg-primary' : 'bg-border-divider')}
                aria-hidden
              />
            )}
          </li>
        )
      })}
    </ol>
  )
}
