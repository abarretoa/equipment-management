import type { ReactNode } from 'react'
import { Search, X } from 'lucide-react'
import { Input } from './Input'
import { Button } from './Button'

interface FilterBarProps {
  search?: string
  onSearchChange?: (value: string) => void
  searchPlaceholder?: string
  children?: ReactNode
  onClear?: () => void
  hasActiveFilters?: boolean
  /** Active-filter chips (e.g. `<Tag prefix="Tipo" … onRemove />`), shown after the controls. */
  chips?: ReactNode
  /** Right-aligned summary such as a result count. */
  summary?: ReactNode
}

/** Search + filter-control row above a DataTable — the one filtering pattern used across every list screen. */
export function FilterBar({
  search,
  onSearchChange,
  searchPlaceholder = 'Pesquisar…',
  children,
  onClear,
  hasActiveFilters,
  chips,
  summary,
}: FilterBarProps) {
  return (
    <div className="flex min-h-13 flex-wrap items-center gap-2 border-b border-border bg-surface px-4 py-2.5">
      {onSearchChange && (
        <div className="relative min-w-[220px] flex-1 sm:max-w-xs">
          <Search
            className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-muted"
            aria-hidden
          />
          <Input
            value={search ?? ''}
            onChange={(event) => onSearchChange(event.target.value)}
            placeholder={searchPlaceholder}
            aria-label="Pesquisar"
            className="pl-8"
          />
        </div>
      )}
      {children}
      {chips}
      {hasActiveFilters && onClear && (
        <Button
          variant="ghost"
          size="sm"
          icon={<X className="size-3.5" aria-hidden />}
          onClick={onClear}
        >
          Limpar filtros
        </Button>
      )}
      {summary && <div className="ml-auto text-[12px] text-fg-muted">{summary}</div>}
    </div>
  )
}
