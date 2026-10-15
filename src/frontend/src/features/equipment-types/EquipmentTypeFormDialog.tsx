import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Dialog } from '../../shared/components/Dialog'
import { FormField } from '../../shared/components/FormField'
import { Input } from '../../shared/components/Input'
import { Select } from '../../shared/components/Select'
import { Button } from '../../shared/components/Button'
import { ApiError } from '../../shared/api/httpClient'
import * as api from './api'
import type { EquipmentType } from './types'

const schema = z.object({
  name: z.string().min(1, 'Indique o nome').max(200),
  kind: z.enum(['Generic', 'Laptop', 'MobilePhone']),
  assetTagPrefix: z
    .string()
    .min(1, 'Indique o prefixo')
    .max(6, 'Máximo de 6 caracteres')
    .regex(/^[A-Za-z0-9]+$/, 'Apenas letras e números'),
})

type FormValues = z.infer<typeof schema>

interface EquipmentTypeFormDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  equipmentType?: EquipmentType
}

export function EquipmentTypeFormDialog({ open, onOpenChange, equipmentType }: EquipmentTypeFormDialogProps) {
  const queryClient = useQueryClient()
  const isEdit = Boolean(equipmentType)

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', kind: 'Generic', assetTagPrefix: '' },
  })

  useEffect(() => {
    if (open) {
      reset({
        name: equipmentType?.name ?? '',
        kind: equipmentType?.kind ?? 'Generic',
        assetTagPrefix: equipmentType?.assetTagPrefix ?? '',
      })
    }
  }, [open, equipmentType, reset])

  const mutation = useMutation({
    mutationFn: (values: FormValues) =>
      isEdit
        ? api.updateEquipmentType(equipmentType!.id, { name: values.name, assetTagPrefix: values.assetTagPrefix })
        : api.createEquipmentType(values),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['equipment-types'] })
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
      title={isEdit ? 'Editar tipo de equipamento' : 'Novo tipo de equipamento'}
      description={isEdit ? 'Atualize o nome e o prefixo do identificador.' : 'Defina a categoria e o prefixo dos identificadores deste tipo.'}
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
            {isEdit ? 'Guardar alterações' : 'Criar tipo de equipamento'}
          </Button>
        </>
      }
    >
      <form className="flex flex-col gap-4" onSubmit={(e) => void handleSubmit((v) => mutation.mutate(v))(e)}>
        <FormField id="et-name" label="Nome" error={errors.name?.message} required>
          <Input id="et-name" invalid={Boolean(errors.name)} autoFocus {...register('name')} />
        </FormField>
        <FormField
          id="et-kind"
          label="Categoria"
          error={errors.kind?.message}
          required
          helperText={isEdit ? 'A categoria não pode ser alterada após a criação.' : undefined}
        >
          <Select id="et-kind" disabled={isEdit} invalid={Boolean(errors.kind)} {...register('kind')}>
            <option value="Generic">Genérico</option>
            <option value="Laptop">Portátil</option>
            <option value="MobilePhone">Telemóvel</option>
          </Select>
        </FormField>
        <FormField
          id="et-prefix"
          label="Prefixo do identificador"
          error={errors.assetTagPrefix?.message}
          required
          helperText="Ex.: LT gera identificadores como LT-0023."
        >
          <Input
            id="et-prefix"
            invalid={Boolean(errors.assetTagPrefix)}
            className="uppercase"
            maxLength={6}
            {...register('assetTagPrefix')}
          />
        </FormField>
      </form>
    </Dialog>
  )
}
