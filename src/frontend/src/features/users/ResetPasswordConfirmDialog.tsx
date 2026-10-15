import { useMutation } from '@tanstack/react-query'
import { ConfirmDialog } from '../../shared/components/ConfirmDialog'
import { ApiError } from '../../shared/api/httpClient'
import * as api from './api'
import type { UserSummary } from './types'

interface ResetPasswordConfirmDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  user: UserSummary | null
  onReset: (temporaryPassword: string) => void
}

export function ResetPasswordConfirmDialog({ open, onOpenChange, user, onReset }: ResetPasswordConfirmDialogProps) {
  const mutation = useMutation({
    mutationFn: () => api.resetUserPassword(user!.id),
    onSuccess: (result) => {
      onOpenChange(false)
      onReset(result.temporaryPassword)
    },
  })

  return (
    <ConfirmDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) mutation.reset()
        onOpenChange(next)
      }}
      title="Repor palavra-passe"
      description={
        user
          ? `Será gerada uma nova palavra-passe temporária para ${user.firstName} ${user.lastName}. A palavra-passe atual deixa de ser válida.`
          : ''
      }
      confirmLabel="Repor"
      loading={mutation.isPending}
      error={
        mutation.error
          ? mutation.error instanceof ApiError && mutation.error.status < 500
            ? mutation.error.message
            : 'Não foi possível repor a palavra-passe.'
          : null
      }
      onConfirm={() => mutation.mutate()}
    />
  )
}
