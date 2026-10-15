import { CircleAlert, CircleCheck, Rows3, TriangleAlert, type LucideIcon } from 'lucide-react'

interface ImportSummaryStatsProps {
  totalRows: number
  validRows: number
  warningRows: number
  errorRows: number
}

/** Total/valid/warning/error counters above a preview table — each one icon + label + number, never colour alone. */
export function ImportSummaryStats({ totalRows, validRows, warningRows, errorRows }: ImportSummaryStatsProps) {
  const stats: { label: string; value: number; icon: LucideIcon; iconClass: string }[] = [
    { label: 'Total de linhas', value: totalRows, icon: Rows3, iconClass: 'text-fg-muted' },
    { label: 'Válidas', value: validRows, icon: CircleCheck, iconClass: 'text-success' },
    { label: 'Avisos', value: warningRows, icon: TriangleAlert, iconClass: 'text-warning' },
    { label: 'Erros', value: errorRows, icon: CircleAlert, iconClass: 'text-danger' },
  ]

  return (
    <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-md border border-border bg-border sm:grid-cols-4">
      {stats.map(({ label, value, icon: Icon, iconClass }) => (
        <div key={label} className="flex flex-col gap-0.5 bg-surface px-3.5 py-2.5">
          <dt className="flex items-center gap-1.5 text-[12px] leading-4 text-fg-muted">
            <Icon className={`size-3.5 shrink-0 ${iconClass}`} aria-hidden />
            {label}
          </dt>
          <dd className="text-[20px] leading-7 font-semibold text-fg tabular-nums">{value}</dd>
        </div>
      ))}
    </dl>
  )
}
