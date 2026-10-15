import { useEffect } from 'react'
import { useForm, Controller } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Dialog } from '../../shared/components/Dialog'
import { FormField } from '../../shared/components/FormField'
import { Input } from '../../shared/components/Input'
import { Select } from '../../shared/components/Select'
import { Button } from '../../shared/components/Button'
import { ApiError } from '../../shared/api/httpClient'
import { roleLabels } from '../../shared/status/statusMaps'
import * as api from './api'
import { editableUserFields, HR_ADDRESS_REQUIRED_MESSAGE, isAddressMissing, toOptionalPhone } from './userFieldRules'

const schema = z
  .object({
    ...editableUserFields,
    email: z.string().min(1, 'Indique o e-mail').email('E-mail inválido'),
    role: z.enum(['Administration', 'HR']),
  })
  .refine((values) => values.role !== 'HR' || !isAddressMissing(values.address), {
    path: ['address'],
    message: HR_ADDRESS_REQUIRED_MESSAGE,
  })

type FormValues = z.infer<typeof schema>

interface CreateUserDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onCreated: (temporaryPassword: string) => void
}

export function CreateUserDialog({ open, onOpenChange, onCreated }: CreateUserDialogProps) {
  const queryClient = useQueryClient()

  const {
    register,
    handleSubmit,
    reset,
    control,
    setError,
    watch,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { firstName: '', lastName: '', email: '', phone: '', role: 'HR', address: '' },
  })

  const role = watch('role')

  useEffect(() => {
    if (open) reset({ firstName: '', lastName: '', email: '', phone: '', role: 'HR', address: '' })
  }, [open, reset])

  const mutation = useMutation({
    mutationFn: (values: FormValues) =>
      api.createUser({
        firstName: values.firstName,
        lastName: values.lastName,
        email: values.email,
        phone: toOptionalPhone(values.phone),
        role: values.role,
        address: values.role === 'HR' ? values.address : undefined,
      }),
    onSuccess: (result) => {
      void queryClient.invalidateQueries({ queryKey: ['users'] })
      onOpenChange(false)
      onCreated(result.temporaryPassword)
    },
    onError: (error) => {
      setError('email', { message: error instanceof ApiError ? error.message : 'Não foi possível criar o utilizador.' })
    },
  })

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="Novo utilizador"
      description="Cria uma conta de acesso à aplicação (não um consultor), com uma palavra-passe temporária de utilização única."
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)}>
            Cancelar
          </Button>
          <Button variant="primary" loading={mutation.isPending} onClick={() => void handleSubmit((v) => mutation.mutate(v))()}>
            Criar utilizador
          </Button>
        </>
      }
    >
      <form className="flex flex-col gap-4" onSubmit={(e) => void handleSubmit((v) => mutation.mutate(v))(e)}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <FormField id="cu-firstName" label="Nome próprio" error={errors.firstName?.message} required>
            <Input id="cu-firstName" invalid={Boolean(errors.firstName)} autoFocus {...register('firstName')} />
          </FormField>
          <FormField id="cu-lastName" label="Apelido" error={errors.lastName?.message} required>
            <Input id="cu-lastName" invalid={Boolean(errors.lastName)} {...register('lastName')} />
          </FormField>
        </div>
        <FormField id="cu-email" label="E-mail" error={errors.email?.message} required>
          <Input id="cu-email" type="email" invalid={Boolean(errors.email)} {...register('email')} />
        </FormField>
        <FormField id="cu-phone" label="Telefone" error={errors.phone?.message} optional>
          <Input id="cu-phone" invalid={Boolean(errors.phone)} {...register('phone')} />
        </FormField>
        <FormField id="cu-role" label="Função" error={errors.role?.message} required>
          <Controller
            name="role"
            control={control}
            render={({ field }) => (
              <Select id="cu-role" {...field}>
                <option value="HR">{roleLabels.HR}</option>
                <option value="Administration">{roleLabels.Administration}</option>
              </Select>
            )}
          />
        </FormField>
        {role === 'HR' && (
          <FormField id="cu-address" label="Morada" error={errors.address?.message} required>
            <Input id="cu-address" invalid={Boolean(errors.address)} {...register('address')} />
          </FormField>
        )}
      </form>
    </Dialog>
  )
}
