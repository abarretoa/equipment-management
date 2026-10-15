import { forwardRef, type InputHTMLAttributes } from 'react'
import { cn } from '../utils/cn'
import { controlHeightClasses, fieldClasses, type ControlSize } from './fieldStyles'

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  invalid?: boolean
  /** Visual height only (the native `size` attribute is left untouched). Defaults to 36px. */
  controlSize?: ControlSize
}

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { invalid, controlSize = 'md', className, ...props },
  ref,
) {
  return (
    <input
      ref={ref}
      aria-invalid={invalid || undefined}
      className={cn(fieldClasses(invalid), controlHeightClasses[controlSize], 'px-2.5', className)}
      {...props}
    />
  )
})
