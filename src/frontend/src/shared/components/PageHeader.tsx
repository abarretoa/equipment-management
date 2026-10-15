import type { ReactNode } from 'react'
import { Link } from 'react-router'

interface Breadcrumb {
  label: string
  to?: string
}

interface PageHeaderProps {
  title: string
  description?: string
  breadcrumbs?: Breadcrumb[]
  actions?: ReactNode
  tabs?: ReactNode
}

/** The one page-title pattern used across every screen — title/description/actions never hand-rolled per page. */
export function PageHeader({ title, description, breadcrumbs, actions, tabs }: PageHeaderProps) {
  return (
    <div className="flex flex-col gap-2 border-b border-border bg-surface px-6 py-5">
      {breadcrumbs && breadcrumbs.length > 0 && (
        <nav aria-label="Localização">
          <ol className="flex flex-wrap items-center gap-1.5 text-[12px] text-fg-muted">
            {breadcrumbs.map((crumb, index) => {
              const isLast = index === breadcrumbs.length - 1
              return (
                <li key={index} className="flex min-w-0 items-center gap-1.5">
                  {index > 0 && (
                    <span className="text-border-control" aria-hidden>
                      /
                    </span>
                  )}
                  {crumb.to ? (
                    <Link to={crumb.to} className="hover:text-fg hover:underline">
                      {crumb.label}
                    </Link>
                  ) : (
                    <span className="truncate text-fg" aria-current={isLast ? 'page' : undefined}>
                      {crumb.label}
                    </span>
                  )}
                </li>
              )
            })}
          </ol>
        </nav>
      )}
      <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
        <div className="min-w-0">
          <h1 className="text-xl leading-7 font-semibold tracking-[-0.01em] break-words text-fg">
            {title}
          </h1>
          {description && <p className="text-[13px] leading-5 text-fg-muted">{description}</p>}
        </div>
        {actions && <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div>}
      </div>
      {tabs}
    </div>
  )
}
