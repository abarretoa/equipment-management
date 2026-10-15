import type { ButtonHTMLAttributes, ReactNode, Ref } from 'react'
import { Loader2 } from 'lucide-react'
import { cn } from '../utils/cn'

/**
 * `danger` is the filled destructive button — inside a confirmation dialog.
 * `danger-outline` is the restrained destructive action for page headers and
 * toolbars (e.g. "Desativar"), per Visual System v1.
 */
export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'danger-outline'
/** sm 32 (headers/toolbars) · md 36 (forms/dialogs) · lg 40 desktop / 44 touch (login) · touch 44. */
export type ButtonSize = 'sm' | 'md' | 'lg' | 'touch'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
  loading?: boolean
  icon?: ReactNode
  ref?: Ref<HTMLButtonElement>
}

const variantClasses: Record<ButtonVariant, string> = {
  primary:
    'border-primary bg-primary font-semibold text-primary-fg hover:border-primary-hover hover:bg-primary-hover disabled:border-border disabled:bg-border disabled:text-fg-subtle',
  secondary:
    'border-border-control bg-surface font-medium text-fg hover:border-border-strong hover:bg-surface-sunken disabled:border-border disabled:bg-surface disabled:text-fg-disabled',
  ghost:
    'border-transparent bg-transparent font-medium text-fg hover:bg-surface-selected disabled:bg-transparent disabled:text-fg-disabled',
  danger:
    'border-danger bg-danger font-semibold text-primary-fg hover:border-danger-hover hover:bg-danger-hover disabled:border-danger-border disabled:bg-danger-border disabled:text-danger-fg',
  'danger-outline':
    'border-border-control bg-surface font-medium text-danger hover:border-danger-border hover:bg-danger-bg disabled:border-border disabled:bg-surface disabled:text-fg-disabled',
}

/** While loading the button is disabled but must still read as the same action, not as "disabled". */
const loadingClasses: Record<ButtonVariant, string> = {
  primary: 'border-fg-muted bg-fg-muted font-semibold text-primary-fg',
  secondary: 'border-border-control bg-surface font-medium text-fg-muted',
  ghost: 'border-transparent bg-transparent font-medium text-fg-muted',
  danger: 'border-danger-hover bg-danger-hover font-semibold text-primary-fg',
  'danger-outline': 'border-border-control bg-surface font-medium text-danger',
}

const sizeClasses: Record<ButtonSize, string> = {
  sm: 'h-8 px-3 text-[13px] gap-1.5',
  md: 'h-9 px-3.5 text-[13px] gap-2',
  /** Login: 40px on desktop, 44px touch target below lg. */
  lg: 'h-11 px-4 text-[15px] gap-2 lg:h-10 lg:text-sm',
  touch: 'h-11 px-4 text-[15px] gap-2',
}

export function Button({
  variant = 'secondary',
  size = 'md',
  loading = false,
  icon,
  disabled,
  className,
  children,
  ...props
}: ButtonProps) {
  return (
    <button
      className={cn(
        'inline-flex items-center justify-center rounded-md border whitespace-nowrap',
        'transition-colors duration-[120ms] motion-reduce:transition-none disabled:cursor-not-allowed',
        loading ? loadingClasses[variant] : variantClasses[variant],
        sizeClasses[size],
        className,
      )}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...props}
    >
      {loading ? (
        <Loader2 className="size-4 animate-spin motion-reduce:animate-none" aria-hidden />
      ) : (
        icon
      )}
      {children}
    </button>
  )
}
