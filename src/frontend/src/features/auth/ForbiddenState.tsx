import { Link } from 'react-router'
import { ShieldAlert } from 'lucide-react'

const actionLinkClass =
  'inline-flex h-9 items-center justify-center rounded-md border border-border-control bg-surface px-3.5 text-[13px] font-medium text-fg hover:border-border-strong hover:bg-surface-sunken max-sm:h-11'

/**
 * Shown when the frontend already knows the current role lacks a permission —
 * not a fallback for the API's own 403s. Never names the permission or policy.
 */
export function ForbiddenState() {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-4 px-6 py-20 text-center">
      <span className="flex size-11 items-center justify-center rounded-lg border border-warning-border bg-warning-bg text-warning">
        <ShieldAlert className="size-5" aria-hidden />
      </span>
      <div className="flex flex-col gap-1">
        <h1 className="text-base leading-6 font-semibold text-fg">Sem permissão de acesso</h1>
        <p className="max-w-sm text-[13px] leading-5 text-fg-muted">
          A sua conta não tem acesso a esta área. Contacte a Administração se precisar de permissões adicionais.
        </p>
      </div>
      <Link to="/" className={actionLinkClass}>
        Voltar ao Dashboard
      </Link>
    </div>
  )
}
