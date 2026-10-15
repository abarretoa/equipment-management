import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router'
import { PageHeader } from '../../shared/components/PageHeader'
import { ContentWidth } from '../../shared/components/ContentWidth'
import { DetailSection } from '../../shared/components/DetailSection'
import { FormField } from '../../shared/components/FormField'
import { Input } from '../../shared/components/Input'
import { Textarea } from '../../shared/components/Textarea'
import { Select } from '../../shared/components/Select'
import { DateField } from '../../shared/components/DateField'
import { mobileNumberLabel } from '../../branding/brand.config'
import { Button } from '../../shared/components/Button'
import { Skeleton } from '../../shared/components/Skeleton'
import { ErrorState } from '../../shared/components/ErrorState'
import { ApiError } from '../../shared/api/httpClient'
import { listEquipmentTypes } from '../equipment-types/api'
import { ConditionPhotoPicker } from './ConditionPhotoPicker'
import * as api from './api'
import type { EquipmentDetail } from './types'

const REQUIRED_PHOTO_COUNT = 3

/** The API's IMEI errors (400: missing or not 15 digits; 409: already used by another phone) belong next to the IMEI field. */
function isImeiError(error: unknown): boolean {
  return error instanceof ApiError && (error.status === 400 || error.status === 409) && error.message.includes('IMEI')
}

const equipmentFieldsSchema = z.object({
  manufacturer: z.string().optional(),
  model: z.string().optional(),
  serialNumber: z.string().optional(),
  notes: z.string().optional(),
  processor: z.string().optional(),
  memory: z.string().optional(),
  storage: z.string().optional(),
  imei: z.string().optional(),
  mobileNumber: z.string().optional(),
  simSerialNumber: z.string().optional(),
  purchaseDate: z.string().optional(),
  invoiceNumber: z.string().max(100, 'Máximo de 100 caracteres').optional(),
  purchaseVendor: z.string().max(200, 'Máximo de 200 caracteres').optional(),
})

const createSchema = equipmentFieldsSchema.extend({
  equipmentTypeId: z.string().min(1, 'Selecione um tipo de equipamento'),
})

type CreateFormValues = z.infer<typeof createSchema>
type EditFormValues = z.infer<typeof equipmentFieldsSchema>

