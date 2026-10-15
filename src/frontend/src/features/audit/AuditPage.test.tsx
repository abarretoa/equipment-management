import { Route, Routes } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AuditPage } from './AuditPage'
import { auditActionLabel, auditActionLabels, auditEntityHref } from './auditPresentation'
import { administrationUser } from '../../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import type { AuditLog } from './types'

const log = (id: string, action: string, overrides: Partial<AuditLog> = {}): AuditLog => ({
  id,
  action,
  entityType: 'Equipment',
  entityId: '3f2c1a9e-0000-4000-8000-000000000001',
  actorDisplayNameSnapshot: 'Eva Example',
  occurredAtUtc: '2026-10-09T10:00:00Z',
  ipAddress: '10.0.0.7',
  correlationId: 'corr-1',
  ...overrides,
})

const logs: AuditLog[] = [
  log('a-1', 'Equipment.Retired', { metadataJson: '{"assetTag":"EQ-NB-0412"}' }),
  log('a-2', 'Some.NewEvent', { entityType: 'Mystery', entityId: 'x-1', actorDisplayNameSnapshot: null }),
  log('a-3', 'EquipmentHandover.Finalized', { entityType: 'EquipmentHandover', entityId: 'h-1' }),
]

function renderAudit() {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, administrationUser),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    'GET /api/audit': () => json(200, { items: logs, page: 1, pageSize: 50, totalCount: logs.length }),
  })
  renderWithProviders(
    <Routes>
      <Route path="/auditoria" element={<AuditPage />} />
      <Route path="*" element={<p>Outra página</p>} />
    </Routes>,
    { route: '/auditoria' },
  )
  return fetchMock
}

const auditRequests = (fetchMock: ReturnType<typeof stubApi>) =>
  recordedRequests(fetchMock).filter((r) => r.url.pathname === '/api/audit')

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('audit action labels', () => {
  it.each([
    ['Equipment.Retired', 'Equipamento abatido'],
    ['EquipmentHandover.Created', 'Entrega criada'],
    ['EquipmentHandover.Finalized', 'Entrega confirmada'],
    ['EquipmentDispatch.NotificationSent', 'Notificação de envio enviada'],
    ['EquipmentReturn.Completed', 'Devolução concluída'],
    ['ReturnInspection.Finalized', 'Inspeção de devolução concluída'],
    ['EquipmentReturnGroup.Created', 'Grupo de devoluções criado'],
    ['EquipmentAssignment.Started', 'Equipamento atribuído'],
    ['EquipmentAssignment.Ended', 'Atribuição terminada'],
    ['EquipmentPhotoSet.Updated', 'Fotografias atuais atualizadas'],
    ['EquipmentPhotoSet.Replaced', 'Fotografias atuais do equipamento alteradas'],
    ['Damage.Recorded', 'Avaria registada'],
    ['Damage.PhotoAdded', 'Fotografia de avaria adicionada'],
  ])('%s → %s', (code, label) => {
    expect(auditActionLabel(code)).toBe(label)
  })

  it('falls back to the raw code for an unmapped event — never blank or invented', () => {
    expect(auditActionLabel('Some.NewEvent')).toBe('Some.NewEvent')
  })

  it('every mapped label is distinct, so no two events read the same', () => {
    const labels = Object.values(auditActionLabels)
    expect(new Set(labels).size).toBe(labels.length)
  })

  it('links an entity only from its structured type + id', () => {
    expect(auditEntityHref('Equipment', 'eq-1')).toBe('/equipamentos/eq-1')
    expect(auditEntityHref('EquipmentHandover', 'h-1')).toBe('/entregas/h-1')
    expect(auditEntityHref('User', 'u-1')).toBeUndefined()
    expect(auditEntityHref('Equipment', null)).toBeUndefined()
  })

  it('labels a deleted Draft and never links to the record it removed', () => {
    expect(auditActionLabel('EquipmentHandover.DraftDeleted')).toBe('Rascunho de entrega eliminado')
    expect(auditEntityHref('EquipmentHandover', 'h-1', 'EquipmentHandover.DraftDeleted')).toBeUndefined()
    expect(auditEntityHref('EquipmentHandover', 'h-1', 'EquipmentHandover.Created')).toBe('/entregas/h-1')
  })
})

