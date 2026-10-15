import { Link, Route, Routes, useLocation } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { HandoversListPage } from './HandoversListPage'
import { administrationUser } from '../../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import { Permissions } from '../../shared/permissions'
import type { CurrentUser } from '../auth/types'
import type { HandoverSummary } from './types'

const draft: HandoverSummary = {
  id: 'h-draft',
  consultantId: 'c-1',
  consultantFullName: 'Sofia Sample Example',
  status: 'Draft',
  operationalStatus: 'Draft',
  deliveredOn: null,
  itemCount: 2,
  createdAtUtc: '2026-10-08T10:00:00Z',
  finalizedAtUtc: null,
}

const awaiting: HandoverSummary = {
  ...draft,
  id: 'h-awaiting',
  consultantFullName: 'Pedro Test',
  status: 'Finalized',
  operationalStatus: 'AwaitingSignedTerm',
  deliveredOn: '2026-10-07',
  itemCount: 1,
  finalizedAtUtc: '2026-10-07T15:00:00Z',
}

const completed: HandoverSummary = { ...awaiting, id: 'h-done', consultantFullName: 'Teresa Sample', operationalStatus: 'Completed' }

function LocationProbe() {
  const location = useLocation()
  return <span data-testid="location">{location.pathname + location.search}</span>
}

function renderList({ route = '/entregas', user = administrationUser }: { route?: string; user?: CurrentUser } = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, user),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    'GET /api/equipment-handovers': (_init, url) => {
      const status = url.searchParams.get('status')
      const items = [draft, awaiting, completed].filter((h) => !status || h.status === status)
      return json(200, { items, page: 1, pageSize: 20, totalCount: items.length })
    },
  })

  renderWithProviders(
    <>
      <Routes>
        <Route path="/entregas" element={<HandoversListPage />} />
        <Route path="*" element={<p>Outra página</p>} />
      </Routes>
      <Link to="/entregas">Menu Entregas</Link>
      <LocationProbe />
    </>,
    { route },
  )
  return fetchMock
}

const listRequests = (fetchMock: ReturnType<typeof stubApi>) =>
  recordedRequests(fetchMock).filter((r) => r.url.pathname === '/api/equipment-handovers')

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('HandoversListPage', () => {
  it('renders the operational status and links each row to its handover', async () => {
    renderList()

    const table = await screen.findByRole('table')
    expect(within(table).getByText('Rascunho')).toBeInTheDocument()
    expect(within(table).getByText('Aguardando termo assinado')).toBeInTheDocument()
    expect(within(table).getByText('Finalizada')).toBeInTheDocument()

    const link = within(table).getByRole('link', { name: 'Sofia Sample Example' })
    expect(link).toHaveAttribute('href', '/entregas/h-draft')
    await userEvent.click(link)
    expect(screen.getByTestId('location')).toHaveTextContent('/entregas/h-draft')
  })

  it('reads the status from the URL and follows it when the URL changes (no stale filter)', async () => {
    const fetchMock = renderList({ route: '/entregas?status=Draft' })

    expect(await screen.findByRole('link', { name: 'Sofia Sample Example' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Filtrar por estado' })).toHaveValue('Draft')
    expect(listRequests(fetchMock).at(-1)?.url.searchParams.get('status')).toBe('Draft')
    expect(screen.queryByText('Pedro Test')).not.toBeInTheDocument()

    // The sidebar's plain "/entregas" must mean "all", not keep the old filter.
    await userEvent.click(screen.getByRole('link', { name: 'Menu Entregas' }))
    expect(await screen.findByRole('link', { name: 'Pedro Test' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Filtrar por estado' })).toHaveValue('')
    expect(listRequests(fetchMock).at(-1)?.url.searchParams.has('status')).toBe(false)
  })

  it('labels the delivered filter "Entrega efetuada" while still sending the same lifecycle value', async () => {
    const fetchMock = renderList()
    await screen.findByRole('table')

    const filter = screen.getByRole('combobox', { name: 'Filtrar por estado' })
    expect(within(filter).getAllByRole('option').map((o) => [o.textContent, (o as HTMLOptionElement).value])).toEqual([
      ['Todos os estados', ''],
      ['Rascunho', 'Draft'],
      // Lifecycle "Finalized" covers both "Aguardando termo assinado" and "Finalizada" rows.
      ['Entrega efetuada', 'Finalized'],
    ])

    await userEvent.selectOptions(filter, 'Entrega efetuada')
    await waitFor(() => expect(listRequests(fetchMock).at(-1)?.url.searchParams.get('status')).toBe('Finalized'))
  })

  it('writes a status change to the URL and resets the page', async () => {
    const fetchMock = renderList({ route: '/entregas?page=2' })
    await screen.findByRole('table')

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Filtrar por estado' }), 'Finalized')

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/entregas?status=Finalized'))
    const last = listRequests(fetchMock).at(-1)
    expect(last?.url.searchParams.get('status')).toBe('Finalized')
    expect(last?.url.searchParams.get('page')).toBe('1')
  })

  it('drops an unknown status from the URL instead of silently applying it', async () => {
    renderList({ route: '/entregas?status=Entrega' })
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(/^\/entregas$/))
  })

  it('keeps free-text search local — sent to the API, never written to the URL', async () => {
    const fetchMock = renderList({ route: '/entregas?status=Draft' })
    await screen.findByRole('table')

    await userEvent.type(screen.getByRole('textbox', { name: 'Pesquisar' }), 'Sofia')

    await waitFor(() => expect(listRequests(fetchMock).at(-1)?.url.searchParams.get('search')).toBe('Sofia'))
    expect(screen.getByTestId('location')).toHaveTextContent('/entregas?status=Draft')
  })

  it('offers "Nova entrega" only with the handover-create permission', async () => {
    renderList()
    await screen.findByRole('table')
    expect(screen.getAllByRole('button', { name: 'Nova entrega' }).length).toBeGreaterThan(0)
  })

  it('hides "Nova entrega" without the handover-create permission', async () => {
    renderList({
      user: {
        ...administrationUser,
        permissions: administrationUser.permissions.filter((p) => p !== Permissions.EquipmentHandover.Create),
      },
    })
    await screen.findByRole('table')
    expect(screen.queryByRole('button', { name: 'Nova entrega' })).not.toBeInTheDocument()
  })
})
