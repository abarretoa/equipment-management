type ClassValue = string | number | null | undefined | false

/** Minimal className joiner — avoids pulling in `clsx` for one function. */
export function cn(...values: ClassValue[]): string {
  return values.filter(Boolean).join(' ')
}
