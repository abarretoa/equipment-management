import { Route, Routes, useLocation } from 'react-router'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { ConsultantDetailPage } from './ConsultantDetailPage'
import { administrationUser, hrUser } from '../../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import { Permissions } from '../../shared/permissions'
import type { CurrentUser } from '../auth/types'
import type { ConsultantAllocation, ConsultantDetail, EndAllocationResult } from './types'
import type { EquipmentAssignment } from '../assignments/types'

const ID = 'c-1'

const activeAllocation: ConsultantAllocation = {
  id: 'al-2',
  consultantId: ID,
  companyId: 'co-1',
  companyName: 'Cliente Exemplo, S.A.',
  workforceType: 'External',
  engagementType: 'ServiceProvider',
  roleOrFunction: 'Consultora de Sistemas',
  startDate: '2026-10-01',
  endDate: null,
  status: 'Active',
  isEndOfEngagement: false,
  createdAtUtc: '2026-10-01T10:00:00Z',
  updatedAtUtc: '2026-10-01T10:00:00Z',
}

const endedAllocation: ConsultantAllocation = {
  ...activeAllocation,
  id: 'al-1',
  companyId: null,
  companyName: null,
  workforceType: 'Internal',
  engagementType: 'EmploymentContract',
  roleOrFunction: 'Analista Júnior',
  startDate: '2025-01-06',
  endDate: '2026-09-30',
  status: 'Ended',
}

const assignment = (n: number, equipmentStatus: EquipmentAssignment['equipmentStatus']): EquipmentAssignment => ({
  id: `as-${n}`,
  equipmentId: `eq-${n}`,
  assetTag: `EQ-NB-000${n}`,
  equipmentTypeName: 'Portátil',
  manufacturer: 'Lenovo',
  model: `ThinkPad T1${n}`,
  consultantId: ID,
  consultantFullName: 'Sofia Sample Example',
  status: 'Active',
  equipmentHandoverId: 'h-1',
  origin: 'Handover',
  startedAtUtc: '2026-10-08T10:00:00Z',
  equipmentStatus,
})

const consultant: ConsultantDetail = {
  id: ID,
  fullName: 'Sofia Sample Example',
  nif: '123456789',
  email: 'sofia.sample@example.test',
  address: 'Rua Exemplo 12, 1000-001 Lisboa',
  phone: '+351 910 000 000',
  isActive: true,
  createdAtUtc: '2026-10-01T10:00:00Z',
  updatedAtUtc: '2026-10-01T10:00:00Z',
  currentAllocation: activeAllocation,
  allocationHistory: [activeAllocation, endedAllocation],
  currentAssignedEquipment: [assignment(1, 'Assigned'), assignment(2, 'ReturnPending')],
}

const unallocated: ConsultantDetail = {
  ...consultant,
  currentAllocation: null,
  allocationHistory: [endedAllocation],
  currentAssignedEquipment: [],
}

const without = (user: CurrentUser, ...removed: string[]): CurrentUser => ({
  ...user,
  permissions: user.permissions.filter((p) => !removed.includes(p)),
})

function LocationProbe() {
  const location = useLocation()
  return <span data-testid="location">{location.pathname + location.search}</span>
}

type Handlers = Parameters<typeof stubApi>[0]

function renderDetail(detail: ConsultantDetail, { user = administrationUser, handlers = {} }: { user?: CurrentUser; handlers?: Handlers } = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, user),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    [`GET /api/consultants/${ID}`]: () => json(200, detail),
    'GET /api/companies': () =>
      json(200, {
        items: [{ id: 'co-1', name: 'Cliente Exemplo, S.A.', isActive: true, createdAtUtc: '', updatedAtUtc: '' }],
        page: 1,
        pageSize: 100,
        totalCount: 1,
      }),
    ...handlers,
  })
  renderWithProviders(
    <>
      <Routes>
        <Route path="/consultores/:id" element={<ConsultantDetailPage />} />
        <Route path="*" element={<p>Outra página</p>} />
      </Routes>
      <LocationProbe />
    </>,
    { route: `/consultores/${ID}` },
  )
  return fetchMock
}

