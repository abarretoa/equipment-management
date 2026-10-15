import { Route, Routes } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { UsersPage } from './UsersPage'
import { RequirePermission } from '../auth/RouteGuards'
import { administrationUser, hrUser } from '../../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import { Permissions } from '../../shared/permissions'
import type { CurrentUser } from '../auth/types'
import type { UserSummary } from './types'

const anaDemo: UserSummary = {
  id: 'u-2',
  firstName: 'Ana',
  lastName: 'Demo',
  email: 'ana.demo@example.test',
  phoneNumber: null,
  address: 'Rua Exemplo 1, Lisboa',
  role: 'HR',
  isActive: true,
  mustChangePassword: true,
  concurrencyStamp: 'stamp-2',
}
const joao: UserSummary = { ...anaDemo, id: 'u-3', firstName: 'João', lastName: 'Test', email: 'joao@example.test', role: 'Administration', isActive: false, mustChangePassword: false, concurrencyStamp: 'stamp-3' }

function renderUsers(user: CurrentUser = administrationUser, handlers: Parameters<typeof stubApi>[0] = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, user),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    'GET /api/users': () => json(200, { items: [anaDemo, joao], page: 1, pageSize: 20, totalCount: 2 }),
    ...handlers,
  })
  renderWithProviders(
    <Routes>
      <Route
        path="/utilizadores"
        element={
          <RequirePermission permission={Permissions.User.Read}>
            <UsersPage />
          </RequirePermission>
        }
      />
    </Routes>,
    { route: '/utilizadores' },
  )
  return fetchMock
}

/** Radix menus open on Enter in jsdom (not on a synthetic click). */
async function openMenu(name: string) {
  const table = await screen.findByRole('table')
  within(table).getByRole('button', { name: `Mais ações: ${name}` }).focus()
  await userEvent.keyboard('{Enter}')
  return screen.findByRole('menu')
}

const mutations = (fetchMock: ReturnType<typeof stubApi>) => recordedRequests(fetchMock).filter((r) => r.method !== 'GET')

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('UsersPage', () => {
  it('lists real accounts with role, state and pending first password change', async () => {
    renderUsers()
    const table = await screen.findByRole('table')

    const anaRow = within(table).getByText('Ana Demo').closest('tr')!
    expect(within(anaRow).getByText('ana.demo@example.test')).toBeInTheDocument()
    expect(within(anaRow).getByText('RH')).toBeInTheDocument()
    expect(within(anaRow).getByText('Ativo')).toBeInTheDocument()
    expect(within(anaRow).getByText('Aguarda 1ª alteração')).toBeInTheDocument()
    const joaoRow = within(table).getByText('João Test').closest('tr')!
    expect(within(joaoRow).getByText('Inativo')).toBeInTheDocument()
  })

  it('is an account list, not a consultant list: no consultant, allocation or custody data or links', async () => {
    renderUsers()
    await screen.findByRole('table')
    expect(screen.getByText(/Não são consultores/)).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /consultor/i })).not.toBeInTheDocument()
    expect(screen.queryByText(/Alocação|Custódia|Equipamento/)).not.toBeInTheDocument()
  })

  it('create validates fields and sends exactly the role value the API defines', async () => {
    const fetchMock = renderUsers(administrationUser, {
      'POST /api/users': () => json(201, { user: anaDemo, temporaryPassword: 'Tmp-Pass-123456' }),
    })
    await screen.findByRole('table')
    await userEvent.click(screen.getAllByRole('button', { name: 'Novo utilizador' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Novo utilizador' })
    expect(within(dialog).getByText(/não um consultor/)).toBeInTheDocument()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Criar utilizador' }))
    expect(await within(dialog).findByText('Indique o e-mail')).toBeInTheDocument()
    expect(mutations(fetchMock)).toHaveLength(0)

    await userEvent.type(within(dialog).getByLabelText(/Nome próprio/), 'Ana')
    await userEvent.type(within(dialog).getByLabelText(/Apelido/), 'Example')
    await userEvent.type(within(dialog).getByLabelText(/E-mail/), 'ana@example.test')
    await userEvent.selectOptions(within(dialog).getByLabelText(/Função/), 'Administração')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Criar utilizador' }))

    await waitFor(() => expect(mutations(fetchMock)).toHaveLength(1))
    expect(mutations(fetchMock)[0]?.body).toEqual({ firstName: 'Ana', lastName: 'Example', email: 'ana@example.test', phone: null, role: 'Administration' })
    expect(await screen.findByRole('dialog', { name: 'Palavra-passe temporária gerada' })).toHaveTextContent('Tmp-Pass-123456')
  })

  it('role change sends the real role id with the concurrency stamp', async () => {
    const fetchMock = renderUsers(administrationUser, { 'PUT /api/users/u-2/role': () => json(200, { ...anaDemo, role: 'Administration' }) })
    const menu = await openMenu('Ana Demo')
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'Alterar função' }))
    const dialog = await screen.findByRole('dialog', { name: 'Alterar função' })
    await userEvent.selectOptions(within(dialog).getByLabelText('Função'), 'Administração')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar alterações' }))

    await waitFor(() => expect(mutations(fetchMock)).toHaveLength(1))
    expect(mutations(fetchMock)[0]?.body).toEqual({ role: 'Administration', concurrencyStamp: 'stamp-2' })
  })

  it('deactivation is confirmed, posts to the existing endpoint and shows a failure inside the dialog', async () => {
    const fetchMock = renderUsers(administrationUser, {
      'POST /api/users/u-2/deactivate': () => json(409, { title: 'Não é possível desativar o último administrador.' }),
    })
    const menu = await openMenu('Ana Demo')
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'Desativar' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Desativar utilizador' })
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toHaveFocus()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Desativar' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Não é possível desativar o último administrador.')
    expect(mutations(fetchMock)[0]?.body).toEqual({ concurrencyStamp: 'stamp-2' })
  })

  it('offers only permitted actions: without archive/role permissions those items are absent', async () => {
    renderUsers({
      ...administrationUser,
      permissions: administrationUser.permissions.filter((p) => p !== Permissions.User.Archive && p !== Permissions.User.AssignRole),
    })
    const menu = await openMenu('Ana Demo')
    expect(within(menu).getAllByRole('menuitem').map((i) => i.textContent)).toEqual(['Editar', 'Repor palavra-passe'])
  })

  it('without any management permission there is no create action and no row menu', async () => {
    renderUsers({ ...administrationUser, permissions: [Permissions.User.Read] })
    await screen.findByRole('table')
    expect(screen.queryByRole('button', { name: 'Novo utilizador' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Mais ações/ })).not.toBeInTheDocument()
  })

  it('RH cannot reach the page at all', async () => {
    const fetchMock = renderUsers(hrUser)
    expect(await screen.findByText('Sem permissão de acesso')).toBeInTheDocument()
    expect(recordedRequests(fetchMock).some((r) => r.url.pathname === '/api/users')).toBe(false)
  })
})
