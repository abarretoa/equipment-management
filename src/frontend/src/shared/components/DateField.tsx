import { forwardRef, type InputHTMLAttributes } from 'react'
import { Input } from './Input'

type DateFieldProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> & { invalid?: boolean }

/**
 * Native date input (yyyy-MM-dd value, browser-native picker/keyboard entry —
 * more robust than a custom calendar widget for a data-entry-heavy internal tool).
 * Display elsewhere in the app uses dd/MM/yyyy via shared/utils/format.ts.
 */
export const DateField = forwardRef<HTMLInputElement, DateFieldProps>(function DateField(
  props,
  ref,
) {
  return <Input ref={ref} type="date" {...props} />
})
