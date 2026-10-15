import type { ReactNode } from 'react'

interface AdminMobileRowProps {
  name: string
  status: ReactNode
  meta: ReactNode
  /** The row's action menu (e.g. a `DropdownMenu` with a 44px `IconButton` trigger). */
  menu?: ReactNode
}

/**
 * Content for a `DataTable` `renderMobileRow` on admin lists without a detail
 * page (Empresas, Tipos de equipamento): name + state, one line of metadata
 * and the row's action menu. Not a link — the menu is the row's only control,
 * so do not pair it with `getRowHref`.
 */
export function AdminMobileRow({ name, status, meta, menu }: AdminMobileRowProps) {
  return (
    <div className="-my-1.5 -mr-2 flex items-center gap-2">
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className="flex items-center justify-between gap-3">
          <span className="min-w-0 truncate text-[14px] leading-5 font-medium text-fg">{name}</span>
          {status}
        </span>
        <span className="text-[13px] leading-[18px] text-fg-muted">{meta}</span>
      </div>
      {menu}
    </div>
  )
}
