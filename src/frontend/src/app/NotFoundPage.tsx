import { Link } from 'react-router'
import { Compass } from 'lucide-react'

export function NotFoundPage() {
  return (
    <div className="flex min-h-[60vh] flex-col items-center justify-center gap-4 px-6 py-20 text-center">
      <span className="flex size-11 items-center justify-center rounded-lg border border-border bg-surface-sunken text-fg-muted">
        <Compass className="size-5" aria-hidden />
      </span>
      <div className="flex flex-col gap-1">
        <h1 className="text-base leading-6 font-semibold text-fg">Página não encontrada</h1>
        <p className="max-w-sm text-[13px] leading-5 text-fg-muted">
          O endereço que introduziu não corresponde a nenhuma página desta aplicação. Verifique o endereço ou utilize o menu.
        </p>
      </div>
      <Link
        to="/"
        className="inline-flex h-9 items-center justify-center rounded-md border border-border-control bg-surface px-3.5 text-[13px] font-medium text-fg hover:border-border-strong hover:bg-surface-sunken max-sm:h-11"
      >
        Voltar ao Dashboard
      </Link>
    </div>
  )
}
