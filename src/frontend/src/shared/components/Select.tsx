import { forwardRef, type SelectHTMLAttributes } from 'react'
import { ChevronDown } from 'lucide-react'
import { cn } from '../utils/cn'
import { controlHeightClasses, fieldClasses, type ControlSize } from './fieldStyles'

interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  invalid?: boolean
  /** Visual height only (the native `size` attribute is left untouched). Defaults to 36px. */
  controlSize?: ControlSize
}

/** Native <select> styled to match Input — keeps full OS-level keyboard/a11y behavior for free. */
export const Select = forwardRef<HTMLSelectElement, SelectProps>(function Select(
  { invalid, controlSize = 'md', className, children, ...props },
  ref,
) {
  return (
    <div className="relative">
      <select
        ref={ref}
        aria-invalid={invalid || undefined}
        className={cn(
          fieldClasses(invalid),
          controlHeightClasses[controlSize],
          'appearance-none pr-8 pl-2.5',
          className,
        )}
        {...props}
      >
        {children}
      </select>
      <ChevronDown
        className="pointer-events-none absolute top-1/2 right-2.5 size-3.5 -translate-y-1/2 text-fg-muted"
        aria-hidden
      />
    </div>
  )
})