describe('AuditPage', () => {
  it('shows human-readable actions in the table, with the raw code as fallback', async () => {
    renderAudit()
    const table = await screen.findByRole('table')

    expect(within(table).getByRole('button', { name: 'Equipamento abatido' })).toBeInTheDocument()
    expect(within(table).getByRole('button', { name: 'Entrega confirmada' })).toBeInTheDocument()
    expect(within(table).getByRole('button', { name: 'Some.NewEvent' })).toBeInTheDocument()
    expect(within(table).queryByText('Equipment.Retired')).not.toBeInTheDocument()
    expect(within(table).getByText('Sistema')).toBeInTheDocument()
    expect(within(table).getByText('Mystery')).toBeInTheDocument()
  })

  it('keeps the wide table in its own labelled scroll region', async () => {
    renderAudit()
    const region = await screen.findByRole('region', { name: 'Registos de auditoria' })
    expect(within(region).getByRole('table')).toHaveStyle({ minWidth: '840px' })
  })

  it('filters send the raw codes the API matches exactly', async () => {
    const fetchMock = renderAudit()
    await screen.findByRole('table')

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Filtrar por ação' }), 'Equipamento abatido')
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Filtrar por entidade' }), 'Entrega')
    await userEvent.type(screen.getByLabelText('Data inicial'), '2026-10-01')

    await waitFor(() => {
      const last = auditRequests(fetchMock).at(-1)
      expect(last?.url.searchParams.get('action')).toBe('Equipment.Retired')
      expect(last?.url.searchParams.get('entityType')).toBe('EquipmentHandover')
      // A calendar day is sent as the UTC instant of its local start (see auditDateRange).
      expect(last?.url.searchParams.get('fromUtc')).toBe(new Date(2026, 9, 1).toISOString())
      expect(last?.url.searchParams.get('page')).toBe('1')
    })
  })

  it('date filters send explicit UTC instants: Data final includes the whole selected day', async () => {
    const fetchMock = renderAudit()
    await screen.findByRole('table')

    await userEvent.type(screen.getByLabelText('Data inicial'), '2026-10-10')
    await userEvent.type(screen.getByLabelText('Data final'), '2026-10-10')

    await waitFor(() => {
      const params = auditRequests(fetchMock).at(-1)?.url.searchParams
      expect(params?.get('fromUtc')).toBe(new Date(2026, 9, 10, 0, 0, 0, 0).toISOString())
      expect(params?.get('toUtc')).toBe(new Date(2026, 9, 10, 23, 59, 59, 999).toISOString())
    })
    // Never a bare date: the API binds that as an unspecified-kind DateTime, which it cannot query.
    const params = auditRequests(fetchMock).at(-1)?.url.searchParams
    expect(params?.get('fromUtc')).toMatch(/Z$/)
    expect(params?.get('toUtc')).toMatch(/Z$/)
  })

  it('opens an accessible detail drawer: label first, technical code kept, structured link only', async () => {
    renderAudit()
    const table = await screen.findByRole('table')
    const opener = within(table).getByRole('button', { name: 'Equipamento abatido' })
    await userEvent.click(opener)

    const drawer = await screen.findByRole('dialog', { name: 'Detalhe do registo de auditoria' })
    expect(within(drawer).getByText('Equipamento abatido')).toBeInTheDocument()
    expect(within(drawer).getByText('Código técnico')).toBeInTheDocument()
    expect(within(drawer).getByText('Equipment.Retired')).toBeInTheDocument()
    expect(within(drawer).getByRole('link', { name: '3f2c1a9e-0000-4000-8000-000000000001' })).toHaveAttribute(
      'href',
      '/equipamentos/3f2c1a9e-0000-4000-8000-000000000001',
    )
    expect(within(drawer).getByText('10.0.0.7')).toBeInTheDocument()
    expect(within(drawer).getByText(/"assetTag": "EQ-NB-0412"/)).toBeInTheDocument()

    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(opener).toHaveFocus()
  })

  it('an unmapped event keeps its raw code as the action and gets no link', async () => {
    renderAudit()
    const table = await screen.findByRole('table')
    await userEvent.click(within(table).getByRole('button', { name: 'Some.NewEvent' }))

    const drawer = await screen.findByRole('dialog', { name: 'Detalhe do registo de auditoria' })
    expect(within(drawer).getByText('Some.NewEvent')).toBeInTheDocument()
    expect(within(drawer).queryByText('Código técnico')).not.toBeInTheDocument()
    expect(within(drawer).queryByRole('link')).not.toBeInTheDocument()
  })

  it('never mutates the received record', async () => {
    const snapshot = structuredClone(logs)
    renderAudit()
    const table = await screen.findByRole('table')
    await userEvent.click(within(table).getByRole('button', { name: 'Equipamento abatido' }))
    await screen.findByRole('dialog')
    expect(logs).toEqual(snapshot)
  })
})
