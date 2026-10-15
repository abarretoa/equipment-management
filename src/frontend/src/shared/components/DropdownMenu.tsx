import type { ReactNode } from 'react'
import * as RadixMenu from '@radix-ui/react-dropdown-menu'
import { cn } from '../utils/cn'

interface MenuItemConfig {
  label: string
  icon?: ReactNode
  onSelect: () => void
  destructive?: boolean
  disabled?: boolean
}

interface DropdownMenuProps {
  trigger: ReactNode
  items: MenuItemConfig[]
  align?: 'start' | 'end'
}

/** Kebab/overflow menu for row actions and the account menu — single implementation, consistent everywhere. */
export function DropdownMenu({ trigger, items, align = 'end' }: DropdownMenuProps) {
  return (
    <RadixMenu.Root>
      <RadixMenu.Trigger asChild>{trigger}</RadixMenu.Trigger>
      <RadixMenu.Portal>
        <RadixMenu.Content
          align={align}
          sideOffset={4}
          className="z-50 min-w-[180px] rounded-lg border border-border-divider bg-surface-raised p-1 shadow-md"
        >
          {items.map((item, index) => (
            <RadixMenu.Item
              key={index}
              disabled={item.disabled}
              onSelect={item.onSelect}
              className={cn(
                'flex h-8 cursor-pointer items-center gap-2 rounded-md px-2.5 text-[13px] outline-none select-none',
                'data-[disabled]:cursor-not-allowed data-[disabled]:opacity-50',
                item.destructive
                  ? 'text-danger data-[highlighted]:bg-danger-bg'
                  : 'text-fg data-[highlighted]:bg-surface-sunken',
              )}
            >
              {item.icon}
              {item.label}
            </RadixMenu.Item>
          ))}
        </RadixMenu.Content>
      </RadixMenu.Portal>
    </RadixMenu.Root>
  )
}
