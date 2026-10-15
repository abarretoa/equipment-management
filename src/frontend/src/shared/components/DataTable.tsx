import type { MouseEvent, ReactNode } from 'react'
import { Link, useNavigate } from 'react-router'
import { ChevronLeft, ChevronRight, ChevronsUpDown, ChevronUp, ChevronDown } from 'lucide-react'
import { cn } from '../utils/cn'
import { TableSkeleton } from './Skeleton'
import { ErrorState } from './ErrorState'
import { EmptyState } from './EmptyState'
import { IconButton } from './IconButton'

export interface DataTableColumn<T> {
  key: string
  header: string
  render: (row: T) => ReactNode
  align?: 'left' | 'right' | 'center'
  width?: string
  sortable?: boolean
  /**
   * `secondary` columns are dropped below 1280px (Visual System v1 table
   * priority: e.g. Nº de série, Registado em), never the identifier/status.
   */
  priority?: 'primary' | 'secondary'
}

export interface SortState {
  key: string
  direction: 'asc' | 'desc'
}

export interface PaginationState {
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

interface DataTableProps<T> {
  columns: DataTableColumn<T>[]
  rows: T[]
  rowKey: (row: T) => string
  isLoading?: boolean
  error?: unknown
  onRetry?: () => void
  emptyTitle: string
  emptyDescription?: string
  /** A real action for the empty state (e.g. a "Limpar filtros" button). */
  emptyAction?: ReactNode
  /** The empty state's icon (shown in its icon tile). */
  emptyIcon?: ReactNode
  onRowClick?: (row: T) => void
  /**
   * The row's primary destination. When set, the primary column's cell is
   * rendered as a real, keyboard-focusable link to it, and a click anywhere
   * else on the row (outside other links/buttons) follows it as well.
   */
  getRowHref?: (row: T) => string | undefined
  /** Column whose cell becomes the row link. Defaults to the first column. */
  rowLinkColumn?: string
  sort?: SortState
  onSortChange?: (key: string) => void
  pagination?: PaginationState
  /**
   * Stacked scan-row presentation below 768px (Visual System v1 "Mobile table
   * rule"). When set, the table is shown from 768px up and each row renders as
   * one divider row — a single link to `getRowHref` when there is one, so the
   * content must not contain other interactive elements.
   */
  renderMobileRow?: (row: T) => ReactNode
  /**
   * For wide comparison tables (audit, import validation): below this width
   * the table scrolls horizontally inside its own region instead of
   * squeezing columns, and the pagination stays in place. Named by
   * `scrollLabel` so the keyboard-focusable scroll region is announced.
   */
  minTableWidth?: string
  scrollLabel?: string
}

const alignClass = { left: 'text-left', right: 'text-right', center: 'text-center' } as const

/** Clicks on these inside a row belong to that control, never to the row. */
const INTERACTIVE_SELECTOR =
  'a, button, input, select, textarea, label, [role="button"], [role="menuitem"]'

/**
 * The single table implementation for the whole product (Phase 9 scope).
 * Server-paginated (the backend already returns small pages), so sorting here
 * is an opt-in controlled prop the caller wires to its own query params rather
 * than a client-side sort engine — no headless table library needed for that.
 */
export function DataTable<T>({
  columns,
  rows,
  rowKey,
  isLoading,
  error,
  onRetry,
  emptyTitle,
  emptyDescription,
  emptyAction,
  emptyIcon,
  onRowClick,
  getRowHref,
  rowLinkColumn,
  sort,
  onSortChange,
  pagination,
  renderMobileRow,
  minTableWidth,
  scrollLabel,
}: DataTableProps<T>) {
  const navigate = useNavigate()

  if (isLoading) {
    return <TableSkeleton columns={columns.length} />
  }

  if (error) {
    return <ErrorState error={error} onRetry={onRetry} />
  }

  if (rows.length === 0) {
    return (
      <EmptyState
        icon={emptyIcon}
        title={emptyTitle}
        description={emptyDescription}
        action={emptyAction}
      />
    )
  }

  const linkColumnKey = rowLinkColumn ?? columns[0]?.key

  function rowActivation(row: T): ((event: MouseEvent<HTMLTableRowElement>) => void) | undefined {
    const href = getRowHref?.(row)
    if (!onRowClick && !href) return undefined
    return (event) => {
      if (event.target instanceof Element && event.target.closest(INTERACTIVE_SELECTOR)) return
      if (onRowClick) onRowClick(row)
      else if (href) void navigate(href)
    }
  }

  return (
    <div className="flex flex-col">
      {renderMobileRow && (
        <ul className="md:hidden">
          {rows.map((row) => {
            const href = getRowHref?.(row)
            const content = renderMobileRow(row)
            return (
              <li key={rowKey(row)} className="border-b border-border-row last:border-b-0">
                {href ? (
                  <Link
                    to={href}
                    className="block min-h-11 px-4 py-2.5 transition-colors duration-[120ms] hover:bg-surface-sunken focus-visible:outline-offset-[-2px] motion-reduce:transition-none"
                  >
                    {content}
                  </Link>
                ) : (
                  <div className="min-h-11 px-4 py-2.5">{content}</div>
                )}
              </li>
            )
          })}
        </ul>
      )}
      <div
        // `relative` when scrolling: absolutely positioned content (e.g. sr-only
        // text) must be clipped by this region, not widen the whole page.
        className={cn('overflow-x-auto', minTableWidth && 'relative', renderMobileRow && 'hidden md:block')}
        {...(minTableWidth ? { role: 'region', 'aria-label': scrollLabel, tabIndex: 0 } : {})}
      >
        <table className="w-full border-collapse text-[13px]" style={minTableWidth ? { minWidth: minTableWidth } : undefined}>
          <thead>
            <tr className="border-b border-border-divider">
              {columns.map((column) => (
                <th
                  key={column.key}
                  scope="col"
                  style={column.width ? { width: column.width } : undefined}
                  className={cn(
                    'h-9 px-3 text-[12px] font-medium whitespace-nowrap text-fg-muted',
                    alignClass[column.align ?? 'left'],
                    column.priority === 'secondary' && 'hidden xl:table-cell',
                  )}
                >
                  {column.sortable && onSortChange ? (
                    <button
                      type="button"
                      onClick={() => onSortChange(column.key)}
                      className="inline-flex items-center gap-1 hover:text-fg"
                    >
                      {column.header}
                      {sort?.key === column.key ? (
                        sort.direction === 'asc' ? (
                          <ChevronUp className="size-3.5" aria-hidden />
                        ) : (
                          <ChevronDown className="size-3.5" aria-hidden />
                        )
                      ) : (
                        <ChevronsUpDown className="size-3.5 opacity-40" aria-hidden />
                      )}
                    </button>
                  ) : (
                    column.header
                  )}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => {
              const onActivate = rowActivation(row)
              const href = getRowHref?.(row)
              return (
                <tr
                  key={rowKey(row)}
                  onClick={onActivate}
                  className={cn(
                    'border-b border-border-row last:border-b-0',
                    onActivate &&
                      'cursor-pointer transition-colors duration-[120ms] hover:bg-surface-sunken motion-reduce:transition-none',
                  )}
                >
                  {columns.map((column) => (
                    <td
                      key={column.key}
                      className={cn(
                        'h-9 px-3 py-2 text-fg',
                        alignClass[column.align ?? 'left'],
                        column.priority === 'secondary' && 'hidden xl:table-cell',
                      )}
                    >
                      {href && column.key === linkColumnKey ? (
                        <Link
                          to={href}
                          className="text-inherit hover:underline focus-visible:outline-offset-1"
                        >
                          {column.render(row)}
                        </Link>
                      ) : (
                        column.render(row)
                      )}
                    </td>
                  ))}
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      {pagination && <TablePagination {...pagination} />}
    </div>
  )
}

function TablePagination({ page, pageSize, totalCount, onPageChange }: PaginationState) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, totalCount)

  return (
    <div className="flex min-h-12 items-center justify-end gap-3 border-t border-border-row px-3 py-2">
      <p className="text-[12px] text-fg-muted tabular-nums">
        {totalCount === 0 ? 'Sem resultados' : `${from}–${to} de ${totalCount}`}
      </p>
      <div className="flex items-center gap-1.5">
        <IconButton
          icon={<ChevronLeft className="size-3.5" />}
          label="Página anterior"
          variant="outline"
          size="sm"
          disabled={page <= 1}
          onClick={() => onPageChange(page - 1)}
        />
        <span className="min-w-12 text-center text-[12px] text-fg-muted tabular-nums">
          {page} / {totalPages}
        </span>
        <IconButton
          icon={<ChevronRight className="size-3.5" />}
          label="Página seguinte"
          variant="outline"
          size="sm"
          disabled={page >= totalPages}
          onClick={() => onPageChange(page + 1)}
        />
      </div>
    </div>
  )
}
