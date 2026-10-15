import { Route, Routes, useLocation } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { CompaniesPage } from './CompaniesPage'
import { administrationUser, hrUser } from '../../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import type { CurrentUser } from '../auth/types'
import type { Company } from './types'

const companies: Company[] = [
  { id: 'co-1', name: 'Cliente Exemplo, S.A.', isActive: true, createdAtUtc: '2026-09-01T10:00:00Z', updatedAtUtc: '2026-09-01T10:00:00Z' },
  { id: 'co-2', name: 'Antiga Parceira, Lda.', isActive: false, createdAtUtc: '2025-02-01T10:00:00Z', updatedAtUtc: '2026-01-01T10:00:00Z' },
]

function LocationProbe() {
  const location = useLocation()
  return <span data-testid="location">{location.pathname}</span>
}

function renderPage({
  user = administrationUser,
  list = () => json(200, { items: companies, page: 1, pageSize: 20, totalCount: companies.length }),
  handlers = {},
}: { user?: CurrentUser; list?: () => Response; handlers?: Parameters<typeof stubApi>[0] } = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, user),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    'GET /api/companies': list,
    ...handlers,
  })
  renderWithProviders(
    <>
      <Routes>
        <Route path="/empresas" element={<CompaniesPage />} />
      </Routes>
      <LocationProbe />
    </>,
    { route: '/empresas' },
  )
  return fetchMock
}

const listParams = (fetchMock: ReturnType<typeof stubApi>) =>
  recordedRequests(fetchMock)
    .map((request) => request.url)
    .filter((url) => url.pathname === '/api/companies')
    .map((url) => Object.fromEntries(url.searchParams))

/** The desktop table's row menu trigger for a company. */
async function openMenu(name: string) {
  const table = await screen.findByRole('table')
  within(table).getByRole('button', { name: `Mais ações: ${name}` }).focus()
  await userEvent.keyboard('{Enter}')
  return screen.findByRole('menu')
}

describe('CompaniesPage', () => {
  it('lists companies in a flat table with state and a result count', async () => {
    renderPage()
    const table = await screen.findByRole('table')
    expect(within(table).getByText('Cliente Exemplo, S.A.')).toBeInTheDocument()
    expect(within(table).getByText('Ativo')).toBeInTheDocument()
    expect(within(table).getByText('Inativo')).toBeInTheDocument()
    expect(screen.getByText((_, el) => el?.tagName === 'SPAN' && el.textContent === '2 empresas')).toBeInTheDocument()
  })

  it('sends search and Estado as server filters', async () => {
    const fetchMock = renderPage()
    await screen.findByRole('table')
    await userEvent.type(screen.getByRole('textbox', { name: 'Pesquisar' }), 'Cli')
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Filtrar por estado' }), 'true')
    await waitFor(() => expect(listParams(fetchMock).at(-1)).toMatchObject({ search: 'Cli', isActive: 'true', page: '1' }))
  })

  it('opens the row menu from the keyboard with labelled items', async () => {
    renderPage()
    const table = await screen.findByRole('table')
    within(table).getByRole('button', { name: 'Mais ações: Cliente Exemplo, S.A.' }).focus()
    await userEvent.keyboard('{Enter}')
    const menu = await screen.findByRole('menu')
    expect(within(menu).getByRole('menuitem', { name: 'Editar' })).toBeInTheDocument()
    expect(within(menu).getByRole('menuitem', { name: 'Desativar' })).toHaveClass('text-danger')
  })

  it('shows Desativar/Ativar only with Company.Archive (not to HR)', async () => {
    renderPage({ user: hrUser })
    const menu = await openMenu('Cliente Exemplo, S.A.')
    expect(within(menu).getByRole('menuitem', { name: 'Editar' })).toBeInTheDocument()
    expect(within(menu).queryByRole('menuitem', { name: /Desativar|Ativar/ })).not.toBeInTheDocument()
  })

  it('offers Ativar for an inactive company to Administration and calls the activate endpoint', async () => {
    const fetchMock = renderPage({ handlers: { 'POST /api/companies/co-2/activate': () => json(204) } })
    const menu = await openMenu('Antiga Parceira, Lda.')
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'Ativar' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Ativar empresa' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Ativar' }))
    await waitFor(() =>
      expect(recordedRequests(fetchMock).some((r) => r.method === 'POST' && r.url.pathname === '/api/companies/co-2/activate')).toBe(true),
    )
  })

  it('does not navigate when a row-menu action is used', async () => {
    renderPage()
    const menu = await openMenu('Cliente Exemplo, S.A.')
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'Editar' }))
    expect(await screen.findByRole('dialog', { name: 'Editar empresa' })).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/empresas$/)
  })

  it('keeps create validation and payload in the form dialog', async () => {
    const fetchMock = renderPage({ handlers: { 'POST /api/companies': () => json(201, companies[0]) } })
    await screen.findByRole('table')
    await userEvent.click(screen.getByRole('button', { name: 'Nova empresa' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nova empresa' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Criar empresa' }))
    expect(await within(dialog).findByText('Indique o nome')).toBeInTheDocument()
    await userEvent.type(within(dialog).getByLabelText(/Nome/), 'Nova Cliente, S.A.')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Criar empresa' }))
    await waitFor(() =>
      expect(
        recordedRequests(fetchMock).filter((r) => r.method === 'POST').map((r) => r.body),
      ).toEqual([{ name: 'Nova Cliente, S.A.' }]),
    )
  })

  it('prefills the edit dialog and closes it on Cancelar', async () => {
    renderPage()
    const table = await screen.findByRole('table')
    const trigger = within(table).getByRole('button', { name: 'Mais ações: Cliente Exemplo, S.A.' })
    trigger.focus()
    await userEvent.keyboard('{Enter}')
    await userEvent.click(within(await screen.findByRole('menu')).getByRole('menuitem', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar empresa' })
    expect(within(dialog).getByLabelText(/Nome/)).toHaveValue('Cliente Exemplo, S.A.')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancelar' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('hides Nova empresa without Company.Create and disables Editar', async () => {
    renderPage({ user: { ...hrUser, permissions: hrUser.permissions.filter((p) => p !== 'Company.Create') } })
    const menu = await openMenu('Cliente Exemplo, S.A.')
    expect(screen.queryByRole('button', { name: 'Nova empresa' })).not.toBeInTheDocument()
    expect(within(menu).getByRole('menuitem', { name: 'Editar' })).toHaveAttribute('data-disabled')
  })

  it('shows a guided empty state with a real action', async () => {
    renderPage({ list: () => json(200, { items: [], page: 1, pageSize: 20, totalCount: 0 }) })
    expect(await screen.findByText('Nenhuma empresa encontrada')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Nova empresa' })).toHaveLength(2)
  })

  it('shows the error state with retry', async () => {
    renderPage({ list: () => json(500, { title: 'Erro inesperado.' }) })
    expect(await screen.findByRole('button', { name: /Tentar novamente/ })).toBeInTheDocument()
  })

  it('renders mobile admin rows with their own menu and no row link', async () => {
    renderPage()
    await screen.findByRole('table')
    const mobileList = screen.getAllByRole('list').find((ul) => ul.className.includes('md:hidden'))!
    expect(within(mobileList).queryByRole('link')).not.toBeInTheDocument()
    expect(within(mobileList).getAllByRole('button', { name: /Mais ações/ })).toHaveLength(2)
    expect(within(mobileList).getByText('Criada em 01/09/2026')).toBeInTheDocument()
  })
})
