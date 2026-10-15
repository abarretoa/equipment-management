import { CircleAlert, TriangleAlert } from 'lucide-react'
import type { IssueDto } from '../../shared/api/types'
import { cn } from '../../shared/utils/cn'

/**
 * A row's validation issues as a compact list — the server's own messages,
 * never a raw JSON dump. Blocking errors and warnings differ by icon and an
 * announced prefix as well as colour.
 */
export function ImportIssuesCell({ issues }: { issues: IssueDto[] }) {
  if (issues.length === 0) {
    return <span className="text-fg-subtle">—</span>
  }

  return (
    <ul className="flex flex-col gap-1">
      {issues.map((issue, index) => {
        const isError = issue.severity === 'Error'
        const Icon = isError ? CircleAlert : TriangleAlert
        return (
          <li key={index} className={cn('flex items-start gap-1.5 text-[12.5px] leading-[17px]', isError ? 'text-danger' : 'text-warning')}>
            <Icon className="mt-px size-3.5 shrink-0" aria-hidden />
            <span>
              <span className="sr-only">{isError ? 'Erro: ' : 'Aviso: '}</span>
              {issue.message}
            </span>
          </li>
        )
      })}
    </ul>
  )
}
