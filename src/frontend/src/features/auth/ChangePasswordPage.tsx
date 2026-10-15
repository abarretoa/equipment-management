import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Navigate, useNavigate } from 'react-router'
import { CircleCheck } from 'lucide-react'
import { Logo } from '../../branding/Logo'
import { FormField } from '../../shared/components/FormField'
import { Input } from '../../shared/components/Input'
import { Button } from '../../shared/components/Button'
import { PageHeader } from '../../shared/components/PageHeader'
import { ContentWidth } from '../../shared/components/ContentWidth'
import { ApiError } from '../../shared/api/httpClient'
import { useAuth } from './AuthContext'
import * as authApi from './api'

const PASSWORD_MIN_LENGTH = 12

const schema = z
  .object({
    currentPassword: z.string().min(1, 'Indique a palavra-passe atual'),
    // Mirrors the server's Identity policy (RequiredLength = 12); the server
    // also requires a digit and an upper- and lower-case letter, and reports
    // those itself.
    newPassword: z.string().min(PASSWORD_MIN_LENGTH, `A nova palavra-passe deve ter pelo menos ${PASSWORD_MIN_LENGTH} caracteres`),
    confirmPassword: z.string().min(1, 'Confirme a nova palavra-passe'),
  })
  .refine((values) => values.newPassword === values.confirmPassword, {
    path: ['confirmPassword'],
    message: 'As palavras-passe não coincidem',
  })

type FormValues = z.infer<typeof schema>

/**
 * The API keys each error by the request field it concerns (decided from
 * Identity's error code — AuthController.ChangePasswordErrorField): wrong
 * current password under "CurrentPassword", password-policy failures under
 * "NewPassword". Anything else is shown for the whole form.
 */
const serverErrorFields: Partial<Record<string, 'currentPassword' | 'newPassword'>> = {
  CurrentPassword: 'currentPassword',
  NewPassword: 'newPassword',
}

/**
 * Reachable both as the forced first-login flow (RequireAuth redirects here
 * while `mustChangePassword` is true — every other endpoint is blocked by
 * RequirePasswordChangeMiddleware) and as a normal self-service screen.
 */
export function ChangePasswordPage() {
  const { user } = useAuth()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [done, setDone] = useState(false)
  const isForced = user?.mustChangePassword ?? false
  // Captured once: a successful change refetches /me, which flips
  // mustChangePassword (and so isForced) to false before `done` is set.
  const [startedForced] = useState(isForced)
  const [formError, setFormError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  const mutation = useMutation({
    mutationFn: (values: FormValues) => authApi.changePassword(values.currentPassword, values.newPassword),
    onMutate: () => setFormError(null),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['auth', 'me'] })
      setDone(true)
    },
    onError: (error) => {
      const fieldErrors = error instanceof ApiError ? error.problem?.errors : undefined
      const unattributed: string[] = []

      for (const [key, messages] of Object.entries(fieldErrors ?? {})) {
        const field = serverErrorFields[key]
        if (field) {
          setError(field, { message: messages.join(' ') })
        } else {
          unattributed.push(...messages)
        }
      }

      if (unattributed.length > 0) {
        setFormError(unattributed.join(' '))
      } else if (!fieldErrors) {
        setFormError(error instanceof ApiError ? error.message : 'Não foi possível alterar a palavra-passe.')
      }
    },
  })

  // Forced first-login flow: straight into the app once the password is changed.
  if (done && startedForced) {
    return <Navigate to="/" replace />
  }

  const form = done ? (
    <div className="flex flex-col gap-4">
      <p
        className="flex items-start gap-2 rounded-md border border-success-border bg-success-bg px-3 py-2.5 text-[13px] leading-[19px] text-fg"
        role="status"
      >
        <CircleCheck className="mt-px size-4 shrink-0 text-success" aria-hidden />
        Palavra-passe alterada com sucesso.
      </p>
      <Button variant="secondary" className="w-full max-sm:h-11 sm:w-fit" onClick={() => void navigate('/perfil')}>
        Voltar ao perfil
      </Button>
    </div>
  ) : (
    <form className="flex flex-col gap-4" onSubmit={(e) => void handleSubmit((v) => mutation.mutate(v))(e)} noValidate>
      <FormField id="currentPassword" label="Palavra-passe atual" error={errors.currentPassword?.message} required>
        <Input
          id="currentPassword"
          type="password"
          autoComplete="current-password"
          invalid={Boolean(errors.currentPassword)}
          {...register('currentPassword')}
        />
      </FormField>
      <FormField
        id="newPassword"
        label="Nova palavra-passe"
        error={errors.newPassword?.message}
        helperText={`Pelo menos ${PASSWORD_MIN_LENGTH} caracteres.`}
        required
      >
        <Input
          id="newPassword"
          type="password"
          autoComplete="new-password"
          invalid={Boolean(errors.newPassword)}
          {...register('newPassword')}
        />
      </FormField>
      <FormField id="confirmPassword" label="Confirmar nova palavra-passe" error={errors.confirmPassword?.message} required>
        <Input
          id="confirmPassword"
          type="password"
          autoComplete="new-password"
          invalid={Boolean(errors.confirmPassword)}
          {...register('confirmPassword')}
        />
      </FormField>
      {formError && (
        <p
          className="rounded-md border border-danger-border bg-danger-bg px-3 py-2.5 text-[13px] leading-[19px] text-danger-fg"
          role="alert"
        >
          {formError}
        </p>
      )}
      <Button type="submit" variant="primary" size={isForced ? 'lg' : 'md'} loading={mutation.isPending} className="mt-1 w-full max-sm:h-11">
        Alterar palavra-passe
      </Button>
    </form>
  )

  // Forced flow: a focused, login-like panel. Every other route still
  // redirects back here (RequireAuth) until the password has been changed.
  if (isForced) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-bg px-4 py-10">
        <div className="w-full max-w-sm">
          <div className="mb-8 flex justify-center">
            <Logo />
          </div>
          <div className="rounded-lg border border-border bg-surface p-6">
            <h1 className="text-base leading-6 font-semibold text-fg">Alterar palavra-passe</h1>
            <p className="mt-1 mb-5 text-[13px] leading-5 text-fg-muted">
              Por motivos de segurança, defina uma nova palavra-passe antes de continuar.
            </p>
            {form}
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="flex flex-col">
      <PageHeader
        title="Alterar palavra-passe"
        description="Escolha uma nova palavra-passe para a sua conta."
        breadcrumbs={[{ label: 'Meu perfil', to: '/perfil' }, { label: 'Alterar palavra-passe' }]}
      />
      <ContentWidth variant="compact" className="px-4 py-6 sm:px-6">
        {/* ≈440px useful width (the shared sm dialog token): not lost on wide desktops, full width on mobile. */}
        <div className="max-w-[440px]">{form}</div>
      </ContentWidth>
    </div>
  )
}
