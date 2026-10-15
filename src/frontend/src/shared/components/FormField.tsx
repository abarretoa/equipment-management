import type { ReactNode } from 'react'
import { CircleAlert } from 'lucide-react'
import { cn } from '../utils/cn'

interface FormFieldProps {
  id: string
  label: string
  error?: string
  helperText?: string
  /** Renders the required asterisk after the label. */
  required?: boolean
  /** Renders " (opcional)" after the label — so the label text itself must not say it. */
  optional?: boolean
  children: ReactNode
  className?: string
}

/**
 * Top-aligned label + control + helper/error slot (Carbon/Fluent/Polaris all
 * default to top-aligned labels — Phase 9 scope).
 * Pass `id={id}` and, when `error` is set, `aria-describedby={`${id}-error`}`
 * `aria-invalid` to the control inside so screen readers announce the message.
 * The error is icon + text (+ the control's danger border), never colour alone.
 */
export function FormField({
  id,
  label,
  error,
  helperText,
  required,
  optional,
  children,
  className,
}: FormFieldProps) {
  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <label htmlFor={id} className="text-[13px] leading-[18px] font-medium text-fg">
        {label}
        {required && (
          <span className="text-danger" aria-hidden>
            {' '}
            *
          </span>
        )}
        {optional && <span className="font-normal text-fg-muted"> (opcional)</span>}
      </label>
      {children}
      {error ? (
        <p
          id={`${id}-error`}
          className="flex items-start gap-1.5 text-[12px] leading-4 text-danger"
          role="alert"
        >
          <CircleAlert className="mt-px size-3 shrink-0" aria-hidden />
          <span>{error}</span>
        </p>
      ) : helperText ? (
        <p id={`${id}-helper`} className="text-[12px] leading-4 text-fg-muted">
          {helperText}
        </p>
      ) : null}
    </div>
  )
}
