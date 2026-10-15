import { cn } from '../utils/cn'

/** sm 32 (toolbars) · md 36 (forms, dialogs — the default) · touch 44 with 16px text. */
export type ControlSize = 'sm' | 'md' | 'touch'

export const controlHeightClasses: Record<ControlSize, string> = {
  sm: 'h-8 text-[13px]',
  md: 'h-9 text-[13px]',
  touch: 'h-11 text-base',
}

/**
 * Shared surface of every text-like control (Input, Select, Textarea, the
 * Combobox trigger): strong 3.2:1 border, ink focus border + 3px ring, and the
 * semantic danger border + 1px ring when invalid. Height is applied separately.
 */
export function fieldClasses(invalid: boolean | undefined) {
  return cn(
    'w-full rounded-md border bg-surface text-fg placeholder:text-fg-subtle',
    'transition-[border-color,box-shadow] duration-[120ms] motion-reduce:transition-none',
    'focus-visible:border-primary focus-visible:ring-3 focus-visible:ring-primary/16 focus-visible:outline-none',
    'disabled:cursor-not-allowed disabled:border-border disabled:bg-surface-sunken disabled:text-fg-disabled',
    invalid
      ? 'border-danger ring-1 ring-danger'
      : 'border-border-strong enabled:hover:border-fg-muted',
  )
}
