import { useRef, type KeyboardEvent } from 'react'
import { Handshake, Truck } from 'lucide-react'
import { cn } from '../../shared/utils/cn'
import { handoverDeliveryMethodLabels } from '../../shared/status/statusMaps'
import type { HandoverDeliveryMethod } from './types'

const OPTIONS: { value: HandoverDeliveryMethod; icon: typeof Truck; hint: string }[] = [
  { value: 'InPerson', icon: Handshake, hint: 'O equipamento é entregue em mão ao consultor. Não há registo de envio.' },
  { value: 'Carrier', icon: Truck, hint: 'O equipamento é enviado. O envio tem de ser registado antes de confirmar a entrega.' },
]

interface DeliveryMethodPickerProps {
  id?: string
  value: HandoverDeliveryMethod | null
  onChange: (value: HandoverDeliveryMethod) => void
  disabled?: boolean
  /**
   * Standard radio-group keyboard model: one Tab stop, and the arrow keys move
   * focus AND select. Only for callers where selecting is local form state (the
   * create form). On an existing Handover every selection is saved immediately,
   * so arrows there would fire a save per keypress; that caller keeps each
   * option as its own Tab stop, activated deliberately with Space/Enter.
   */
  arrowKeySelection?: boolean
}

/** "Método de entrega" as two explicit choices (Iteration 3): never inferred, always chosen. */
export function DeliveryMethodPicker({
  id,
  value,
  onChange,
  disabled = false,
  arrowKeySelection = false,
}: DeliveryMethodPickerProps) {
  const buttonsRef = useRef<(HTMLButtonElement | null)[]>([])
  // With no selection yet, the first option is the group's Tab stop.
  const tabStop = Math.max(0, OPTIONS.findIndex((o) => o.value === value))

  function handleKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const forward = event.key === 'ArrowRight' || event.key === 'ArrowDown'
    const backward = event.key === 'ArrowLeft' || event.key === 'ArrowUp'
    if (!forward && !backward) return
    event.preventDefault()
    const next = (index + (forward ? 1 : -1) + OPTIONS.length) % OPTIONS.length
    const option = OPTIONS[next]
    if (!option) return
    buttonsRef.current[next]?.focus()
    onChange(option.value)
  }

  return (
    <div id={id} role="radiogroup" aria-label="Método de entrega" className="grid gap-2 sm:grid-cols-2">
      {OPTIONS.map(({ value: option, icon: Icon, hint }, index) => {
        const selected = value === option
        return (
          <button
            key={option}
            ref={(el) => {
              buttonsRef.current[index] = el
            }}
            type="button"
            role="radio"
            aria-checked={selected}
            disabled={disabled}
            {...(arrowKeySelection
              ? {
                  tabIndex: index === tabStop ? 0 : -1,
                  onKeyDown: (event: KeyboardEvent<HTMLButtonElement>) => handleKeyDown(event, index),
                }
              : {})}
            onClick={() => onChange(option)}
            className={cn(
              'flex items-start gap-3 rounded-md border px-3 py-2.5 text-left transition-colors',
              'focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-60',
              selected ? 'border-primary bg-primary-subtle' : 'border-border bg-surface hover:border-border-strong',
            )}
          >
            <Icon className={cn('mt-0.5 size-4 shrink-0', selected ? 'text-primary' : 'text-fg-subtle')} aria-hidden />
            <span className="flex flex-col gap-0.5">
              <span className="text-[13px] font-medium text-fg">{handoverDeliveryMethodLabels[option]}</span>
              <span className="text-[12px] text-fg-muted">{hint}</span>
            </span>
          </button>
        )
      })}
    </div>
  )
}
