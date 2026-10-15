import { X } from 'lucide-react'
import { cn } from '../utils/cn'

interface TagProps {
  label: string
  onRemove?: () => void
  className?: string
  /**
   * Filter-chip presentation: renders "{prefix}: **{label}**" in the active-filter
   * style (ink border, selected fill), e.g. prefix "Tipo", label "Laptop".
   */
  prefix?: string
}

/** Neutral, removable label for filters/categorization — distinct from StatusBadge's fixed semantic tones. */
export function Tag({ label, onRemove, className, prefix }: TagProps) {
  const isFilter = prefix !== undefined
  const removeLabel = isFilter ? `Remover filtro ${prefix}: ${label}` : `Remover ${label}`

  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center whitespace-nowrap',
        isFilter
          ? 'h-7 gap-1.5 rounded-md border border-primary bg-surface-selected pl-2.5 text-[13px] text-fg'
          : 'h-[22px] gap-1 rounded-sm border border-border bg-surface-selected pl-[7px] text-[12px] font-medium text-fg-muted',
        onRemove ? 'pr-1' : isFilter ? 'pr-2.5' : 'pr-[7px]',
        className,
      )}
    >
      {isFilter ? (
        <span>
          {prefix}: <b className="font-semibold">{label}</b>
        </span>
      ) : (
        label
      )}
      {onRemove && (
        <button
          type="button"
          onClick={onRemove}
          aria-label={removeLabel}
          title={removeLabel}
          className={cn(
            'flex shrink-0 items-center justify-center rounded-sm text-fg-muted hover:bg-border hover:text-fg',
            isFilter ? 'size-[22px]' : 'size-4',
          )}
        >
          <X className={isFilter ? 'size-3.5' : 'size-3'} aria-hidden />
        </button>
      )}
    </span>
  )
}
