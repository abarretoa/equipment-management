import { forwardRef, type TextareaHTMLAttributes } from 'react'
import { cn } from '../utils/cn'
import { fieldClasses } from './fieldStyles'

interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  invalid?: boolean
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(function Textarea(
  { invalid, className, rows = 3, ...props },
  ref,
) {
  return (
    <textarea
      ref={ref}
      rows={rows}
      aria-invalid={invalid || undefined}
      className={cn(
        fieldClasses(invalid),
        'min-h-[60px] resize-y px-2.5 py-2 text-[13px] leading-5',
        className,
      )}
      {...props}
    />
  )
})
