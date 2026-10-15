import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { cn } from '../utils/cn'

interface IconButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  icon: ReactNode
  label: string
  /** `outline` adds the control border (e.g. pagination). */
  variant?: 'ghost' | 'danger' | 'outline'
  /** sm 28 · md 32 · touch 44. */
  size?: 'sm' | 'md' | 'touch'
}

const sizeClasses = { sm: 'size-7', md: 'size-8', touch: 'size-11' } as const

const variantClasses = {
  ghost:
    'border-transparent text-fg-muted hover:bg-surface-selected hover:text-fg disabled:bg-transparent',
  danger: 'border-transparent text-danger hover:bg-danger-bg disabled:bg-transparent',
  outline:
    'border-border-control bg-surface text-fg hover:border-border-strong hover:bg-surface-sunken disabled:border-border disabled:bg-surface',
} as const

/**
 * Icon-only control — `label` is mandatory and becomes the accessible name (never a bare icon).
 * Defaults to type="button" (overridable): an icon action inside a <form> must never submit it.
 */
export function IconButton({
  icon,
  label,
  variant = 'ghost',
  size = 'md',
  className,
  ...props
}: IconButtonProps) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      className={cn(
        'inline-flex shrink-0 items-center justify-center rounded-md border',
        'transition-colors duration-[120ms] motion-reduce:transition-none',
        'disabled:cursor-not-allowed disabled:text-fg-disabled',
        sizeClasses[size],
        variantClasses[variant],
        className,
      )}
      {...props}
    >
      {icon}
    </button>
  )
}
