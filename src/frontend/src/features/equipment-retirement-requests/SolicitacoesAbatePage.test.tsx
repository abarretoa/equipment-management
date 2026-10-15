import { Route, Routes } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { SolicitacoesAbatePage } from './SolicitacoesAbatePage'
import { EquipmentDetailPage } from '../equipment/EquipmentDetailPage'
import { RequirePermission } from '../auth/RouteGuards'
import { administrationUser, hrUser } from '../../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import { Permissions } from '../../shared/permissions'
import type { CurrentUser } from '../auth/types'
import type { EquipmentDetail } from '../equipment/types'
import type { EquipmentRetirementRequest } from './types'

const pending: EquipmentRetirementRequest = {
  id: 'rr-1',
  equipmentId: 'eq-1',
  assetTag: 'EQ-NB-0412',
  equipmentTypeName: 'Portátil',
  manufacturer: 'Lenovo',
  model: 'ThinkPad T14',
  equipmentStatus: 'Damaged',
  requestedByDisplayName: 'Ana Demo',
  requestedAtUtc: '2026-10-08T10:00:00Z',
  requestReason: 'Sem reparação possível.',
  status: 'Pending',
}

const rejected: EquipmentRetirementRequest = {
  ...pending,
  id: 'rr-2',
  equipmentId: 'eq-2',
  assetTag: 'EQ-TM-0077',
  equipmentStatus: 'Available',
  status: 'Rejected',
  decidedByDisplayName: 'Eva Example',
  decidedAtUtc: '2026-10-09T10:00:00Z',
  decisionReason: 'Ainda tem utilização.',
}

/** Rendered behind the same guard as the real "/solicitacoes-abate" route. */
function renderPage(user: CurrentUser = administrationUser, handlers: Parameters<typeof stubApi>[0] = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, user),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    'GET /api/equipment-retirement-requests': (_init, url) => {
      const status = url.searchParams.get('status')
      const items = [pending, rejected].filter((r) => !status || r.status === status)
      return json(200, { items, page: 1, pageSize: 20, totalCount: items.length })
    },
    ...handlers,
  })
  renderWithProviders(
    <Routes>
      <Route
        path="/solicitacoes-abate"
        element={
          <RequirePermission permission={Permissions.EquipmentRetirementRequest.Read}>
            <SolicitacoesAbatePage />
          </RequirePermission>
        }
      />
    </Routes>,
    { route: '/solicitacoes-abate' },
  )
  return fetchMock
}

