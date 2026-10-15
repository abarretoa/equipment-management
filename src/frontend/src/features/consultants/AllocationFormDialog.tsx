import { useEffect, type ChangeEvent } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Dialog } from '../../shared/components/Dialog'
import { FormField } from '../../shared/components/FormField'
import { Input } from '../../shared/components/Input'
import { Select } from '../../shared/components/Select'
import { DateField } from '../../shared/components/DateField'
import { Button } from '../../shared/components/Button'
import { ApiError } from '../../shared/api/httpClient'
import { workforceTypeLabels, engagementTypeLabels } from '../../shared/status/statusMaps'
import { listCompanies } from '../companies/api'
import * as api from './api'

const schema = z
  .object({
    workforceType: z.enum(['Internal', 'External']),
    engagementType: z.enum(['ServiceProvider', 'EmploymentContract']),
    roleOrFunction: z.string().min(1, 'Indique a função').max(200),
    companyId: z.string().optional(),
    startDate: z.string().min(1, 'Indique a data de início'),
    endDate: z.string().optional(),
  })
  .superRefine((values, ctx) => {
    if (values.workforceType === 'External' && !values.companyId) {
      ctx.addIssue({ code: 'custom', path: ['companyId'], message: 'Indique a empresa para um vínculo externo' })
    }
  })

type FormValues = z.infer<typeof schema>

interface AllocationFormDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  consultantId: string
}

/** Creates a new consultant allocation — ending the current one, if any, is a separate action on the detail page. */
export function AllocationFormDialog({ open, onOpenChange, consultantId }: AllocationFormDialogProps) {
  const queryClient = useQueryClient()

  const {
    register,
    handleSubmit,
    reset,
    watch,
    setValue,
    setError,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      workforceType: 'Internal',
      engagementType: 'ServiceProvider',
      roleOrFunction: '',
      companyId: '',
      startDate: '',
      endDate: '',
    },
  })

  useEffect(() => {
    if (open) {
      reset({
        workforceType: 'Internal',
        engagementType: 'ServiceProvider',
        roleOrFunction: '',
        companyId: '',
        startDate: '',
        endDate: '',
      })
    }
  }, [open, reset])

  const workforceType = watch('workforceType')

  const companiesQuery = useQuery({
    queryKey: ['companies', 'active-picker'],
    queryFn: () => listCompanies({ isActive: true, page: 1, pageSize: 100 }),
    enabled: open,
  })

  const mutation = useMutation({
    mutationFn: (values: FormValues) =>
      api.createAllocation(consultantId, {
        workforceType: values.workforceType,
        engagementType: values.engagementType,
        roleOrFunction: values.roleOrFunction,
        companyId: values.workforceType === 'External' ? values.companyId : undefined,
        startDate: values.startDate,
        endDate: values.endDate || undefined,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['consultants', consultantId] })
      onOpenChange(false)
    },
    onError: (error) => {
      setError('roleOrFunction', { message: error instanceof ApiError ? error.message : 'Não foi possível guardar.' })
    },
  })

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="Nova alocação"
      description="Regista um novo período de vínculo para este consultor."
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
            Criar alocação
          </Button>
        </>
      }
    >
      <form className="flex flex-col gap-4" onSubmit={(e) => void handleSubmit((v) => mutation.mutate(v))(e)}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <FormField id="workforceType" label="Regime" error={errors.workforceType?.message} required>
            <Select
              id="workforceType"
              invalid={Boolean(errors.workforceType)}
              {...register('workforceType', {
                onChange: (e: ChangeEvent<HTMLSelectElement>) => {
                  if (e.target.value === 'Internal') setValue('companyId', '')
                },
              })}
            >
              {Object.entries(workforceTypeLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </Select>
          </FormField>
          <FormField id="engagementType" label="Tipo de vínculo" error={errors.engagementType?.message} required>
            <Select id="engagementType" invalid={Boolean(errors.engagementType)} {...register('engagementType')}>
              {Object.entries(engagementTypeLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </Select>
          </FormField>
        </div>

        {workforceType === 'External' && (
          <FormField id="companyId" label="Empresa" error={errors.companyId?.message} required>
            <Select id="companyId" invalid={Boolean(errors.companyId)} {...register('companyId')}>
              <option value="">Selecione uma empresa…</option>
              {companiesQuery.data?.items.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.name}
                </option>
              ))}
            </Select>
          </FormField>
        )}

        <FormField id="roleOrFunction" label="Função" error={errors.roleOrFunction?.message} required>
          <Input id="roleOrFunction" invalid={Boolean(errors.roleOrFunction)} {...register('roleOrFunction')} />
        </FormField>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <FormField id="startDate" label="Data de início" error={errors.startDate?.message} required>
            <DateField id="startDate" invalid={Boolean(errors.startDate)} {...register('startDate')} />
          </FormField>
          <FormField id="endDate" label="Data de fim" error={errors.endDate?.message} optional>
            <DateField id="endDate" invalid={Boolean(errors.endDate)} {...register('endDate')} />
          </FormField>
        </div>
      </form>
    </Dialog>
  )
}
