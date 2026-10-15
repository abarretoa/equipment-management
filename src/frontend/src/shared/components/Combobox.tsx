import { useEffect, useRef, useState, type ReactNode } from 'react'
import * as Popover from '@radix-ui/react-popover'
import { Check, ChevronsUpDown, Loader2, Search, X } from 'lucide-react'
import { cn } from '../utils/cn'
import { controlHeightClasses, fieldClasses } from './fieldStyles'

export interface ComboboxOption {
  value: string
  label: string
  secondary?: ReactNode
}

interface ComboboxProps {
  id?: string
  value: string | null
  onChange: (value: string) => void
  options: ComboboxOption[]
  search: string
  onSearchChange: (value: string) => void
  placeholder?: string
  searchPlaceholder?: string
  isLoading?: boolean
  emptyLabel?: string
  invalid?: boolean
  disabled?: boolean
  'aria-label'?: string
}

/**
 * Searchable single-select combobox (Phase 9 refinement) — a Popover
 * anchored below a Select/Input-styled trigger, with a search field and a
 * scrollable, keyboard-navigable option list. Built on the same Radix family
 * already used for DropdownMenu/Tabs/Tooltip rather than a new table/combobox
 * library, since the backend already returns small server-filtered pages.
 */
export function Combobox({
  id,
  value,
  onChange,
  options,
  search,
  onSearchChange,
  placeholder = 'Selecione…',
  searchPlaceholder = 'Pesquisar…',
  isLoading,
  emptyLabel = 'Sem resultados.',
  invalid,
  disabled,
  'aria-label': ariaLabel,
}: ComboboxProps) {
  const [open, setOpen] = useState(false)
  const [activeIndex, setActiveIndex] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLUListElement>(null)

  const selected = options.find((o) => o.value === value) ?? null
  // Clamped at render time (not reset via effect) so the highlighted row always stays in
  // bounds after the option list shrinks, without a setState-in-effect cascade.
  const safeActiveIndex = Math.min(activeIndex, Math.max(options.length - 1, 0))

  useEffect(() => {
    if (!open) return
    const el = listRef.current?.querySelector<HTMLElement>(`[data-index="${safeActiveIndex}"]`)
    el?.scrollIntoView({ block: 'nearest' })
  }, [safeActiveIndex, open])

  function handleOpenChange(next: boolean) {
    setOpen(next)
    if (next) setActiveIndex(0)
  }

  function handleSearchChange(next: string) {
    onSearchChange(next)
    setActiveIndex(0)
  }

  function select(option: ComboboxOption) {
    onChange(option.value)
    setOpen(false)
  }

  function onKeyDown(event: React.KeyboardEvent) {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      setActiveIndex((i) => Math.min(i + 1, options.length - 1))
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      setActiveIndex((i) => Math.max(i - 1, 0))
    } else if (event.key === 'Enter') {
      event.preventDefault()
      const option = options[safeActiveIndex]
      if (option) select(option)
    } else if (event.key === 'Escape') {
      setOpen(false)
    }
  }

  return (
    <Popover.Root open={open} onOpenChange={handleOpenChange}>
      {/*
        The clear action is a sibling of the trigger, not nested inside it: an
        interactive control inside a <button> is invalid and unreachable for
        assistive technology. It is positioned over the trigger's right side.
      */}
      <div className="relative">
        <Popover.Trigger asChild>
          <button
            type="button"
            id={id}
            disabled={disabled}
            aria-label={ariaLabel}
            aria-invalid={invalid || undefined}
            className={cn(
              fieldClasses(invalid),
              controlHeightClasses.md,
              'flex items-center justify-between gap-2 pl-2.5 text-left',
              selected && !disabled ? 'pr-14' : 'pr-2.5',
            )}
          >
            <span className={cn('truncate', !selected && 'text-fg-subtle')}>
              {selected ? selected.label : placeholder}
            </span>
            <ChevronsUpDown className="size-3.5 shrink-0 text-fg-muted" aria-hidden />
          </button>
        </Popover.Trigger>
        {selected && !disabled && (
          <button
            type="button"
            aria-label="Limpar seleção"
            onClick={() => onChange('')}
            className="absolute top-1/2 right-7 flex size-6 -translate-y-1/2 items-center justify-center rounded-sm text-fg-muted hover:bg-surface-selected hover:text-fg"
          >
            <X className="size-3.5" aria-hidden />
          </button>
        )}
      </div>
      <Popover.Portal>
        <Popover.Content
          align="start"
          sideOffset={4}
          // Focus the search field once the content is actually mounted. Doing it from a
          // requestAnimationFrame in the open handler ran before the portal content
          // existed when the combobox sits inside a Dialog, leaving focus on the trigger.
          onOpenAutoFocus={(event) => {
            event.preventDefault()
            inputRef.current?.focus()
          }}
          className="z-50 flex w-[var(--radix-popover-trigger-width)] flex-col overflow-hidden rounded-lg border border-border-divider bg-surface-raised shadow-md"
        >
          <div className="relative border-b border-border p-1.5">
            <Search className="pointer-events-none absolute top-1/2 left-4 size-3.5 -translate-y-1/2 text-fg-subtle" aria-hidden />
            <input
              ref={inputRef}
              value={search}
              onChange={(event) => handleSearchChange(event.target.value)}
              onKeyDown={onKeyDown}
              placeholder={searchPlaceholder}
              className="h-8 w-full rounded-sm border-0 bg-transparent pl-6 text-sm text-fg outline-none placeholder:text-fg-subtle"
            />
          </div>
          <ul ref={listRef} role="listbox" className="max-h-64 overflow-y-auto p-1">
            {isLoading ? (
              <li className="flex items-center justify-center gap-2 px-3 py-4 text-[13px] text-fg-subtle">
                <Loader2 className="size-3.5 animate-spin" aria-hidden />A carregar…
              </li>
            ) : options.length === 0 ? (
              <li className="px-3 py-4 text-center text-[13px] text-fg-subtle">{emptyLabel}</li>
            ) : (
              options.map((option, index) => (
                <li key={option.value}>
                  <button
                    type="button"
                    data-index={index}
                    role="option"
                    aria-selected={option.value === value}
                    onMouseEnter={() => setActiveIndex(index)}
                    onClick={() => select(option)}
                    className={cn(
                      'flex w-full items-center justify-between gap-2 rounded-md px-2.5 py-2 text-left text-[13px]',
                      index === safeActiveIndex ? 'bg-surface-selected' : undefined,
                    )}
                  >
                    <span className="flex min-w-0 flex-col">
                      <span className="truncate font-medium text-fg">{option.label}</span>
                      {option.secondary && <span className="truncate text-[12px] text-fg-muted">{option.secondary}</span>}
                    </span>
                    {option.value === value && <Check className="size-4 shrink-0 text-primary" aria-hidden />}
                  </button>
                </li>
              ))
            )}
          </ul>
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  )
}