const findTitle = () => screen.findByRole('heading', { level: 1, name: 'Sofia Sample Example' })
const section = (name: string) => screen.getByRole('heading', { level: 2, name }).closest('section')!
const button = (name: string) => screen.queryByRole('button', { name })

function requestBodies(fetchMock: ReturnType<typeof stubApi>, method: string, path: string) {
  return recordedRequests(fetchMock)
    .filter((request) => request.method === method && request.url.pathname.includes(path))
    .map((request) => request.body)
}

describe('ConsultantDetailPage — content', () => {
  it('renders the person data, current allocation, assigned equipment and allocation history', async () => {
    renderDetail(consultant)
    await findTitle()

    const data = section('Dados do consultor')
    for (const text of ['123456789', 'sofia.sample@example.test', '+351 910 000 000', 'Rua Exemplo 12, 1000-001 Lisboa']) {
      expect(within(data).getByText(text)).toBeInTheDocument()
    }

    const allocation = section('Alocação atual')
    const expected: [string, string][] = [
      ['Regime', 'Externo'],
      ['Empresa', 'Cliente Exemplo, S.A.'],
      ['Vínculo', 'Prestador de serviço'],
      ['Função', 'Consultora de Sistemas'],
      ['Data de início', '01/10/2026'],
    ]
    for (const [label, value] of expected) {
      expect(within(allocation).getByText(label).nextElementSibling).toHaveTextContent(value)
    }

    const equipment = section('Equipamentos atribuídos')
    expect(within(equipment).getByText('2 equipamentos')).toBeInTheDocument()
    const equipmentTable = within(equipment).getByRole('table')
    expect(within(equipmentTable).getByText('Atribuído')).toBeInTheDocument()
    expect(within(equipmentTable).getByText('Devolução pendente')).toBeInTheDocument()
    // User-facing terminology: the asset tag is "Identificador" everywhere.
    expect(within(equipmentTable).getByRole('columnheader', { name: 'Identificador' })).toBeInTheDocument()
    expect(within(equipment).queryByText('Asset Tag')).not.toBeInTheDocument()

    const history = within(section('Histórico de alocações')).getByRole('table')
    for (const header of ['Empresa', 'Função', 'Regime', 'Vínculo', 'Início', 'Fim', 'Estado']) {
      expect(within(history).getByRole('columnheader', { name: header })).toBeInTheDocument()
    }
    expect(within(history).getByText('Analista Júnior')).toBeInTheDocument()
    expect(within(history).getByText('Terminada')).toBeInTheDocument()
    expect(within(history).getByText('Contrato de trabalho')).toBeInTheDocument()
  })

  it('shows the active/inactive state beside the name', async () => {
    renderDetail({ ...consultant, isActive: false })
    const title = await findTitle()
    expect(title.parentElement).toHaveTextContent('Inativo')
  })

  it('does not expose any User / login / role concept', async () => {
    renderDetail(consultant)
    await findTitle()
    const text = document.body.textContent ?? ''
    expect(text).not.toMatch(/utilizador|login|palavra-passe|password|permiss|conta de acesso|iniciar sessão|perfil de acesso/i)
    expect(screen.queryByText(/^(Administração|RH)$/)).not.toBeInTheDocument()
  })

  it('shows guidance when there is no current allocation and no equipment', async () => {
    renderDetail(unallocated)
    await findTitle()
    expect(within(section('Alocação atual')).getByText('Este consultor não tem nenhuma alocação ativa.')).toBeInTheDocument()
    expect(within(section('Equipamentos atribuídos')).getByText('Sem equipamentos atribuídos atualmente.')).toBeInTheDocument()
  })

  it('renders equipment as real links that leave the consultant page independently', async () => {
    renderDetail(consultant)
    await findTitle()
    const table = within(section('Equipamentos atribuídos')).getByRole('table')
    const link = within(table).getByRole('link', { name: 'EQ-NB-0001' })
    expect(link).toHaveAttribute('href', '/equipamentos/eq-1')
    expect(screen.getByRole('link', { name: 'Consultores' })).toHaveAttribute('href', '/consultores')
    await userEvent.click(link)
    expect(screen.getByTestId('location')).toHaveTextContent('/equipamentos/eq-1')
  })
})

