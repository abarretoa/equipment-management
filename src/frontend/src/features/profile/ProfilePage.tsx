import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { CheckCircle2, KeyRound } from 'lucide-react'
import { PageHeader } from '../../shared/components/PageHeader'
import { ContentWidth } from '../../shared/components/ContentWidth'
import { FormField } from '../../shared/components/FormField'
import { Input } from '../../shared/components/Input'
import { Button } from '../../shared/components/Button'
import { ErrorState } from '../../shared/components/ErrorState'
import { Skeleton } from '../../shared/components/Skeleton'
import { ApiError } from '../../shared/api/httpClient'
import { roleLabels } from '../../shared/status/statusMaps'
import { editableUserFields, HR_ADDRESS_REQUIRED_MESSAGE, isAddressMissing, toOptionalPhone } from '../users/userFieldRules'
import * as api from './api'
import type { Profile } from './types'

const baseSchema = z.object(editableUserFields)

type FormValues = z.infer<typeof baseSchema>

/** Same rule the API enforces on the user's current role — mirrored here only so the message is immediate and in pt-PT. */
function schemaFor(role: Profile['role']) {
  return baseSchema.refine((values) => role !== 'HR' || !isAddressMissing(values.address), {
    path: ['address'],
    message: HR_ADDRESS_REQUIRED_MESSAGE,
  })
}

function toFormValues(profile: Profile): FormValues {
  return {
    firstName: profile.firstName,
    lastName: profile.lastName,
    phone: profile.phoneNumber ?? '',
    address: profile.address ?? '',
  }
}

/** pt-PT text for the API's field-keyed validation errors (ValidationProblemDetails `errors`). */
const serverFieldMessages: Record<string, { field: keyof FormValues; message: string }> = {
  FirstName: { field: 'firstName', message: 'Indique um nome próprio válido (máximo de 200 caracteres).' },
  LastName: { field: 'lastName', message: 'Indique um apelido válido (máximo de 200 caracteres).' },
  Phone: { field: 'phone', message: 'Indique um número de telefone válido.' },
  Address: { field: 'address', message: 'Indique uma morada válida (máximo de 500 caracteres).' },
}

/**
 * "Meu perfil" (ADR-073): the signed-in user's own personal data. Always the
 * current session's account — the page takes no id and the API accepts none.
 * Email and role are shown read-only; the password goes through the existing
 * change-password screen.
 */
export function ProfilePage() {
  const queryClient = useQueryClient()
  const profileQuery = useQuery({ queryKey: api.PROFILE_QUERY_KEY, queryFn: api.getProfile })

  return (
    <div className="flex flex-col">
      <PageHeader title="Meu perfil" description="Consulte e atualize os seus dados pessoais." />
      <ContentWidth variant="compact" className="flex flex-col px-4 py-6 sm:px-6">
        {profileQuery.isLoading ? (
          <ProfileSkeleton />
        ) : profileQuery.isError || !profileQuery.data ? (
          <ErrorState error={profileQuery.error} onRetry={() => void profileQuery.refetch()} />
        ) : (
          <ProfileContent
            profile={profileQuery.data}
            onSaved={(updated) => {
              queryClient.setQueryData(api.PROFILE_QUERY_KEY, updated)
              // The account menu reads the session's name from /api/auth/me —
              // refetch that single source of truth rather than copying it.
              void queryClient.invalidateQueries({ queryKey: ['auth', 'me'] })
            }}
          />
        )}
      </ContentWidth>
    </div>
  )
}

