import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Dialog } from '../../shared/components/Dialog'
import { FormField } from '../../shared/components/FormField'
import { Select } from '../../shared/components/Select'
import { Button } from '../../shared/components/Button'
import { ApiError } from '../../shared/api/httpClient'
import { roleLabels } from '../../shared/status/statusMaps'
import * as api from './api'
import type { UserRole, UserSummary } from './types'

interface ChangeRoleDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  user: UserSummary | null
}

/**
 * Reset-on-open-for-a-different-user is handled by the caller keying this
 * component on `user?.id` (rather than a setState-in-effect here) — see
 * UsersPage, matching React's own guidance to derive reset-on-prop-change via
 * remount instead of an effect.
 */
export function ChangeRoleDialog({ open, onOpenChange, user }: ChangeRoleDialogProps) {
  const queryClient = useQueryClient()
  const [role, setRole] = useState<UserRole>(user?.role ?? 'HR')
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: () => api.changeUserRole(user!.id, role, user!.concurrencyStamp),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['users'] })
      onOpenChange(false)
    },
    onError: (err) => {
      setError(err instanceof ApiError ? err.message : 'Não foi possível alterar a função.')
    },
  })

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="Alterar função"
      description={user ? `${user.firstName} ${user.lastName}` : undefined}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)}>
            Cancelar
          </Button>
          <Button variant="primary" loading={mutation.isPending} onClick={() => mutation.mutate()} disabled={role === user?.role}>
            Guardar alterações
          </Button>
        </>
      }
    >
      <FormField id="role-select" label="Função">
        <Select id="role-select" value={role} onChange={(e) => setRole(e.target.value as UserRole)}>
          <option value="HR">{roleLabels.HR}</option>
          <option value="Administration">{roleLabels.Administration}</option>
        </Select>
      </FormField>
      {error && (
        <p className="mt-3 rounded-md border border-danger-border bg-danger-bg px-3 py-2 text-[13px] text-danger" role="alert">
          {error}
        </p>
      )}
    </Dialog>
  )
}