describe('ConsultantDetailPage — action matrix', () => {
  it('shows Editar and a danger-outline Desativar for an active consultant', async () => {
    renderDetail(consultant)
    await findTitle()
    expect(button('Editar')).toBeInTheDocument()
    expect(button('Desativar')).toHaveClass('text-danger')
    expect(button('Ativar')).not.toBeInTheDocument()
  })

  it('shows Ativar for an inactive consultant and calls the activate endpoint', async () => {
    const fetchMock = renderDetail(
      { ...consultant, isActive: false },
      { handlers: { [`POST /api/consultants/${ID}/activate`]: () => json(204) } },
    )
    await findTitle()
    expect(button('Desativar')).not.toBeInTheDocument()
    await userEvent.click(button('Ativar')!)
    const dialog = await screen.findByRole('alertdialog', { name: 'Ativar consultor' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Ativar' }))
    await waitFor(() => expect(requestBodies(fetchMock, 'POST', `/api/consultants/${ID}/activate`)).toHaveLength(1))
  })

  it('hides Editar and Ativar/Desativar without Consultant.Update / Consultant.Archive', async () => {
    renderDetail(consultant, { user: without(hrUser, Permissions.Consultant.Update, Permissions.Consultant.Archive) })
    await findTitle()
    expect(button('Editar')).not.toBeInTheDocument()
    expect(button('Desativar')).not.toBeInTheDocument()
  })

  it('shows Nova alocação only for an active consultant without allocation and with the permission', async () => {
    renderDetail(unallocated, { user: hrUser })
    await findTitle()
    expect(button('Nova alocação')).toBeInTheDocument()
    expect(button('Encerrar alocação')).not.toBeInTheDocument()
  })

  it('hides Nova alocação without ConsultantAllocation.Create', async () => {
    renderDetail(unallocated, { user: without(hrUser, Permissions.ConsultantAllocation.Create) })
    await findTitle()
    expect(button('Nova alocação')).not.toBeInTheDocument()
  })

  it('hides Nova alocação for an inactive consultant', async () => {
    renderDetail({ ...unallocated, isActive: false })
    await findTitle()
    expect(button('Nova alocação')).not.toBeInTheDocument()
  })

  it('shows Encerrar alocação for an active allocation, and hides it without ConsultantAllocation.End', async () => {
    renderDetail(consultant, { user: without(administrationUser, Permissions.ConsultantAllocation.End) })
    await findTitle()
    expect(button('Encerrar alocação')).not.toBeInTheDocument()
    expect(button('Nova alocação')).not.toBeInTheDocument()
  })

  it('is permission-driven, not role-driven: HR with every consultant permission sees the full matrix', async () => {
    renderDetail(consultant, { user: hrUser })
    await findTitle()
    expect(button('Editar')).toBeInTheDocument()
    expect(button('Desativar')).toBeInTheDocument()
    expect(button('Encerrar alocação')).toBeInTheDocument()
  })
})

describe('ConsultantDetailPage — Nova alocação', () => {
  it('keeps the dialog validation, including the company rule for an external allocation', async () => {
    const fetchMock = renderDetail(unallocated)
    await findTitle()
    await userEvent.click(button('Nova alocação')!)
    const dialog = await screen.findByRole('dialog', { name: 'Nova alocação' })
    await userEvent.selectOptions(within(dialog).getByLabelText(/Regime/), 'External')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Criar alocação' }))
    expect(await within(dialog).findByText('Indique a empresa para um vínculo externo')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique a função')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique a data de início')).toBeInTheDocument()
    expect(requestBodies(fetchMock, 'POST', `/api/consultants/${ID}/allocations`)).toHaveLength(0)
  })

  it('posts the same allocation payload as before', async () => {
    const fetchMock = renderDetail(unallocated, {
      handlers: { [`POST /api/consultants/${ID}/allocations`]: () => json(201, activeAllocation) },
    })
    await findTitle()
    await userEvent.click(button('Nova alocação')!)
    const dialog = await screen.findByRole('dialog', { name: 'Nova alocação' })
    await userEvent.type(within(dialog).getByLabelText(/Função/), 'Analista')
    fireEvent.change(within(dialog).getByLabelText(/Data de início/), { target: { value: '2026-10-12' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Criar alocação' }))
    await waitFor(() =>
      expect(requestBodies(fetchMock, 'POST', `/api/consultants/${ID}/allocations`)).toEqual([
        { workforceType: 'Internal', engagementType: 'ServiceProvider', roleOrFunction: 'Analista', startDate: '2026-10-12' },
      ]),
    )
  })
})

describe('ConsultantDetailPage — Encerrar alocação / vínculo', () => {
  const endResult = (overrides: Partial<EndAllocationResult>): EndAllocationResult => ({
    ...activeAllocation,
    status: 'Ended',
    endDate: '2026-10-31',
    isEndOfEngagement: true,
    returnsCreatedCount: 1,
    returnsAlreadyPendingCount: 1,
    returnGroupId: null,
    ...overrides,
  })

  async function openEndDialog() {
    await findTitle()
    await userEvent.click(button('Encerrar alocação')!)
    return screen.findByRole('dialog', { name: 'Encerrar alocação' })
  }

  it('keeps a plain allocation ending (not end of engagement) without collection fields', async () => {
    const fetchMock = renderDetail(consultant, {
      handlers: { [`POST /api/consultants/${ID}/allocations/al-2/end`]: () => json(200, endResult({ isEndOfEngagement: false, returnsCreatedCount: 0, returnsAlreadyPendingCount: 0 })) },
    })
    const dialog = await openEndDialog()
    expect(within(dialog).queryByLabelText(/Previsão de recolha/)).not.toBeInTheDocument()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Encerrar alocação' }))
    expect(await within(dialog).findByText('Indique a data de fim')).toBeInTheDocument()
    fireEvent.change(within(dialog).getByLabelText(/Data de fim/), { target: { value: '2026-10-31' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Encerrar alocação' }))
    await waitFor(() =>
      expect(requestBodies(fetchMock, 'POST', '/allocations/al-2/end')).toEqual([{ endDate: '2026-10-31', isEndOfEngagement: false }]),
    )
    expect(await screen.findByRole('status')).toHaveTextContent('Alocação encerrada.')
    expect(screen.queryByRole('link', { name: 'Ver devoluções' })).not.toBeInTheDocument()
  })

  it('keeps the full offboarding flow: equipment preview, collection window and bulk-return payload', async () => {
    const fetchMock = renderDetail(consultant, {
      handlers: { [`POST /api/consultants/${ID}/allocations/al-2/end`]: () => json(200, endResult({})) },
    })
    const dialog = await openEndDialog()
    await userEvent.selectOptions(within(dialog).getByLabelText(/fim do vínculo/), 'yes')

    expect(within(dialog).getByRole('heading', { name: 'Equipamentos atualmente atribuídos' })).toBeInTheDocument()
    expect(within(dialog).getByText('O consultor possui 2 equipamentos que deverão ser devolvidos:')).toBeInTheDocument()
    expect(within(dialog).getByText('Devolução será iniciada')).toBeInTheDocument()
    expect(within(dialog).getByText('Devolução já iniciada')).toBeInTheDocument()

    const confirm = within(dialog).getByRole('button', { name: 'Encerrar vínculo e iniciar devoluções' })
    fireEvent.change(within(dialog).getByLabelText(/Data de fim/), { target: { value: '2026-10-31' } })
    await userEvent.click(confirm)
    expect(await within(dialog).findByText('Indique a previsão de recolha')).toBeInTheDocument()
    expect(requestBodies(fetchMock, 'POST', '/allocations/al-2/end')).toHaveLength(0)

    fireEvent.change(within(dialog).getByLabelText(/Previsão de recolha/), { target: { value: '2026-11-02' } })
    fireEvent.change(within(dialog).getByLabelText(/^Até/), { target: { value: '2026-11-06' } })
    await userEvent.click(confirm)
    await waitFor(() =>
      expect(requestBodies(fetchMock, 'POST', '/allocations/al-2/end')).toEqual([
        { endDate: '2026-10-31', isEndOfEngagement: true, expectedCollectionFromDate: '2026-11-02', expectedCollectionToDate: '2026-11-06' },
      ]),
    )
    // The workflow goes through Returns only: no assignment/equipment mutation is sent.
    expect(recordedRequests(fetchMock).some((r) => r.method === 'POST' && /assignments|\/api\/equipment\//.test(r.url.pathname))).toBe(false)
  })

  it('shows the inline success banner with "Ver devoluções" to pending returns', async () => {
    renderDetail(consultant, {
      handlers: { [`POST /api/consultants/${ID}/allocations/al-2/end`]: () => json(200, endResult({})) },
    })
    const dialog = await openEndDialog()
    await userEvent.selectOptions(within(dialog).getByLabelText(/fim do vínculo/), 'yes')
    fireEvent.change(within(dialog).getByLabelText(/Data de fim/), { target: { value: '2026-10-31' } })
    fireEvent.change(within(dialog).getByLabelText(/Previsão de recolha/), { target: { value: '2026-11-02' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Encerrar vínculo e iniciar devoluções' }))

    const banner = await screen.findByRole('status')
    expect(banner).toHaveTextContent('Vínculo encerrado. Foi iniciada 1 devolução e 1 já estava pendente.')
    const link = within(banner).getByRole('link', { name: 'Ver devoluções' })
    expect(link).toHaveAttribute('href', '/devolucoes?status=Pending')
    await userEvent.click(within(banner).getByRole('button', { name: 'Fechar' }))
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('routes "Ver devoluções" to the return group when several returns were initiated', async () => {
    renderDetail(consultant, {
      handlers: {
        [`POST /api/consultants/${ID}/allocations/al-2/end`]: () =>
          json(200, endResult({ returnsCreatedCount: 2, returnsAlreadyPendingCount: 0, returnGroupId: 'rg-7' })),
      },
    })
    const dialog = await openEndDialog()
    await userEvent.selectOptions(within(dialog).getByLabelText(/fim do vínculo/), 'yes')
    fireEvent.change(within(dialog).getByLabelText(/Data de fim/), { target: { value: '2026-10-31' } })
    fireEvent.change(within(dialog).getByLabelText(/Previsão de recolha/), { target: { value: '2026-11-02' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Encerrar vínculo e iniciar devoluções' }))
    const link = await within(await screen.findByRole('status')).findByRole('link', { name: 'Ver devoluções' })
    expect(link).toHaveAttribute('href', '/devolucoes/grupos/rg-7')
    await userEvent.click(link)
    expect(screen.getByTestId('location')).toHaveTextContent('/devolucoes/grupos/rg-7')
  })

  it('hides "Ver devoluções" without EquipmentReturn.Read', async () => {
    renderDetail(consultant, {
      user: without(administrationUser, Permissions.EquipmentReturn.Read),
      handlers: { [`POST /api/consultants/${ID}/allocations/al-2/end`]: () => json(200, endResult({})) },
    })
    const dialog = await openEndDialog()
    await userEvent.selectOptions(within(dialog).getByLabelText(/fim do vínculo/), 'yes')
    fireEvent.change(within(dialog).getByLabelText(/Data de fim/), { target: { value: '2026-10-31' } })
    fireEvent.change(within(dialog).getByLabelText(/Previsão de recolha/), { target: { value: '2026-11-02' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Encerrar vínculo e iniciar devoluções' }))
    expect(await screen.findByRole('status')).toHaveTextContent('Vínculo encerrado.')
    expect(screen.queryByRole('link', { name: 'Ver devoluções' })).not.toBeInTheDocument()
  })

  it('shows a server rejection inside the dialog', async () => {
    renderDetail(consultant, {
      handlers: {
        [`POST /api/consultants/${ID}/allocations/al-2/end`]: () =>
          json(400, { title: 'A data de fim não pode ser anterior à data de início.' }),
      },
    })
    const dialog = await openEndDialog()
    fireEvent.change(within(dialog).getByLabelText(/Data de fim/), { target: { value: '2026-09-01' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Encerrar alocação' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('A data de fim não pode ser anterior à data de início.')
  })
})
