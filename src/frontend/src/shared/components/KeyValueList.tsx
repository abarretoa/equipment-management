import type { ReactNode } from 'react'

interface KeyValueItem {
  label: string
  value: ReactNode
}

const COLUMN_CLASSES = {
  1: 'grid-cols-1',
  2: 'grid-cols-1 sm:grid-cols-2',
  3: 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-3',
} as const

/** Label/value grid for read-only detail data — the one pattern for "here's what this record contains." */
export function KeyValueList({
  items,
  columns = 2,
}: {
  items: KeyValueItem[]
  columns?: 1 | 2 | 3
}) {
  return (
    <dl className={`grid gap-x-6 gap-y-3.5 ${COLUMN_CLASSES[columns]}`}>
      {items.map((item, index) => (
        <div key={index} className="flex min-w-0 flex-col gap-0.5">
          <dt className="text-[12px] leading-4 text-fg-muted">{item.label}</dt>
          <dd className="text-[13px] leading-5 break-words text-fg">{item.value}</dd>
        </div>
      ))}
    </dl>
  )
}
