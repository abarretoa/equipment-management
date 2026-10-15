import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router'
import { PageHeader } from '../../shared/components/PageHeader'
import { ContentWidth } from '../../shared/components/ContentWidth'
import { FormField } from '../../shared/components/FormField'
import { Input } from '../../shared/components/Input'
import { Textarea } from '../../shared/components/Textarea'
import { Button } from '../../shared/components/Button'
import { Skeleton } from '../../shared/components/Skeleton'
import { ErrorState } from '../../shared/components/ErrorState'
import { ApiError } from '../../shared/api/httpClient'
import * as api from './api'
import type { ConsultantDetail, ConsultantFormValues } from './types'

const schema = z.object({
  fullName: z.string().min(1, 'Indique o nome completo').max(300, 'Máximo de 300 caracteres'),
  nif: z.string().min(1, 'Indique o NIF'),
  email: z.string().min(1, 'Indique o e-mail').email('E-mail inválido'),
  address: z.string().min(1, 'Indique a morada').max(500, 'Máximo de 500 caracteres'),
  phone: z.string().min(1, 'Indique o telefone'),
})

interface ConsultantFormBodyProps {
  consultant?: ConsultantDetail
}

/** Full-page create/edit form — several required fields including sensitive PII, so a dialog would feel cramped. */
function ConsultantFormBody({ consultant }: ConsultantFormBodyProps) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const isEdit = Boolean(consultant)

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<ConsultantFormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      fullName: consultant?.fullName ?? '',
      nif: consultant?.nif ?? '',
      email: consultant?.email ?? '',
      address: consultant?.address ?? '',
      phone: consultant?.phone ?? '',
    },
  })

  const mutation = useMutation({
    mutationFn: (values: ConsultantFormValues) =>
      isEdit ? api.updateConsultant(consultant!.id, values) : api.createConsultant(values),
    onSuccess: (result) => {
      void queryClient.invalidateQueries({ queryKey: ['consultants'] })
      void navigate(`/consultores/${result.id}`)
    },
    onError: (error) => {
      setError('fullName', { message: error instanceof ApiError ? error.message : 'Não foi possível guardar.' })
    },
  })

  return (
    <div className="flex flex-col">
      <PageHeader
        title={isEdit ? 'Editar consultor' : 'Novo consultor'}
        breadcrumbs={[
          { label: 'Consultores', to: '/consultores' },
          { label: isEdit ? consultant!.fullName : 'Novo consultor' },
        ]}
      />
      {/* Visual System v1 form: no card, one compact column, groups separated by a hairline. */}
      <ContentWidth variant="compact" className="px-4 py-6 sm:px-6">
        <form className="flex flex-col" onSubmit={(e) => void handleSubmit((v) => mutation.mutate(v))(e)} noValidate>
          <section aria-labelledby="consultant-form-personal" className="flex flex-col gap-4 border-b border-border pb-6">
            <h2 id="consultant-form-personal" className="text-[14px] leading-5 font-semibold text-fg">
              Dados pessoais
            </h2>
            <FormField id="fullName" label="Nome completo" error={errors.fullName?.message} required>
              <Input id="fullName" invalid={Boolean(errors.fullName)} autoFocus {...register('fullName')} />
            </FormField>
            <FormField id="nif" label="NIF" error={errors.nif?.message} required className="sm:max-w-xs">
              <Input id="nif" invalid={Boolean(errors.nif)} {...register('nif')} />
            </FormField>
          </section>

          <section aria-labelledby="consultant-form-contacts" className="flex flex-col gap-4 pt-6">
            <h2 id="consultant-form-contacts" className="text-[14px] leading-5 font-semibold text-fg">
              Contactos
            </h2>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <FormField id="email" label="E-mail" error={errors.email?.message} required>
                <Input id="email" type="email" invalid={Boolean(errors.email)} {...register('email')} />
              </FormField>
              <FormField id="phone" label="Telefone" error={errors.phone?.message} required>
                <Input id="phone" invalid={Boolean(errors.phone)} {...register('phone')} />
              </FormField>
            </div>
            <FormField id="address" label="Morada" error={errors.address?.message} required>
              <Textarea id="address" invalid={Boolean(errors.address)} {...register('address')} />
            </FormField>
          </section>

          <div className="mt-6 flex flex-col-reverse gap-2 border-t border-border pt-4 sm:flex-row sm:justify-end">
            <Button type="button" variant="secondary" className="max-sm:h-11" onClick={() => { void navigate(-1) }}>
              Cancelar
            </Button>
            <Button type="submit" variant="primary" className="max-sm:h-11" loading={mutation.isPending}>
              {isEdit ? 'Guardar alterações' : 'Criar consultor'}
            </Button>
          </div>
        </form>
      </ContentWidth>
    </div>
  )
}

export function ConsultantCreatePage() {
  return <ConsultantFormBody />
}

export function ConsultantEditPage() {
  const { id } = useParams<{ id: string }>()

  const query = useQuery({
    queryKey: ['consultants', id],
    queryFn: () => api.getConsultant(id!),
    enabled: Boolean(id),
  })

  if (query.isLoading) {
    return (
      <div className="flex flex-col gap-3 px-4 py-6 sm:px-6" aria-busy="true">
        <Skeleton className="h-7 w-56" />
        <Skeleton className="h-80 w-full max-w-[760px]" />
      </div>
    )
  }

  if (query.error || !query.data) {
    return <ErrorState error={query.error} onRetry={() => void query.refetch()} />
  }

  return <ConsultantFormBody consultant={query.data} />
}
