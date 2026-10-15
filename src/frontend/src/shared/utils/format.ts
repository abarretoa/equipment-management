import { format, parseISO } from 'date-fns'
import { pt } from 'date-fns/locale'

/** `2026-09-10` (DateOnly) or full ISO datetime -> `10/09/2026` (docs/PROJECT_SPEC.md: dd/MM/yyyy, pt-PT). */
export function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  try {
    return format(parseISO(value), 'dd/MM/yyyy', { locale: pt })
  } catch {
    return value
  }
}

/** Full ISO UTC datetime -> `10/09/2026 14:32`. */
export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  try {
    return format(parseISO(value), 'dd/MM/yyyy HH:mm', { locale: pt })
  } catch {
    return value
  }
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

/** `<input type="date">` expects yyyy-MM-dd; DateOnly values already arrive in that shape. */
export function toDateInputValue(value: string | null | undefined): string {
  return value ?? ''
}
