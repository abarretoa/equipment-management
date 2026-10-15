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
import type { Company } from './types'

const schema = z.object({
  name: z.string().min(1, 'Indique o nome').max(200, 'Máximo de 200 caracteres'),
})

type FormValues = z.infer<typeof schema>

interface CompanyFormDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  company?: Company
}

export function CompanyFormDialog({ open, onOpenChange, company }: CompanyFormDialogProps) {
  const queryClient = useQueryClient()
  const isEdit = Boolean(company)

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { name: company?.name ?? '' } })

  useEffect(() => {
    if (open) reset({ name: company?.name ?? '' })
  }, [open, company, reset])

  const mutation = useMutation({
    mutationFn: (values: FormValues) =>
      isEdit ? api.updateCompany(company!.id, values) : api.createCompany(values),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['companies'] })
      onOpenChange(false)
    },
    onError: (error) => {
      setError('name', { message: error instanceof ApiError ? error.message : 'Não foi possível guardar.' })
    },
  })

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={isEdit ? 'Editar empresa' : 'Nova empresa'}
      description={isEdit ? 'Atualize o nome da empresa cliente.' : 'Registe uma empresa cliente para alocações de consultores externos.'}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)}>
            Cancelar
          </Button>
          <Button
            variant="primary"
            loading={mutation.isPending}
            onClick={() => void handleSubmit((v) => mutation.mutate(v))()}
          >
            {isEdit ? 'Guardar alterações' : 'Criar empresa'}
          </Button>
        </>
      }
    >
      <form className="flex flex-col gap-4" onSubmit={(e) => void handleSubmit((v) => mutation.mutate(v))(e)}>
        <FormField id="company-name" label="Nome" error={errors.name?.message} required>
          <Input id="company-name" invalid={Boolean(errors.name)} autoFocus {...register('name')} />
        </FormField>
      </form>
    </Dialog>
  )
}
