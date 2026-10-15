import { Route, Routes, useLocation } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ReturnsListPage } from './ReturnsListPage'
import { administrationUser } from '../../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import type { EquipmentAssignment } from '../assignments/types'
import type { ReturnGroup, ReturnListRow } from './types'

const groupItem = (n: number, status: 'Pending' | 'Received' = 'Pending') => ({
  returnId: `r-${n}`,
  equipmentId: `eq-${n}`,
  assetTag: `EQ-NB-000${n}`,
  equipmentTypeName: 'Portátil',
  status,
  equipmentStatus: status === 'Pending' ? ('ReturnPending' as const) : ('UnderInspection' as const),
  receivedOn: status === 'Received' ? '2026-10-09' : null,
})

const offboarding: ReturnGroup = {
  id: 'g-1',
  consultantId: 'c-1',
  consultantFullName: 'Sofia Sample Example',
  isEndOfEngagement: true,
  collectionStatus: 'Partial',
  expectedFromDate: '2026-10-10',
  expectedToDate: '2026-10-12',
  receivedCount: 1,
  createdByDisplayNameSnapshot: 'Ana Demo',
  createdAtUtc: '2026-10-08T10:00:00Z',
  items: [groupItem(1, 'Received'), groupItem(2)],
}

const singleGroup: ReturnGroup = {
  ...offboarding,
  id: 'g-2',
  consultantFullName: 'Pedro Test',
  isEndOfEngagement: false,
  collectionStatus: 'Awaiting',
  expectedToDate: null,
  receivedCount: 0,
  items: [{ ...groupItem(7), returnId: 'r-7' }],
}

const rows: ReturnListRow[] = [
  { group: offboarding },
  { group: singleGroup },
  {
    return: {
      id: 'r-legacy',
      equipmentAssignmentId: 'a-9',
      equipmentId: 'eq-9',
      assetTag: 'EQ-TM-0009',
      consultantId: 'c-9',
      consultantFullName: 'Teresa Sample',
      status: 'Completed',
      receivedOn: '2026-09-01',
      createdAtUtc: '2026-08-30T10:00:00Z',
    },
  },
]

function LocationProbe() {
  const location = useLocation()
  return <span data-testid="location">{location.pathname + location.search}</span>
}

function renderList(route = '/devolucoes', extra: Parameters<typeof stubApi>[0] = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, administrationUser),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    'GET /api/equipment-returns/overview': () => json(200, { items: rows, page: 1, pageSize: 20, totalCount: rows.length }),
    ...extra,
  })
  renderWithProviders(
    <>
      <Routes>
        <Route path="/devolucoes" element={<ReturnsListPage />} />
        <Route path="*" element={<p>Outra página</p>} />
      </Routes>
      <LocationProbe />
    </>,
    { route },
  )
  return fetchMock
}

