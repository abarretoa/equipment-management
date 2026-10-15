import { forwardRef, type InputHTMLAttributes } from 'react'
import { Check } from 'lucide-react'
import { cn } from '../utils/cn'

interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> {
  label: string
}

export const Checkbox = forwardRef<HTMLInputElement, CheckboxProps>(function Checkbox(
  { label, className, id, ...props },
  ref,
) {
  return (
    <label
      htmlFor={id}
      className={cn('inline-flex cursor-pointer items-center gap-2 text-[13px] text-fg', className)}
    >
      <span className="relative inline-flex size-4 shrink-0 items-center justify-center">
        <input
          ref={ref}
          id={id}
          type="checkbox"
          className={cn(
            'peer size-4 shrink-0 cursor-pointer appearance-none rounded-sm border border-border-strong bg-surface',
            'transition-colors duration-[120ms] motion-reduce:transition-none enabled:hover:border-fg-muted',
            'checked:border-primary checked:bg-primary enabled:checked:hover:border-primary-hover enabled:checked:hover:bg-primary-hover',
            'disabled:cursor-not-allowed disabled:border-border disabled:bg-surface-sunken',
          )}
          {...props}
        />
        <Check
          className="pointer-events-none absolute size-3 text-primary-fg opacity-0 peer-checked:opacity-100"
          strokeWidth={3}
          aria-hidden
        />
      </span>
      {label}
    </label>
  )
})