const mutations = (fetchMock: ReturnType<typeof stubApi>) => recordedRequests(fetchMock).filter((r) => r.method !== 'GET')

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('SolicitacoesAbatePage', () => {
  it('lists pending requests by default, with requester, reason and both states', async () => {
    renderPage()
    const table = await screen.findByRole('table')

    expect(within(table).getByRole('link', { name: 'EQ-NB-0412' })).toHaveAttribute('href', '/equipamentos/eq-1')
    expect(within(table).getByText('Pendente')).toBeInTheDocument()
    expect(within(table).getByText('Avariado')).toBeInTheDocument()
    expect(within(table).getByText('Ana Demo')).toBeInTheDocument()
    expect(within(table).getByText('Sem reparação possível.')).toBeInTheDocument()
    expect(within(table).queryByText('EQ-TM-0077')).not.toBeInTheDocument()
  })

  it('shows the decision of decided requests', async () => {
    renderPage()
    await screen.findByRole('table')
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Filtrar por estado' }), 'Rejected')

    const table = await screen.findByRole('table')
    await within(table).findByText('Rejeitado')
    expect(within(table).getByText('Eva Example')).toBeInTheDocument()
    expect(within(table).getByText('Ainda tem utilização.')).toBeInTheDocument()
    expect(within(table).queryByRole('button', { name: 'Abater equipamento' })).not.toBeInTheDocument()
  })

  it('approval runs the existing approve endpoint only (which performs the retirement)', async () => {
    const fetchMock = renderPage(administrationUser, {
      'POST /api/equipment-retirement-requests/rr-1/approve': () => json(200, { ...pending, status: 'Approved' }),
    })
    const table = await screen.findByRole('table')

    await userEvent.click(within(table).getByRole('button', { name: 'Abater equipamento' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Abater equipamento' })
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toHaveFocus()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Abater' }))

    await waitFor(() =>
      expect(mutations(fetchMock).map((r) => `${r.method} ${r.url.pathname}`)).toEqual([
        'POST /api/equipment-retirement-requests/rr-1/approve',
      ]),
    )
  })

  it('shows a failed approval in the dialog error slot', async () => {
    renderPage(administrationUser, {
      'POST /api/equipment-retirement-requests/rr-1/approve': () => json(409, { title: 'O equipamento já não é elegível para abate.' }),
    })
    const table = await screen.findByRole('table')
    await userEvent.click(within(table).getByRole('button', { name: 'Abater equipamento' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Abater equipamento' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Abater' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('O equipamento já não é elegível para abate.')
  })

  it('rejection requires a reason and posts it', async () => {
    const fetchMock = renderPage(administrationUser, {
      'POST /api/equipment-retirement-requests/rr-1/reject': () => json(200, { ...pending, status: 'Rejected' }),
    })
    const table = await screen.findByRole('table')

    await userEvent.click(within(table).getByRole('button', { name: 'Rejeitar solicitação' }))
    const dialog = await screen.findByRole('dialog', { name: 'Rejeitar solicitação' })
    expect(within(dialog).getByRole('button', { name: 'Rejeitar' })).toBeDisabled()
    await userEvent.type(within(dialog).getByLabelText(/Motivo da rejeição/), 'Ainda reparável.')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Rejeitar' }))

    await waitFor(() => expect(mutations(fetchMock)).toHaveLength(1))
    expect(mutations(fetchMock)[0]?.url.pathname).toBe('/api/equipment-retirement-requests/rr-1/reject')
    expect(mutations(fetchMock)[0]?.body).toEqual({ reason: 'Ainda reparável.' })
  })

  it('RH (no review permission) reaches neither the list nor the approve/reject actions', async () => {
    const fetchMock = renderPage(hrUser)
    expect(await screen.findByText('Sem permissão de acesso')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Solicitações de abate' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Abater equipamento' })).not.toBeInTheDocument()
    expect(recordedRequests(fetchMock).some((r) => r.url.pathname === '/api/equipment-retirement-requests')).toBe(false)
  })
})

describe('Requesting retirement (RH, from Equipment detail)', () => {
  const available = {
    id: 'eq-1',
    equipmentTypeId: 't-1',
    equipmentTypeName: 'Monitor',
    assetTag: 'EQ-MN-0010',
    status: 'Available',
    createdAtUtc: '2024-03-12T10:00:00Z',
    updatedAtUtc: '2026-10-08T10:00:00Z',
  } as EquipmentDetail

  function renderEquipment(equipment: EquipmentDetail) {
    const fetchMock = stubApi({
      'GET /api/auth/me': () => json(200, hrUser),
      'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
      'GET /api/equipment/eq-1': () => json(200, equipment),
      'GET /api/equipment/eq-1/history': () => json(200, []),
      'POST /api/equipment-retirement-requests': () => json(201, pending),
    })
    renderWithProviders(
      <Routes>
        <Route path="/equipamentos/:id" element={<EquipmentDetailPage />} />
      </Routes>,
      { route: '/equipamentos/eq-1' },
    )
    return fetchMock
  }

  it('RH requests retirement with a mandatory reason and can never retire directly', async () => {
    const fetchMock = renderEquipment(available)
    await userEvent.click(await screen.findByRole('button', { name: 'Solicitar abate' }))
    expect(screen.queryByRole('button', { name: 'Abater equipamento' })).not.toBeInTheDocument()

    const dialog = await screen.findByRole('dialog', { name: /Solicitar abate/ })
    const submit = within(dialog).getAllByRole('button').at(-1)!
    expect(submit).toBeDisabled()
    await userEvent.type(within(dialog).getByLabelText(/Motivo da solicitação/), 'Ecrã danificado.')
    await userEvent.click(submit)

    await waitFor(() => expect(mutations(fetchMock)).toHaveLength(1))
    expect(mutations(fetchMock)[0]?.url.pathname).toBe('/api/equipment-retirement-requests')
    expect(mutations(fetchMock)[0]?.body).toEqual({ equipmentId: 'eq-1', reason: 'Ecrã danificado.' })
    expect(recordedRequests(fetchMock).some((r) => r.url.pathname.endsWith('/retire'))).toBe(false)
  })

  it('a pending request stays visible on the equipment and blocks a second one', async () => {
    renderEquipment({
      ...available,
      pendingRetirementRequest: {
        id: 'rr-1',
        requestedByDisplayName: 'Ana Demo',
        requestedAtUtc: '2026-10-08T10:00:00Z',
        requestReason: 'Sem reparação possível.',
      },
    })

    expect(await screen.findByRole('heading', { name: 'Abate solicitado' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Solicitar abate' })).not.toBeInTheDocument()
  })
})
