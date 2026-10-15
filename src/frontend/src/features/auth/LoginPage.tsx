import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Navigate, useLocation } from 'react-router'
import { AlertCircle, ShieldCheck } from 'lucide-react'
import { Logo } from '../../branding/Logo'
import { brand } from '../../branding/brand.config'
import { FormField } from '../../shared/components/FormField'
import { Button } from '../../shared/components/Button'
import { cn } from '../../shared/utils/cn'
import { useAuth } from './AuthContext'

const loginSchema = z.object({
  email: z.string().min(1, 'Indique o e-mail').email('E-mail inválido'),
  password: z.string().min(1, 'Indique a palavra-passe'),
})

type LoginFormValues = z.infer<typeof loginSchema>

/** Login controls: 40px on desktop, 44px with 16px text on touch (no iOS zoom). */
function fieldClass(invalid: boolean) {
  return cn(
    'h-11 w-full rounded-md border bg-surface px-3 text-base text-fg placeholder:text-fg-subtle lg:h-10 lg:text-sm',
    'transition-[border-color,box-shadow] duration-[120ms] motion-reduce:transition-none',
    'focus-visible:border-primary focus-visible:shadow-[0_0_0_3px_rgb(23_24_26/0.16)] focus-visible:outline-none',
    invalid ? 'border-danger shadow-[0_0_0_1px_var(--color-danger)]' : 'border-border-strong',
  )
}

function InternalAccessFooter({ tone }: { tone: 'light' | 'dark' }) {
  return (
    <div
      className={cn(
        'flex items-center justify-between text-[12px]',
        tone === 'dark' ? 'text-rail-fg-muted' : 'text-fg-muted',
      )}
    >
      <span className="flex items-center gap-2">
        <ShieldCheck className="size-3.5" aria-hidden />
        {/* Template strings keep each line one text node, as before the brand was configurable. */}
        {`Acesso interno ${brand.organizationName}`}
      </span>
      <span>
        © {new Date().getFullYear()}
        {` ${brand.organizationName}`}
      </span>
    </div>
  )
}

export function LoginPage() {
  const { user, login, isLoggingIn, loginError } = useAuth()
  const location = useLocation()

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<LoginFormValues>({ resolver: zodResolver(loginSchema) })

  if (user) {
    const state = location.state as { from?: { pathname?: string } } | null
    const redirectTo = state?.from?.pathname ?? '/'
    return <Navigate to={redirectTo} replace />
  }

  async function onSubmit(values: LoginFormValues) {
    try {
      await login(values.email, values.password)
    } catch {
      // surfaced via loginError below
    }
  }

  return (
    <div className="flex min-h-dvh flex-col bg-bg lg:grid lg:grid-cols-[42%_58%]">
      {/* Brand field — desktop: full-height ink panel; below lg: the 56px dark header. */}
      <div className="flex h-14 shrink-0 items-center bg-rail px-5 lg:hidden">
        <Logo size="md" tone="dark" />
      </div>
      <div className="hidden flex-col bg-rail px-12 lg:flex">
        <div className="flex h-28 items-center">
          <Logo size="lg" tone="dark" />
        </div>
        <div className="mt-40">
          <p className="font-mono text-[11px] font-medium tracking-[0.06em] text-rail-fg-muted uppercase">
            Gestão de equipamentos
          </p>
          <p className="mt-3.5 max-w-[380px] text-[26px] leading-[34px] font-medium tracking-[-0.015em] text-rail-fg-active">
            {`Inventário, entregas e custódia de equipamentos ${brand.organizationName}.`}
          </p>
        </div>
        <div className="mt-auto flex h-16 items-center border-t border-rail-border">
          <div className="w-full">
            <InternalAccessFooter tone="dark" />
          </div>
        </div>
      </div>

      {/* Authentication panel */}
      <div className="flex flex-1 flex-col lg:justify-center lg:pl-28">
        <div className="w-full max-w-[448px] px-5 pt-10 pb-10 lg:w-[368px] lg:px-0 lg:pt-0">
          <h1 className="text-2xl leading-[30px] font-semibold tracking-[-0.015em] text-fg">
            Iniciar sessão
          </h1>
          <p className="mt-1.5 text-[15px] leading-[22px] text-fg-muted lg:text-sm lg:leading-[21px]">
            Aceda com as suas credenciais internas.
          </p>

          <form
            className="mt-7 flex flex-col gap-4"
            onSubmit={(e) => void handleSubmit(onSubmit)(e)}
            noValidate
          >
            <FormField id="email" label="E-mail" error={errors.email?.message} required>
              <input
                id="email"
                type="email"
                autoComplete="username"
                aria-invalid={errors.email ? true : undefined}
                aria-describedby={errors.email ? 'email-error' : undefined}
                className={fieldClass(Boolean(errors.email))}
                {...register('email')}
              />
            </FormField>

            <FormField
              id="password"
              label="Palavra-passe"
              error={errors.password?.message}
              required
            >
              <input
                id="password"
                type="password"
                autoComplete="current-password"
                aria-invalid={errors.password ? true : undefined}
                aria-describedby={errors.password ? 'password-error' : undefined}
                className={fieldClass(Boolean(errors.password))}
                {...register('password')}
              />
            </FormField>

            {loginError && (
              <div
                className="flex gap-2.5 rounded-md border border-danger-border bg-danger-bg px-3 py-2.5 text-[13px] leading-[19px] text-danger-fg"
                role="alert"
              >
                <AlertCircle className="mt-px size-4 shrink-0 text-danger" aria-hidden />
                <p>{loginError}</p>
              </div>
            )}

            <Button
              type="submit"
              variant="primary"
              size="lg"
              loading={isLoggingIn}
              className="mt-2 w-full"
            >
              {isLoggingIn ? 'A entrar…' : 'Entrar'}
            </Button>
          </form>
        </div>

        <div className="mt-auto border-t border-border px-5 pt-3.5 pb-5 lg:hidden">
          <InternalAccessFooter tone="light" />
        </div>
      </div>
    </div>
  )
}
