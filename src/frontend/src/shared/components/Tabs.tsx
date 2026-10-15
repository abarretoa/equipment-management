import type { ReactNode } from 'react'
import * as RadixTabs from '@radix-ui/react-tabs'
import { cn } from '../utils/cn'

interface TabItem {
  value: string
  label: string
  badge?: ReactNode
}

interface TabsProps {
  items: TabItem[]
  value: string
  onValueChange: (value: string) => void
  children?: ReactNode
  /** Accessible name of the tab list. Defaults to the historical "Secções". */
  'aria-label'?: string
  /**
   * Radix default (`automatic`) activates a tab as soon as it receives focus.
   * Use `manual` for tabs that act as filters, so arrow keys only move focus
   * and Enter/Space applies the filter.
   */
  activationMode?: 'automatic' | 'manual'
}

export function Tabs({
  items,
  value,
  onValueChange,
  children,
  'aria-label': ariaLabel = 'Secções',
  activationMode,
}: TabsProps) {
  return (
    <RadixTabs.Root value={value} onValueChange={onValueChange} activationMode={activationMode}>
      <RadixTabs.List
        className="flex gap-5 overflow-x-auto border-b border-border"
        aria-label={ariaLabel}
      >
        {items.map((item) => (
          <RadixTabs.Trigger
            key={item.value}
            value={item.value}
            className={cn(
              'flex h-9 shrink-0 items-center gap-1.5 text-[13px] whitespace-nowrap text-fg-muted',
              'transition-colors duration-[120ms] hover:text-fg focus-visible:-outline-offset-2 motion-reduce:transition-none',
              'data-[state=active]:font-semibold data-[state=active]:text-fg data-[state=active]:shadow-[inset_0_-2px_0_var(--color-primary)]',
            )}
          >
            {item.label}
            {item.badge}
          </RadixTabs.Trigger>
        ))}
      </RadixTabs.List>
      {children}
    </RadixTabs.Root>
  )
}

export function TabPanel({ value, children }: { value: string; children: ReactNode }) {
  return (
    <RadixTabs.Content value={value} className="pt-4">
      {children}
    </RadixTabs.Content>
  )
}
