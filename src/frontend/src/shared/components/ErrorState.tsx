import { CloudOff } from 'lucide-react'
import { Button } from './Button'
import { EmptyState } from './EmptyState'
import { ApiError } from '../api/httpClient'

interface ErrorStateProps {
  error: unknown
  onRetry?: () => void
}

/** Calm, useful error messaging — never a raw error object dumped to the screen (Phase 9 scope). */
export function ErrorState({ error, onRetry }: ErrorStateProps) {
  const message =
    error instanceof ApiError ? error.message : 'Ocorreu um erro inesperado. Tente novamente.'

  return (
    <div role="alert">
      <EmptyState
        tone="danger"
        icon={<CloudOff aria-hidden />}
        title="Não foi possível carregar os dados"
        description={message}
        action={
          onRetry && (
            <Button variant="secondary" size="sm" onClick={onRetry}>
              Tentar novamente
            </Button>
          )
        }
      />
    </div>
  )
}