function ProfileContent({ profile, onSaved }: { profile: Profile; onSaved: (updated: Profile) => void }) {
  const navigate = useNavigate()
  const [saved, setSaved] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const roleLabel = roleLabels[profile.role] ?? profile.role

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isDirty },
  } = useForm<FormValues>({ resolver: zodResolver(schemaFor(profile.role)), defaultValues: toFormValues(profile) })

  const mutation = useMutation({
    mutationFn: (values: FormValues) =>
      api.updateProfile({
        firstName: values.firstName,
        lastName: values.lastName,
        phone: toOptionalPhone(values.phone),
        address: values.address ? values.address : null,
      }),
    onMutate: () => {
      setSaved(false)
      setFormError(null)
    },
    onSuccess: (updated) => {
      reset(toFormValues(updated))
      onSaved(updated)
      setSaved(true)
    },
    onError: (error) => {
      const fieldErrors = error instanceof ApiError ? error.problem?.errors : undefined
      const mapped = fieldErrors ? Object.keys(fieldErrors).flatMap((key) => serverFieldMessages[key] ?? []) : []

      if (mapped.length > 0) {
        for (const { field, message } of mapped) {
          setError(field, {
            message: field === 'address' && profile.role === 'HR' ? HR_ADDRESS_REQUIRED_MESSAGE : message,
          })
        }
        return
      }

      setFormError(
        error instanceof ApiError && error.status === 409
          ? 'Os seus dados foram alterados noutra sessão. Recarregue a página e tente novamente.'
          : 'Não foi possível guardar as alterações. Tente novamente.',
      )
    },
  })

  return (
    <>
      <section aria-labelledby="profile-account" className="flex flex-col gap-1 border-b border-border pb-5">
        <h2 id="profile-account" className="text-[14px] leading-5 font-semibold text-fg">
          {profile.firstName} {profile.lastName}
        </h2>
        <p className="text-[13px] leading-5 text-fg-muted">
          {roleLabel} · {profile.email}
        </p>
      </section>

      <section aria-labelledby="profile-personal" className="flex flex-col gap-4 border-b border-border py-6">
        <h2 id="profile-personal" className="text-[14px] leading-5 font-semibold text-fg">
          Dados pessoais
        </h2>
        <form
          className="flex flex-col gap-4"
          onSubmit={(e) =>
            void handleSubmit((values) => {
              if (!mutation.isPending) mutation.mutate(values)
            })(e)
          }
          noValidate
        >
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField id="profile-firstName" label="Nome próprio" error={errors.firstName?.message} required>
              <Input
                id="profile-firstName"
                autoComplete="given-name"
                invalid={Boolean(errors.firstName)}
                aria-describedby={errors.firstName ? 'profile-firstName-error' : undefined}
                {...register('firstName')}
              />
            </FormField>
            <FormField id="profile-lastName" label="Apelido" error={errors.lastName?.message} required>
              <Input
                id="profile-lastName"
                autoComplete="family-name"
                invalid={Boolean(errors.lastName)}
                aria-describedby={errors.lastName ? 'profile-lastName-error' : undefined}
                {...register('lastName')}
              />
            </FormField>
          </div>

          <FormField id="profile-email" label="Email" helperText="O email da conta não pode ser alterado aqui.">
            <Input id="profile-email" value={profile.email} readOnly disabled aria-describedby="profile-email-helper" />
          </FormField>

          <FormField id="profile-phone" label="Telefone" error={errors.phone?.message} optional>
            <Input
              id="profile-phone"
              type="tel"
              autoComplete="tel"
              invalid={Boolean(errors.phone)}
              aria-describedby={errors.phone ? 'profile-phone-error' : undefined}
              {...register('phone')}
            />
          </FormField>

          <FormField
            id="profile-address"
            label="Morada"
            error={errors.address?.message}
            required={profile.role === 'HR'}
            optional={profile.role !== 'HR'}
          >
            <Input
              id="profile-address"
              autoComplete="street-address"
              invalid={Boolean(errors.address)}
              aria-describedby={errors.address ? 'profile-address-error' : undefined}
              {...register('address')}
            />
          </FormField>

          <FormField id="profile-role" label="Função" helperText="Definida pela Administração.">
            <Input id="profile-role" value={roleLabel} readOnly disabled aria-describedby="profile-role-helper" />
          </FormField>

          {formError && (
            <p
              className="rounded-md border border-danger-border bg-danger-bg px-3 py-2.5 text-[13px] leading-[19px] text-danger-fg"
              role="alert"
            >
              {formError}
            </p>
          )}

          <div className="flex flex-col-reverse flex-wrap items-stretch gap-3 border-t border-border pt-4 sm:flex-row sm:items-center sm:justify-end">
            {/* Hidden again as soon as the user starts another edit. */}
            {saved && !isDirty && (
              <p className="flex items-center gap-1.5 text-[13px] text-success sm:mr-auto" role="status">
                <CheckCircle2 className="size-4" aria-hidden />
                Alterações guardadas.
              </p>
            )}
            <Button type="submit" variant="primary" className="max-sm:h-11" loading={mutation.isPending} disabled={!isDirty}>
              Guardar alterações
            </Button>
          </div>
        </form>
      </section>

      <section aria-labelledby="profile-security" className="flex flex-col gap-3 pt-6">
        <h2 id="profile-security" className="text-[14px] leading-5 font-semibold text-fg">
          Segurança
        </h2>
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <p className="text-[13px] font-medium text-fg">Palavra-passe</p>
            <p className="text-[13px] text-fg-muted">Altere a palavra-passe com que inicia sessão.</p>
          </div>
          <Button
            className="max-sm:h-11"
            icon={<KeyRound className="size-4" aria-hidden />}
            onClick={() => void navigate('/alterar-password')}
          >
            Alterar palavra-passe
          </Button>
        </div>
      </section>
    </>
  )
}

function ProfileSkeleton() {
  return (
    <div className="flex flex-col gap-4" role="status" aria-label="A carregar">
      <Skeleton className="h-5 w-48" />
      <Skeleton className="h-4 w-64" />
      {Array.from({ length: 5 }).map((_, index) => (
        <Skeleton key={index} className="h-9 w-full" />
      ))}
    </div>
  )
}
