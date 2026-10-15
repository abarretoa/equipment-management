import type { HTMLAttributes } from 'react'
import { cn } from '../utils/cn'

const WIDTH_CLASS = {
  standard: 'max-w-[1000px]',
  compact: 'max-w-[760px]',
} as const

interface ContentWidthProps extends HTMLAttributes<HTMLDivElement> {
  /** 'standard' for entity forms (≈900-1100px), 'compact' for focused/simple forms (≈600-800px). */
  variant?: keyof typeof WIDTH_CLASS
}

/**
 * Caps a form/task page's width so it reads as intentionally designed instead
 * of stretching input fields across the full data-table content area (Phase 9
 * refinement). Left-aligned, not centered — wide tables/dashboards stay
 * full-width by simply not using this component.
 */
export function ContentWidth({ variant = 'standard', className, ...props }: ContentWidthProps) {
  return <div className={cn(WIDTH_CLASS[variant], className)} {...props} />
}