/** Full-page form — Equipment is a core entity, so it gets its own page rather than a dialog (Phase 9 scope). */
export function EquipmentCreatePage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const equipmentTypesQuery = useQuery({
    queryKey: ['equipment-types', 'active-options'],
    queryFn: () => listEquipmentTypes({ isActive: true, page: 1, pageSize: 100 }),
  })

  const {
    register,
    handleSubmit,
    watch,
    setError,
    formState: { errors },
  } = useForm<CreateFormValues>({
    resolver: zodResolver(createSchema),
    defaultValues: { equipmentTypeId: '' },
  })

  const selectedTypeId = watch('equipmentTypeId')
  const selectedType = equipmentTypesQuery.data?.items.find((t) => t.id === selectedTypeId)
  const isLaptop = selectedType?.kind === 'Laptop'
  const isMobilePhone = selectedType?.kind === 'MobilePhone'
  const requiresPhotos = isLaptop || isMobilePhone

  const [photos, setPhotos] = useState<File[]>([])
  const [photosError, setPhotosError] = useState<string | undefined>()

  const mutation = useMutation({
    mutationFn: (values: CreateFormValues) => {
      const { processor, memory, storage, imei, mobileNumber, simSerialNumber, ...rest } = values
      const laptopSpecification =
        isLaptop && processor && memory && storage ? { processor, memory, storage } : undefined
      const mobilePhoneSpecification = isMobilePhone && imei ? { imei, mobileNumber, simSerialNumber } : undefined
      return api.createEquipment({
        ...rest,
        laptopSpecification,
        mobilePhoneSpecification,
        photos: requiresPhotos ? photos : undefined,
        purchaseDate: rest.purchaseDate || undefined,
      })
    },
    onSuccess: (created) => {
      void queryClient.invalidateQueries({ queryKey: ['equipment'] })
      void navigate(`/equipamentos/${created.id}`)
    },
    onError: (error) => {
      setError(isImeiError(error) ? 'imei' : 'equipmentTypeId', { message: error instanceof ApiError ? error.message : 'Não foi possível guardar.' })
    },
  })

  function onSubmit(values: CreateFormValues) {
    let hasError = false
    if (isLaptop) {
      if (!values.manufacturer) { setError('manufacturer', { message: 'Obrigatório para portáteis' }); hasError = true }
      if (!values.serialNumber) { setError('serialNumber', { message: 'Obrigatório para portáteis' }); hasError = true }
      if (!values.processor) { setError('processor', { message: 'Campo obrigatório' }); hasError = true }
      if (!values.memory) { setError('memory', { message: 'Campo obrigatório' }); hasError = true }
      if (!values.storage) { setError('storage', { message: 'Campo obrigatório' }); hasError = true }
    }
    if (isMobilePhone) {
      if (!values.manufacturer) { setError('manufacturer', { message: 'Obrigatório para telemóveis' }); hasError = true }
      if (!values.imei) { setError('imei', { message: 'Campo obrigatório' }); hasError = true }
    }
    if (requiresPhotos && photos.length !== REQUIRED_PHOTO_COUNT) {
      setPhotosError(`São exigidas exatamente ${REQUIRED_PHOTO_COUNT} fotografias (${photos.length}/${REQUIRED_PHOTO_COUNT} selecionadas).`)
      hasError = true
    } else {
      setPhotosError(undefined)
    }
    if (hasError) return
    mutation.mutate(values)
  }

  return (
    <div className="flex flex-col">
      <PageHeader
        title="Novo equipamento"
        breadcrumbs={[{ label: 'Equipamentos', to: '/equipamentos' }, { label: 'Novo' }]}
      />
      <ContentWidth className="p-6">
      <form className="flex flex-col gap-5" onSubmit={(e) => void handleSubmit(onSubmit)(e)} noValidate>
        <DetailSection title="Identificação">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField
              id="equipmentTypeId"
              label="Tipo de equipamento"
              error={errors.equipmentTypeId?.message}
              required
              helperText="Determina o prefixo do identificador e se são exigidas especificações de portátil."
            >
              <Select id="equipmentTypeId" invalid={Boolean(errors.equipmentTypeId)} {...register('equipmentTypeId')}>
                <option value="">Selecione…</option>
                {equipmentTypesQuery.data?.items.map((type) => (
                  <option key={type.id} value={type.id}>
                    {type.name}
                  </option>
                ))}
              </Select>
            </FormField>
            <FormField id="manufacturer" label="Fabricante" error={errors.manufacturer?.message} required={isLaptop || isMobilePhone} optional={!isLaptop && !isMobilePhone}>
              <Input id="manufacturer" invalid={Boolean(errors.manufacturer)} {...register('manufacturer')} />
            </FormField>
            <FormField id="model" label="Modelo" optional>
              <Input id="model" {...register('model')} />
            </FormField>
            <FormField
              id="serialNumber"
              label="Número de série"
              error={errors.serialNumber?.message}
              required={isLaptop}
              optional={!isLaptop}
            >
              <Input id="serialNumber" invalid={Boolean(errors.serialNumber)} {...register('serialNumber')} />
            </FormField>
          </div>
          <FormField id="notes" label="Observações" optional className="mt-4">
            <Textarea id="notes" {...register('notes')} />
          </FormField>
        </DetailSection>

        {isLaptop && (
          <DetailSection title="Especificações do portátil" description="Obrigatórias para equipamento do tipo Portátil.">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <FormField id="processor" label="Processador" error={errors.processor?.message} required>
                <Input id="processor" invalid={Boolean(errors.processor)} {...register('processor')} />
              </FormField>
              <FormField id="memory" label="Memória RAM" error={errors.memory?.message} required>
                <Input id="memory" invalid={Boolean(errors.memory)} {...register('memory')} />
              </FormField>
              <FormField id="storage" label="Armazenamento" error={errors.storage?.message} required>
                <Input id="storage" invalid={Boolean(errors.storage)} {...register('storage')} />
              </FormField>
            </div>
          </DetailSection>
        )}

        {isMobilePhone && (
          <DetailSection title="Especificações do telemóvel" description="Obrigatórias para equipamento do tipo Telemóvel.">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <FormField id="imei" label="IMEI" error={errors.imei?.message} required>
                <Input id="imei" invalid={Boolean(errors.imei)} {...register('imei')} />
              </FormField>
              <FormField id="mobileNumber" label={mobileNumberLabel} optional>
                <Input id="mobileNumber" {...register('mobileNumber')} />
              </FormField>
              <FormField id="simSerialNumber" label="Número SIM" optional>
                <Input id="simSerialNumber" {...register('simSerialNumber')} />
              </FormField>
            </div>
          </DetailSection>
        )}

        {requiresPhotos && (
          <DetailSection title="Fotografias atuais" description="São exigidas exatamente 3 fotografias do estado atual do equipamento.">
            <ConditionPhotoPicker photos={photos} onChange={setPhotos} disabled={mutation.isPending} error={photosError} />
          </DetailSection>
        )}

        <DetailSection title="Informações de compra">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <FormField id="purchaseDate" label="Data de compra" optional>
              <DateField id="purchaseDate" {...register('purchaseDate')} />
            </FormField>
            <FormField id="invoiceNumber" label="N.º da fatura" optional error={errors.invoiceNumber?.message}>
              <Input id="invoiceNumber" invalid={Boolean(errors.invoiceNumber)} {...register('invoiceNumber')} />
            </FormField>
            <FormField id="purchaseVendor" label="Loja / Fornecedor" optional error={errors.purchaseVendor?.message}>
              <Input id="purchaseVendor" invalid={Boolean(errors.purchaseVendor)} {...register('purchaseVendor')} />
            </FormField>
          </div>
        </DetailSection>

        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={() => void navigate('/equipamentos')}>
            Cancelar
          </Button>
          <Button type="submit" variant="primary" loading={mutation.isPending}>
            Criar equipamento
          </Button>
        </div>
      </form>
      </ContentWidth>
    </div>
  )
}

