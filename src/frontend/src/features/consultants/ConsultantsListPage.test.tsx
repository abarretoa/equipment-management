import { Route, Routes, useLocation } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { ConsultantsListPage } from './ConsultantsListPage'
import { administrationUser, hrUser } from '../../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import { Permissions } from '../../shared/permissions'
import type { CurrentUser } from '../auth/types'
import type { ConsultantSummary } from './types'

const rows: ConsultantSummary[] = [
  {
    id: 'c-1',
    fullName: 'Sofia Sample Example',
    isActive: true,
    createdAtUtc: '2026-10-01T10:00:00Z',
    currentWorkforceType: 'External',
    currentCompanyId: 'co-1',
    currentCompanyName: 'Cliente Exemplo, S.A.',
    currentEngagementType: 'ServiceProvider',
    currentRoleOrFunction: 'Consultora de Sistemas',
  },
  {
    id: 'c-2',
    fullName: 'Miguel Test',
    isActive: false,
    createdAtUtc: '2026-09-01T10:00:00Z',
  },
]

function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname}</output>
}

function renderList({
  user = administrationUser,
  items = rows,
}: { user?: CurrentUser; items?: ConsultantSummary[] } = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, user),
    'GET /api/consultants': () => json(200, { items, page: 1, pageSize: 20, totalCount: items.length }),
  })
  renderWithProviders(
    <>
      <Routes>
        <Route path="/consultores" element={<ConsultantsListPage />} />
        <Route path="/consultores/:id" element={<p>Detalhe do consultor</p>} />
        <Route path="/consultores/novo" element={<p>Novo consultor (página)</p>} />
      </Routes>
      <LocationProbe />
    </>,
    { route: '/consultores' },
  )
  return fetchMock
}

/** Query params of every `/api/consultants` request, oldest first. */
function listRequests(fetchMock: ReturnType<typeof stubApi>) {
  return recordedRequests(fetchMock)
    .map((request) => request.url)
    .filter((url) => url.pathname === '/api/consultants')
    .map((url) => Object.fromEntries(url.searchParams))
}

const table = () => screen.getByRole('table')

describe('ConsultantsListPage', () => {
  it('renders the real columns and rows as a flat table with the result count', async () => {
    renderList()
    const grid = await screen.findByRole('table')
    for (const header of ['Nome', 'Regime', 'Empresa', 'Função', 'Estado', 'Criado em']) {
      expect(within(grid).getByRole('columnheader', { name: header })).toBeInTheDocument()
    }
    expect(within(grid).getByText('Externo')).toBeInTheDocument()
    expect(within(grid).getByText('Cliente Exemplo, S.A.')).toBeInTheDocument()
    expect(within(grid).getByText('Consultora de Sistemas')).toBeInTheDocument()
    expect(screen.getByText((_, el) => el?.tagName === 'SPAN' && el.textContent === '2 consultores')).toBeInTheDocument()
  })

  it('makes the consultant name a real link to /consultores/:id', async () => {
    renderList()
    const link = await within(await screen.findByRole('table')).findByRole('link', { name: 'Sofia Sample Example' })
    expect(link).toHaveAttribute('href', '/consultores/c-1')
    await userEvent.click(link)
    expect(screen.getByTestId('location')).toHaveTextContent('/consultores/c-1')
  })

  it('renders stacked mobile scan rows, each a single link', async () => {
    renderList()
    await screen.findByRole('table')
    const mobileLinks = screen.getAllByRole('link').filter((a) => a.closest('ul'))
    expect(mobileLinks.map((a) => a.getAttribute('href'))).toEqual(['/consultores/c-1', '/consultores/c-2'])
    expect(mobileLinks[0]).toHaveTextContent('Externo · Cliente Exemplo, S.A.')
    expect(mobileLinks[1]).toHaveTextContent('Sem alocação ativa')
  })

  it('shows active and inactive state as icon + text', async () => {
    renderList()
    const grid = await screen.findByRole('table')
    const allRows = within(grid).getAllByRole('row')
    const activeRow = allRows[1]!
    const inactiveRow = allRows[2]!
    expect(within(activeRow).getByText('Ativo')).toBeInTheDocument()
    expect(within(inactiveRow).getByText('Inativo')).toBeInTheDocument()
    expect(within(activeRow).getByText('Ativo').querySelector('svg')).not.toBeNull()
  })

  it('sends search, Estado and Regime as server filters and resets to page 1', async () => {
    const fetchMock = renderList()
    await screen.findByRole('table')
    await userEvent.type(screen.getByRole('textbox', { name: 'Pesquisar' }), 'Mar')
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Filtrar por estado' }), 'false')
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Filtrar por regime' }), 'External')
    await waitFor(() =>
      expect(listRequests(fetchMock).at(-1)).toMatchObject({ search: 'Mar', isActive: 'false', workforceType: 'External', page: '1' }),
    )
  })

  it('does not put filters in the URL', async () => {
    renderList()
    await screen.findByRole('table')
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Filtrar por estado' }), 'true')
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/consultores$/)
  })

  it('offers "Limpar filtros" in the filtered empty state and resets the filters', async () => {
    const fetchMock = renderList({ items: [] })
    await screen.findByText('Nenhum consultor encontrado')
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Filtrar por regime' }), 'Internal')
    await screen.findByText(/Nenhum consultor corresponde aos filtros/)
    const clear = screen.getAllByRole('button', { name: 'Limpar filtros' })
    await userEvent.click(clear.at(-1)!)
    await waitFor(() => expect(listRequests(fetchMock).at(-1)).not.toHaveProperty('workforceType'))
  })

  it('shows Novo consultor only with Consultant.Create', async () => {
    renderList({ user: hrUser })
    await screen.findByRole('table')
    expect(screen.getByRole('button', { name: 'Novo consultor' })).toBeInTheDocument()
  })

  it('hides Novo consultor without Consultant.Create', async () => {
    renderList({
      user: { ...administrationUser, permissions: administrationUser.permissions.filter((p) => p !== Permissions.Consultant.Create) },
    })
    await screen.findByRole('table')
    expect(screen.queryByRole('button', { name: 'Novo consultor' })).not.toBeInTheDocument()
    expect(table()).toBeInTheDocument()
  })
})