const overviewRequests = (fetchMock: ReturnType<typeof stubApi>) =>
  recordedRequests(fetchMock).filter((r) => r.url.pathname === '/api/equipment-returns/overview')

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ReturnsListPage', () => {
  it('keeps the status filter in the URL', async () => {
    const fetchMock = renderList('/devolucoes?status=Pending')
    await screen.findByRole('table')

    expect(screen.getByRole('combobox', { name: 'Filtrar por estado' })).toHaveValue('Pending')
    expect(overviewRequests(fetchMock).at(-1)?.url.searchParams.get('status')).toBe('Pending')

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Filtrar por estado' }), 'Received')
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('status=Received'))
    expect(overviewRequests(fetchMock).at(-1)?.url.searchParams.get('status')).toBe('Received')
  })

  it('keeps free-text search local', async () => {
    const fetchMock = renderList('/devolucoes?status=Pending')
    await screen.findByRole('table')

    await userEvent.type(screen.getByRole('textbox', { name: 'Pesquisar' }), 'EQ-NB')

    await waitFor(() => expect(overviewRequests(fetchMock).at(-1)?.url.searchParams.get('search')).toBe('EQ-NB'))
    expect(screen.getByTestId('location')).toHaveTextContent('/devolucoes?status=Pending')
  })

  it('a new search resets to page 1, after which pagination through the matches works', async () => {
    // 45 matching rows → 3 pages of 20; the stub always answers with the current page of `rows`.
    const fetchMock = renderList('/devolucoes?status=Pending&page=3', {
      'GET /api/equipment-returns/overview': (_init, url) =>
        json(200, { items: rows, page: Number(url.searchParams.get('page') ?? '1'), pageSize: 20, totalCount: 45 }),
    })
    await screen.findByRole('table')
    expect(overviewRequests(fetchMock).at(-1)?.url.searchParams.get('page')).toBe('3')

    await userEvent.type(screen.getByRole('textbox', { name: 'Pesquisar' }), 'Sofia')
    await waitFor(() => expect(overviewRequests(fetchMock).at(-1)?.url.searchParams.get('search')).toBe('Sofia'))
    expect(overviewRequests(fetchMock).at(-1)?.url.searchParams.get('page')).toBe('1')
    // Search stays local; only the page reset touched the URL, status is kept.
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/devolucoes\?status=Pending$/)

    await userEvent.click(screen.getByRole('button', { name: 'Página seguinte' }))
    await waitFor(() => expect(overviewRequests(fetchMock).at(-1)?.url.searchParams.get('page')).toBe('2'))
    expect(overviewRequests(fetchMock).at(-1)?.url.searchParams.get('search')).toBe('Sofia')
    expect(overviewRequests(fetchMock).at(-1)?.url.searchParams.get('status')).toBe('Pending')

    await userEvent.click(screen.getByRole('button', { name: 'Página anterior' }))
    await waitFor(() => expect(overviewRequests(fetchMock).at(-1)?.url.searchParams.get('page')).toBe('1'))
    expect(overviewRequests(fetchMock).at(-1)?.url.searchParams.get('search')).toBe('Sofia')

    // Changing the search text again resets to page 1.
    await userEvent.click(screen.getByRole('button', { name: 'Página seguinte' }))
    await waitFor(() => expect(overviewRequests(fetchMock).at(-1)?.url.searchParams.get('page')).toBe('2'))
    await userEvent.type(screen.getByRole('textbox', { name: 'Pesquisar' }), ' S')
    await waitFor(() => expect(overviewRequests(fetchMock).at(-1)?.url.searchParams.get('search')).toBe('Sofia S'))
    expect(overviewRequests(fetchMock).at(-1)?.url.searchParams.get('page')).toBe('1')
  })

  it('links each row semantically: a multi-item collection to its group, a single return to itself', async () => {
    renderList()
    const table = await screen.findByRole('table')

    expect(within(table).getByRole('link', { name: 'Sofia Sample Example' })).toHaveAttribute('href', '/devolucoes/grupos/g-1')
    // A group of one is just that return.
    expect(within(table).getByRole('link', { name: 'Pedro Test' })).toHaveAttribute('href', '/devolucoes/r-7')
    expect(within(table).getByRole('link', { name: 'Teresa Sample' })).toHaveAttribute('href', '/devolucoes/r-legacy')
  })

  it('shows an end-of-engagement collection with its own wording and receipt progress', async () => {
    renderList()
    const table = await screen.findByRole('table')

    expect(within(table).getByText('Recolha parcial')).toBeInTheDocument()
    expect(within(table).getByText('1 de 2')).toBeInTheDocument()
    expect(within(table).getByText('Pendente')).toBeInTheDocument()
    expect(within(table).getByText('Concluída')).toBeInTheDocument()
  })

  it('"Nova devolução" starts returns from the consultant\'s active assignments only', async () => {
    const assignment = (id: string, assetTag: string, equipmentStatus: EquipmentAssignment['equipmentStatus']): EquipmentAssignment => ({
      id,
      equipmentId: `eq-${id}`,
      assetTag,
      equipmentTypeName: 'Portátil',
      consultantId: 'c-1',
      consultantFullName: 'Sofia Sample Example',
      status: 'Active',
      origin: 'Handover',
      startedAtUtc: '2026-09-01T10:00:00Z',
      equipmentStatus,
    })
    const fetchMock = renderList('/devolucoes', {
      'GET /api/consultants': () =>
        json(200, { items: [{ id: 'c-1', fullName: 'Sofia Sample Example' }], page: 1, pageSize: 20, totalCount: 1 }),
      'GET /api/equipment-assignments': () =>
        json(200, {
          items: [assignment('a-1', 'EQ-NB-0001', 'Assigned'), assignment('a-2', 'EQ-NB-0002', 'ReturnPending')],
          page: 1,
          pageSize: 100,
          totalCount: 2,
        }),
      'POST /api/equipment-returns/groups': () => json(200, { returnGroupId: 'g-9', returnsCreatedCount: 1, returnIds: ['r-9'] }),
    })
    await screen.findByRole('table')

    await userEvent.click(screen.getAllByRole('button', { name: 'Nova devolução' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Nova devolução' })
    await userEvent.click(within(dialog).getByRole('button', { name: /Consultor/ }))
    await userEvent.click(await screen.findByRole('option', { name: /Sofia Sample Example/ }))

    // An item whose return is already initiated cannot be chosen again.
    expect(await within(dialog).findByRole('checkbox', { name: /EQ-NB-0002/ })).toBeDisabled()
    await userEvent.click(within(dialog).getByRole('checkbox', { name: /EQ-NB-0001/ }))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Iniciar devolução' }))

    await waitFor(() => expect(recordedRequests(fetchMock).some((r) => r.method === 'POST')).toBe(true))
    const assignmentsRequest = recordedRequests(fetchMock).find((r) => r.url.pathname === '/api/equipment-assignments')
    expect(assignmentsRequest?.url.searchParams.get('status')).toBe('Active')
    const start = recordedRequests(fetchMock).find((r) => r.method === 'POST')
    expect(start?.url.pathname).toBe('/api/equipment-returns/groups')
    expect(start?.body).toEqual({ consultantId: 'c-1', equipmentAssignmentIds: ['a-1'] })
  })
})