export function EquipmentEditPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const equipmentQuery = useQuery({
    queryKey: ['equipment', id],
    queryFn: () => api.getEquipment(id!),
    enabled: Boolean(id),
  })

  if (equipmentQuery.isLoading) {
    return (
      <div className="flex flex-col gap-3 p-6">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-40 w-full" />
      </div>
    )
  }

  if (equipmentQuery.error || !equipmentQuery.data) {
    return <ErrorState error={equipmentQuery.error} onRetry={() => void equipmentQuery.refetch()} />
  }

  return (
    <EquipmentEditForm
      equipment={equipmentQuery.data}
      onSaved={() => void navigate(`/equipamentos/${id}`)}
      onCancel={() => void navigate(`/equipamentos/${id}`)}
      onInvalidate={() => void queryClient.invalidateQueries({ queryKey: ['equipment'] })}
    />
  )
}

function EquipmentEditForm({
  equipment,
  onSaved,
  onCancel,
  onInvalidate,
}: {
  equipment: EquipmentDetail
  onSaved: () => void
  onCancel: () => void
  onInvalidate: () => void
}) {
  const isLaptop = Boolean(equipment.laptopSpecification)
  const isMobilePhone = Boolean(equipment.mobilePhoneSpecification)

  const defaultValues = useMemo<EditFormValues>(
    () => ({
      manufacturer: equipment.manufacturer ?? undefined,
      model: equipment.model ?? undefined,
      serialNumber: equipment.serialNumber ?? undefined,
      notes: equipment.notes ?? undefined,
      processor: equipment.laptopSpecification?.processor,
      memory: equipment.laptopSpecification?.memory,
      storage: equipment.laptopSpecification?.storage,
      imei: equipment.mobilePhoneSpecification?.imei,
      mobileNumber: equipment.mobilePhoneSpecification?.mobileNumber ?? undefined,
      simSerialNumber: equipment.mobilePhoneSpecification?.simSerialNumber ?? undefined,
      purchaseDate: equipment.purchaseDate ?? undefined,
      invoiceNumber: equipment.invoiceNumber ?? undefined,
      purchaseVendor: equipment.purchaseVendor ?? undefined,
    }),
    [equipment],
  )

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<EditFormValues>({ resolver: zodResolver(equipmentFieldsSchema), defaultValues })

  const mutation = useMutation({
    mutationFn: (values: EditFormValues) => {
      const laptopSpecification =
        isLaptop && values.processor && values.memory && values.storage
          ? { processor: values.processor, memory: values.memory, storage: values.storage }
          : undefined
      const mobilePhoneSpecification =
        isMobilePhone && values.imei
          ? { imei: values.imei, mobileNumber: values.mobileNumber, simSerialNumber: values.simSerialNumber }
          : undefined
      return api.updateEquipment(equipment.id, {
        ...values,
        laptopSpecification,
        mobilePhoneSpecification,
        purchaseDate: values.purchaseDate || undefined,
      })
    },
    onSuccess: () => {
      onInvalidate()
      onSaved()
    },
    onError: (error) => {
      setError(isImeiError(error) ? 'imei' : 'manufacturer', { message: error instanceof ApiError ? error.message : 'Não foi possível guardar.' })
    },
  })

  function onSubmit(values: EditFormValues) {
    let hasError = false
    if (isLaptop) {
      if (!values.serialNumber) { setError('serialNumber', { message: 'Obrigatório para portáteis' }); hasError = true }
      if (!values.processor) { setError('processor', { message: 'Campo obrigatório' }); hasError = true }
      if (!values.memory) { setError('memory', { message: 'Campo obrigatório' }); hasError = true }
      if (!values.storage) { setError('storage', { message: 'Campo obrigatório' }); hasError = true }
    }
    if (isMobilePhone && !values.imei) {
      setError('imei', { message: 'Campo obrigatório' })
      hasError = true
    }
    if (hasError) return
    mutation.mutate(values)
  }

  return (
    <div className="flex flex-col">
      <PageHeader
        title={`Editar ${equipment.assetTag}`}
        breadcrumbs={[
          { label: 'Equipamentos', to: '/equipamentos' },
          { label: equipment.assetTag, to: `/equipamentos/${equipment.id}` },
          { label: 'Editar' },
        ]}
      />
      <ContentWidth className="p-6">
      <form className="flex flex-col gap-5" onSubmit={(e) => void handleSubmit(onSubmit)(e)} noValidate>
        <DetailSection title="Identificação" description={`Tipo: ${equipment.equipmentTypeName} (não editável)`}>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField id="manufacturer" label="Fabricante" optional>
              <Input id="manufacturer" {...register('manufacturer')} />
            </FormField>
            <FormField id="model" label="Modelo" optional>
              <Input id="model" {...register('model')} />
            </FormField>
            <FormField
              id="serialNumber"
              label="Número de série"
              error={errors.serialNumber?.message}
              required={isLaptop}
              optional={!isLaptop}
            >
              <Input id="serialNumber" invalid={Boolean(errors.serialNumber)} {...register('serialNumber')} />
            </FormField>
          </div>
          <FormField id="notes" label="Observações" optional className="mt-4">
            <Textarea id="notes" {...register('notes')} />
          </FormField>
        </DetailSection>

        {isLaptop && (
          <DetailSection title="Especificações do portátil">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <FormField id="processor" label="Processador" error={errors.processor?.message} required>
                <Input id="processor" invalid={Boolean(errors.processor)} {...register('processor')} />
              </FormField>
              <FormField id="memory" label="Memória RAM" error={errors.memory?.message} required>
                <Input id="memory" invalid={Boolean(errors.memory)} {...register('memory')} />
              </FormField>
              <FormField id="storage" label="Armazenamento" error={errors.storage?.message} required>
                <Input id="storage" invalid={Boolean(errors.storage)} {...register('storage')} />
              </FormField>
            </div>
          </DetailSection>
        )}

        {isMobilePhone && (
          <DetailSection title="Especificações do telemóvel">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <FormField id="imei" label="IMEI" error={errors.imei?.message} required>
                <Input id="imei" invalid={Boolean(errors.imei)} {...register('imei')} />
              </FormField>
              <FormField id="mobileNumber" label={mobileNumberLabel} optional>
                <Input id="mobileNumber" {...register('mobileNumber')} />
              </FormField>
              <FormField id="simSerialNumber" label="Número SIM" optional>
                <Input id="simSerialNumber" {...register('simSerialNumber')} />
              </FormField>
            </div>
          </DetailSection>
        )}

        <DetailSection title="Informações de compra">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <FormField id="purchaseDate" label="Data de compra" optional>
              <DateField id="purchaseDate" {...register('purchaseDate')} />
            </FormField>
            <FormField id="invoiceNumber" label="N.º da fatura" optional error={errors.invoiceNumber?.message}>
              <Input id="invoiceNumber" invalid={Boolean(errors.invoiceNumber)} {...register('invoiceNumber')} />
            </FormField>
            <FormField id="purchaseVendor" label="Loja / Fornecedor" optional error={errors.purchaseVendor?.message}>
              <Input id="purchaseVendor" invalid={Boolean(errors.purchaseVendor)} {...register('purchaseVendor')} />
            </FormField>
          </div>
        </DetailSection>

        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onCancel}>
            Cancelar
          </Button>
          <Button type="submit" variant="primary" loading={mutation.isPending}>
            Guardar alterações
          </Button>
        </div>
      </form>
      </ContentWidth>
    </div>
  )
}
