import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Dialog } from '../../shared/components/Dialog'
import { FormField } from '../../shared/components/FormField'
import { Input } from '../../shared/components/Input'
import { Button } from '../../shared/components/Button'
import { ApiError } from '../../shared/api/httpClient'
import * as api from './api'
import type { UserSummary } from './types'
import { editableUserFields, toOptionalPhone } from './userFieldRules'

const schema = z.object(editableUserFields)

type FormValues = z.infer<typeof schema>

interface EditUserDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  user: UserSummary | null
}

export function EditUserDialog({ open, onOpenChange, user }: EditUserDialogProps) {
  const queryClient = useQueryClient()

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  useEffect(() => {
    if (open && user) {
      reset({ firstName: user.firstName, lastName: user.lastName, phone: user.phoneNumber ?? '', address: user.address ?? '' })
    }
  }, [open, user, reset])

  const mutation = useMutation({
    mutationFn: (values: FormValues) =>
      api.updateUser(user!.id, { ...values, phone: toOptionalPhone(values.phone), concurrencyStamp: user!.concurrencyStamp }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['users'] })
      onOpenChange(false)
    },
    onError: (error) => {
      setError('firstName', { message: error instanceof ApiError ? error.message : 'Não foi possível guardar.' })
    },
  })

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="Editar utilizador"
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)}>
            Cancelar
          </Button>
          <Button variant="primary" loading={mutation.isPending} onClick={() => void handleSubmit((v) => mutation.mutate(v))()}>
            Guardar alterações
          </Button>
        </>
      }
    >
      <form className="flex flex-col gap-4" onSubmit={(e) => void handleSubmit((v) => mutation.mutate(v))(e)}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <FormField id="eu-firstName" label="Nome próprio" error={errors.firstName?.message} required>
            <Input id="eu-firstName" invalid={Boolean(errors.firstName)} autoFocus {...register('firstName')} />
          </FormField>
          <FormField id="eu-lastName" label="Apelido" error={errors.lastName?.message} required>
            <Input id="eu-lastName" invalid={Boolean(errors.lastName)} {...register('lastName')} />
          </FormField>
        </div>
        <FormField id="eu-phone" label="Telefone" error={errors.phone?.message} optional>
          <Input id="eu-phone" invalid={Boolean(errors.phone)} {...register('phone')} />
        </FormField>
        <FormField
          id="eu-address"
          label="Morada"
          error={errors.address?.message}
          helperText="Obrigatória apenas para utilizadores de RH."
        >
          <Input id="eu-address" invalid={Boolean(errors.address)} {...register('address')} />
        </FormField>
      </form>
    </Dialog>
  )
}
