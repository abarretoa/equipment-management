import { Route, Routes } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { MaintenancePage } from './MaintenancePage'
import { EquipmentDetailPage } from '../equipment/EquipmentDetailPage'
import { administrationUser } from '../../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import { Permissions } from '../../shared/permissions'
import type { CurrentUser } from '../auth/types'
import type { EquipmentDetail } from '../equipment/types'
import type { MaintenanceDetail, MaintenanceSummary } from './types'

const active: MaintenanceSummary = {
  id: 'm-1',
  equipmentId: 'eq-1',
  assetTag: 'EQ-NB-0412',
  equipmentTypeName: 'Portátil',
  manufacturer: 'Lenovo',
  model: 'ThinkPad T14',
  status: 'Active',
  sentAtUtc: '2026-10-08T10:00:00Z',
}

const completed: MaintenanceSummary = {
  ...active,
  id: 'm-2',
  equipmentId: 'eq-2',
  assetTag: 'EQ-TM-0077',
  status: 'Completed',
  completedAtUtc: '2026-10-09T10:00:00Z',
}

const activeDetail: MaintenanceDetail = {
  id: 'm-1',
  equipmentId: 'eq-1',
  assetTag: 'EQ-NB-0412',
  equipmentTypeName: 'Portátil',
  status: 'Active',
  notes: 'Ecrã partido',
  sentAtUtc: '2026-10-08T10:00:00Z',
  createdAtUtc: '2026-10-08T10:00:00Z',
  updatedAtUtc: '2026-10-08T10:00:00Z',
}

function renderList({ user = administrationUser, handlers = {} }: { user?: CurrentUser; handlers?: Parameters<typeof stubApi>[0] } = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, user),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    'GET /api/maintenance': () => json(200, { items: [active, completed], page: 1, pageSize: 20, totalCount: 2 }),
    'GET /api/maintenance/m-1': () => json(200, activeDetail),
    ...handlers,
  })
  renderWithProviders(
    <Routes>
      <Route path="/manutencao" element={<MaintenancePage />} />
    </Routes>,
    { route: '/manutencao' },
  )
  return fetchMock
}

async function openActive() {
  const table = await screen.findByRole('table')
  await userEvent.click(within(table).getByRole('button', { name: 'Abrir manutenção de EQ-NB-0412' }))
  return screen.findByRole('dialog', { name: 'Manutenção: EQ-NB-0412' })
}

const completions = (fetchMock: ReturnType<typeof stubApi>) =>
  recordedRequests(fetchMock).filter((r) => r.method === 'POST' && r.url.pathname === '/api/maintenance/m-1/complete')

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('MaintenancePage', () => {
  it('lists the real maintenance states, each record opening from a real button', async () => {
    renderList()
    const table = await screen.findByRole('table')

    expect(within(table).getByText('Em curso')).toBeInTheDocument()
    expect(within(table).getByText('Concluída')).toBeInTheDocument()
    const dialog = await openActive()
    expect(within(dialog).getByRole('link', { name: 'EQ-NB-0412' })).toHaveAttribute('href', '/equipamentos/eq-1')
    expect(within(dialog).getByText('Ecrã partido')).toBeInTheDocument()
  })

  it.each([
    ['Repaired', 'Reparado'],
    ['NotRepairable', 'Sem reparação possível'],
  ])('completes with outcome %s through the existing endpoint', async (outcome, label) => {
    const fetchMock = renderList({
      handlers: { 'POST /api/maintenance/m-1/complete': () => json(200, { ...activeDetail, status: 'Completed', outcome }) },
    })
    const dialog = await openActive()

    await userEvent.selectOptions(within(dialog).getByLabelText(/Resultado/), label)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Concluir manutenção' }))

    await waitFor(() => expect(completions(fetchMock)).toHaveLength(1))
    expect(completions(fetchMock)[0]?.body).toEqual({ outcome, notes: '' })
  })

  it('shows a failed completion inside the drawer', async () => {
    renderList({
      handlers: { 'POST /api/maintenance/m-1/complete': () => json(409, { title: 'A manutenção já foi concluída.' }) },
    })
    const dialog = await openActive()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Concluir manutenção' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('A manutenção já foi concluída.')
  })

  it('offers no completion without the maintenance-manage permission', async () => {
    renderList({
      user: {
        ...administrationUser,
        permissions: administrationUser.permissions.filter((p) => p !== Permissions.EquipmentMaintenance.Manage),
      },
    })
    const dialog = await openActive()
    expect(within(dialog).queryByRole('button', { name: 'Concluir manutenção' })).not.toBeInTheDocument()
  })
})

describe('Starting maintenance (Equipment detail → existing workflow)', () => {
  const damaged = {
    id: 'eq-1',
    equipmentTypeId: 't-1',
    equipmentTypeName: 'Monitor',
    assetTag: 'EQ-MN-0010',
    status: 'Damaged',
    createdAtUtc: '2024-03-12T10:00:00Z',
    updatedAtUtc: '2026-10-08T10:00:00Z',
  } as EquipmentDetail

  function renderEquipment(startResponse: () => Response) {
    const fetchMock = stubApi({
      'GET /api/auth/me': () => json(200, administrationUser),
      'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
      'GET /api/equipment/eq-1': () => json(200, damaged),
      'GET /api/equipment/eq-1/history': () => json(200, []),
      'POST /api/equipment/eq-1/maintenance': startResponse,
    })
    renderWithProviders(
      <Routes>
        <Route path="/equipamentos/:id" element={<EquipmentDetailPage />} />
      </Routes>,
      { route: '/equipamentos/eq-1' },
    )
    return fetchMock
  }

  it('Damaged equipment starts maintenance through the existing mutation — no direct status change', async () => {
    const fetchMock = renderEquipment(() => json(200, activeDetail))
    await userEvent.click(await screen.findByRole('button', { name: 'Iniciar manutenção' }))
    const dialog = await screen.findByRole('dialog', { name: 'Iniciar manutenção' })
    await userEvent.type(within(dialog).getByLabelText(/Observações/), 'Substituir ecrã')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Iniciar' }))

    await waitFor(() => expect(recordedRequests(fetchMock).filter((r) => r.method !== 'GET')).toHaveLength(1))
    const [start] = recordedRequests(fetchMock).filter((r) => r.method !== 'GET')
    expect(start?.url.pathname).toBe('/api/equipment/eq-1/maintenance')
    expect(start?.body).toEqual({ notes: 'Substituir ecrã' })
  })

  it('shows a failed start inside the dialog', async () => {
    renderEquipment(() => json(409, { title: 'O equipamento já não está avariado.' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Iniciar manutenção' }))
    const dialog = await screen.findByRole('dialog', { name: 'Iniciar manutenção' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Iniciar' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('O equipamento já não está avariado.')
    expect(screen.getByRole('dialog', { name: 'Iniciar manutenção' })).toBeInTheDocument()
  })
})
