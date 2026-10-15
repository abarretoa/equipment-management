import { Link, Route, Routes, useLocation } from 'react-router'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ProfilePage } from '../features/profile/ProfilePage'
import { ChangePasswordPage } from '../features/auth/ChangePasswordPage'
import { AuthGate, RequireAuth } from '../features/auth/RouteGuards'
import { ForbiddenState } from '../features/auth/ForbiddenState'
import { NotFoundPage } from './NotFoundPage'
import { hrUser } from '../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../test/renderWithProviders'
import type { CurrentUser } from '../features/auth/types'

const profile = {
  firstName: 'Ana',
  lastName: 'Demo',
  email: 'ana.demo@example.test',
  phoneNumber: '910000000',
  address: 'Rua Exemplo 1, Lisboa',
  role: 'HR' as const,
}

function LocationProbe() {
  const location = useLocation()
  return <span data-testid="location">{location.pathname}</span>
}

/** The real guard around the real routes, as App.tsx wires them. */
function renderApp(route: string, user: CurrentUser, handlers: Parameters<typeof stubApi>[0] = {}) {
  let me = user
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, me),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    'GET /api/profile': () => json(200, profile),
    ...handlers,
    'POST /api/auth/change-password': (init, url) => {
      const custom = handlers['POST /api/auth/change-password']
      if (custom) return custom(init, url)
      me = { ...me, mustChangePassword: false }
      return new Response(null, { status: 204 })
    },
  })
  renderWithProviders(
    <AuthGate>
      <Routes>
        <Route path="/alterar-password" element={<RequireAuth><ChangePasswordPage /></RequireAuth>} />
        <Route path="/perfil" element={<RequireAuth><ProfilePage /></RequireAuth>} />
        <Route path="/equipamentos" element={<RequireAuth><p>Equipamentos</p></RequireAuth>} />
        <Route path="/" element={<RequireAuth><p>Dashboard</p></RequireAuth>} />
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
      <LocationProbe />
      <Link to="/equipamentos">Ir para Equipamentos</Link>
    </AuthGate>,
    { route },
  )
  return fetchMock
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Meu perfil', () => {
  it('shows the account data, with email and role read-only', async () => {
    renderApp('/perfil', hrUser)
    expect(await screen.findByLabelText(/Nome próprio/)).toHaveValue('Ana')
    expect(screen.getByLabelText('Email')).toBeDisabled()
    expect(screen.getByLabelText('Email')).toHaveValue('ana.demo@example.test')
    expect(screen.getByLabelText('Função')).toHaveValue('RH')
    expect(screen.getByRole('button', { name: 'Alterar palavra-passe' })).toBeInTheDocument()
  })

  it('saves through the existing PUT /api/profile with only the self-editable fields', async () => {
    const fetchMock = renderApp('/perfil', hrUser, {
      'PUT /api/profile': () => json(200, { ...profile, phoneNumber: '912345678' }),
    })
    const phone = await screen.findByLabelText(/Telefone/)
    await userEvent.clear(phone)
    await userEvent.type(phone, '912345678')
    await userEvent.click(screen.getByRole('button', { name: 'Guardar alterações' }))

    expect(await screen.findByText('Alterações guardadas.')).toBeInTheDocument()
    const put = recordedRequests(fetchMock).find((r) => r.method === 'PUT')
    expect(put?.body).toEqual({ firstName: 'Ana', lastName: 'Demo', phone: '912345678', address: 'Rua Exemplo 1, Lisboa' })
  })

  it('shows a save failure (concurrency) as a form error', async () => {
    renderApp('/perfil', hrUser, { 'PUT /api/profile': () => json(409, { title: 'Conflito' }) })
    const phone = await screen.findByLabelText(/Telefone/)
    await userEvent.type(phone, '1')
    await userEvent.click(screen.getByRole('button', { name: 'Guardar alterações' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Os seus dados foram alterados noutra sessão.')
  })
})

describe('Alterar palavra-passe', () => {
  it('validates the existing rules before calling the API', async () => {
    const fetchMock = renderApp('/alterar-password', hrUser)
    await screen.findByRole('heading', { level: 1, name: 'Alterar palavra-passe' })

    await userEvent.type(screen.getByLabelText(/Palavra-passe atual/), 'velha')
    await userEvent.type(screen.getByLabelText(/^Nova palavra-passe/), 'curta')
    await userEvent.type(screen.getByLabelText(/Confirmar nova palavra-passe/), 'outra')
    await userEvent.click(screen.getByRole('button', { name: 'Alterar palavra-passe' }))

    expect(await screen.findByText('A nova palavra-passe deve ter pelo menos 12 caracteres')).toBeInTheDocument()
    expect(screen.getByText('As palavras-passe não coincidem')).toBeInTheDocument()
    expect(recordedRequests(fetchMock).some((r) => r.method === 'POST')).toBe(false)
  })

  it('shows the API’s field error on the field it concerns', async () => {
    renderApp('/alterar-password', hrUser, {
      'POST /api/auth/change-password': () =>
        json(400, { title: 'Erro', errors: { CurrentPassword: ['A palavra-passe atual está incorreta.'] } }),
    })
    await screen.findByRole('heading', { level: 1, name: 'Alterar palavra-passe' })
    await userEvent.type(screen.getByLabelText(/Palavra-passe atual/), 'errada')
    await userEvent.type(screen.getByLabelText(/^Nova palavra-passe/), 'NovaPalavra123')
    await userEvent.type(screen.getByLabelText(/Confirmar nova palavra-passe/), 'NovaPalavra123')
    await userEvent.click(screen.getByRole('button', { name: 'Alterar palavra-passe' }))

    expect(await screen.findByText('A palavra-passe atual está incorreta.')).toBeInTheDocument()
  })

  it('mustChangePassword: every other route is forced to the change-password screen, then released', async () => {
    renderApp('/equipamentos', { ...hrUser, mustChangePassword: true })

    expect(await screen.findByText('Por motivos de segurança, defina uma nova palavra-passe antes de continuar.')).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent('/alterar-password')
    expect(screen.queryByText('Equipamentos')).not.toBeInTheDocument()

    // Still forced: following a link elsewhere is bounced straight back.
    await userEvent.click(screen.getByRole('link', { name: 'Ir para Equipamentos' }))
    expect(screen.getByTestId('location')).toHaveTextContent('/alterar-password')
    expect(screen.queryByText('Equipamentos')).not.toBeInTheDocument()

    await userEvent.type(screen.getByLabelText(/Palavra-passe atual/), 'Temporaria-123')
    await userEvent.type(screen.getByLabelText(/^Nova palavra-passe/), 'NovaPalavra123')
    await userEvent.type(screen.getByLabelText(/Confirmar nova palavra-passe/), 'NovaPalavra123')
    await userEvent.click(screen.getByRole('button', { name: 'Alterar palavra-passe' }))

    // Changing the password refetches the session; the forced screen is then gone…
    await waitFor(
      () => expect(screen.queryByText('Por motivos de segurança, defina uma nova palavra-passe antes de continuar.')).not.toBeInTheDocument(),
      { timeout: 4000 },
    )
    // …and the rest of the application is reachable again.
    await userEvent.click(screen.getByRole('link', { name: 'Ir para Equipamentos' }))
    expect(await screen.findByText('Equipamentos')).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent('/equipamentos')
  })
})

describe('Forbidden and NotFound', () => {
  it('Forbidden explains the lack of access without internals and offers a safe way back', () => {
    stubApi({})
    renderWithProviders(<ForbiddenState />)
    expect(screen.getByRole('heading', { level: 1, name: 'Sem permissão de acesso' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Voltar ao Dashboard' })).toHaveAttribute('href', '/')
    expect(document.body.textContent).not.toMatch(/\.(Read|Create|Update|Archive|Decide)\b/)
  })

  it('NotFound offers a real navigation action', async () => {
    renderApp('/nao-existe', hrUser)
    expect(await screen.findByRole('heading', { level: 1, name: 'Página não encontrada' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Voltar ao Dashboard' })).toHaveAttribute('href', '/')
  })
})
