import { Route, Routes } from 'react-router'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { LoginPage } from './LoginPage'
import { brand } from '../../branding/brand.config'
import { administrationUser } from '../../test/fixtures'
import { json, renderWithProviders, stubApi } from '../../test/renderWithProviders'

function renderLogin(handlers: Parameters<typeof stubApi>[0] = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(401, { title: 'Unauthorized' }),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf-test-token' }),
    ...handlers,
  })
  renderWithProviders(
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/" element={<p>Área autenticada</p>} />
    </Routes>,
    { route: '/login' },
  )
  return fetchMock
}

describe('LoginPage', () => {
  it('renders the product lockup, the heading and the two labelled fields — and nothing the product does not have', async () => {
    renderLogin()

    expect(await screen.findByRole('heading', { name: 'Iniciar sessão' })).toBeInTheDocument()
    expect(screen.getByText('Aceda com as suas credenciais internas.')).toBeInTheDocument()
    expect(screen.getByLabelText(/E-mail/)).toHaveAttribute('type', 'email')
    expect(screen.getByLabelText(/Palavra-passe/)).toHaveAttribute('type', 'password')
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeInTheDocument()
    expect(screen.getAllByAltText(brand.shortName).length).toBeGreaterThan(0)
    expect(
      screen.getAllByText(`© ${new Date().getFullYear()} ${brand.organizationName}`).length,
    ).toBeGreaterThan(0)

    // No password-visibility toggle, forgot-password, registration, SSO or MFA.
    expect(screen.getAllByRole('button')).toHaveLength(1)
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })

  it('shows the existing validation copy for empty and malformed input', async () => {
    const user = userEvent.setup()
    renderLogin()

    await user.click(await screen.findByRole('button', { name: 'Entrar' }))
    expect(await screen.findByText('Indique o e-mail')).toBeInTheDocument()
    expect(screen.getByText('Indique a palavra-passe')).toBeInTheDocument()
    expect(screen.getByLabelText(/E-mail/)).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByLabelText(/E-mail/)).toHaveAttribute('aria-describedby', 'email-error')

    await user.type(screen.getByLabelText(/E-mail/), 'eva.example@example')
    await user.click(screen.getByRole('button', { name: 'Entrar' }))
    expect(await screen.findByText('E-mail inválido')).toBeInTheDocument()
  })

  it('renders the API error message in an alert and keeps the form usable', async () => {
    const user = userEvent.setup()
    renderLogin({
      'POST /api/auth/login': () => json(401, { title: 'Credenciais inválidas.' }),
    })

    await user.type(await screen.findByLabelText(/E-mail/), 'eva.example@example.test')
    await user.type(screen.getByLabelText(/Palavra-passe/), 'not-the-password')
    await user.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Credenciais inválidas.')
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeEnabled()
  })

  it('shows a loading state that blocks a second submit, sends the CSRF header, then redirects on success', async () => {
    const user = userEvent.setup()
    let resolveLogin: (response: Response) => void = () => {}
    const fetchMock = renderLogin({
      'POST /api/auth/login': () => new Promise<Response>((resolve) => (resolveLogin = resolve)),
    })

    await user.type(await screen.findByLabelText(/E-mail/), 'eva.example@example.test')
    await user.type(screen.getByLabelText(/Palavra-passe/), 'correct-password')
    await user.click(screen.getByRole('button', { name: 'Entrar' }))

    const submit = await screen.findByRole('button', { name: 'A entrar…' })
    expect(submit).toBeDisabled()
    expect(submit).toHaveAttribute('aria-busy', 'true')

    const loginCall = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')
    expect(loginCall?.[1]?.headers).toMatchObject({ 'X-CSRF-TOKEN': 'csrf-test-token' })
    expect(loginCall?.[1]?.credentials).toBe('include')

    resolveLogin(json(200, administrationUser))

    await waitFor(() => expect(screen.getByText('Área autenticada')).toBeInTheDocument())
  })
})
