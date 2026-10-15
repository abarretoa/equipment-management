import { Route, Routes, useLocation, useNavigate } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { DashboardPage } from './DashboardPage'
import { administrationUser } from '../../test/fixtures'
import { json, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import { Permissions } from '../../shared/permissions'
import type { CurrentUser } from '../auth/types'

const LAPTOP = 'type-laptop'

/** Eight available types — more than the old six-item cap. */
const availableByType = [
  { equipmentTypeId: LAPTOP, equipmentTypeName: 'Laptop', count: 4 },
  { equipmentTypeId: 'type-monitor', equipmentTypeName: 'Monitor', count: 7 },
  { equipmentTypeId: 'type-phone', equipmentTypeName: 'Telemóvel', count: 5 },
  { equipmentTypeId: 'type-headset', equipmentTypeName: 'Auscultadores', count: 3 },
  { equipmentTypeId: 'type-dock', equipmentTypeName: 'Dock', count: 2 },
  { equipmentTypeId: 'type-keyboard', equipmentTypeName: 'Teclado', count: 6 },
  { equipmentTypeId: 'type-mouse', equipmentTypeName: 'Rato', count: 2 },
  { equipmentTypeId: 'type-tablet', equipmentTypeName: 'Tablet', count: 1 },
]

const globalStats = {
  totalCount: 419,
  byStatus: [
    { status: 'Available', count: 30 },
    { status: 'InTransit', count: 7 },
    { status: 'Assigned', count: 300 },
    { status: 'ReturnPending', count: 18 },
    { status: 'UnderInspection', count: 6 },
    { status: 'Damaged', count: 5 },
    { status: 'UnderMaintenance', count: 19 },
    { status: 'Retired', count: 34 },
  ],
  availableByType,
}

/** Laptop-scoped: Damaged is absent from the payload, Retired is an explicit zero. */
const laptopStats = {
  totalCount: 160,
  byStatus: [
    { status: 'Available', count: 4 },
    { status: 'Assigned', count: 150 },
    { status: 'ReturnPending', count: 3 },
    { status: 'UnderInspection', count: 2 },
    { status: 'UnderMaintenance', count: 1 },
    { status: 'Retired', count: 0 },
  ],
  availableByType: [availableByType[0]],
}

const paged = (totalCount: number, items: unknown[] = []) => ({
  items,
  page: 1,
  pageSize: 5,
  totalCount,
})

type Handlers = Parameters<typeof stubApi>[0]

function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname + location.search}</output>
}

function EquipmentListProbe() {
  const navigate = useNavigate()
  return (
    <>
      <LocationProbe />
      <button type="button" onClick={() => void navigate(-1)}>
        Voltar
      </button>
    </>
  )
}

function renderDashboard({
  route = '/',
  user = administrationUser,
  handlers = {},
}: { route?: string; user?: CurrentUser; handlers?: Handlers } = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, user),
    'GET /api/equipment/stats': (_init, url) =>
      json(200, url.searchParams.get('equipmentTypeId') === LAPTOP ? laptopStats : globalStats),
    'GET /api/consultants': () => json(200, paged(238)),
    'GET /api/equipment-handovers': (_init, url) =>
      url.searchParams.get('status') === 'Draft'
        ? json(200, paged(3))
        : json(
            200,
            paged(1, [
              {
                id: 'handover-1',
                consultantId: 'c-1',
                consultantFullName: 'Sofia Sample',
                status: 'Draft',
                operationalStatus: 'AwaitingSignedTerm',
                itemCount: 1,
                createdAtUtc: '2026-10-08T10:00:00Z',
              },
            ]),
          ),
    'GET /api/equipment-returns': (_init, url) => {
      const status = url.searchParams.get('status')
      if (status === 'Pending') return json(200, paged(20))
      if (status === 'Received') return json(200, paged(4))
      return json(
        200,
        paged(1, [
          {
            id: 'return-1',
            equipmentAssignmentId: 'a-1',
            equipmentId: 'e-1',
            assetTag: 'EQ-NB-0412',
            consultantId: 'c-2',
            consultantFullName: 'Tiago Example',
            status: 'Received',
            createdAtUtc: '2026-10-07T10:00:00Z',
          },
        ]),
      )
    },
    ...handlers,
  })

  renderWithProviders(
    <Routes>
      <Route
        path="/"
        element={
          <>
            <DashboardPage />
            <LocationProbe />
          </>
        }
      />
      <Route path="/equipamentos" element={<EquipmentListProbe />} />
    </Routes>,
    { route },
  )
  return fetchMock
}

function statsRequests(fetchMock: ReturnType<typeof stubApi>) {
  return fetchMock.mock.calls
    .map(
      ([input]) =>
        new URL(
          typeof input === 'string' ? input : input instanceof URL ? input.href : input.url,
          'http://localhost',
        ),
    )
    .filter((url) => url.pathname === '/api/equipment/stats')
    .map((url) => url.searchParams.get('equipmentTypeId'))
}

const location = () => screen.getByTestId('location').textContent

const typeButton = (name: string) =>
  screen.getByRole('button', { name: new RegExp(`^${name} \\d+$`) })

/** Scoped to the status region — "Em inspeção" / "Em manutenção" also label attention rows. */
const statusLink = (label: string) =>
  within(screen.getByRole('region', { name: /^Equipamento por estado/ })).getByRole('link', {
    name: new RegExp(`^${label}:`),
  })

async function findStatusHeading(scope: string) {
  return screen.findByRole('heading', { level: 2, name: `Equipamento por estado · ${scope}` })
}

describe('DashboardPage — type selection', () => {
  it('starts in the all-types state with every type unpressed', async () => {
    renderDashboard()

    await findStatusHeading('Todos os tipos')
    expect(await screen.findByRole('link', { name: /^Disponível: 30\./ })).toBeInTheDocument()
    for (const type of availableByType) {
      expect(typeButton(type.equipmentTypeName)).toHaveAttribute('aria-pressed', 'false')
    }
    expect(screen.queryByRole('button', { name: /^Todos os tipos/ })).not.toBeInTheDocument()
    expect(location()).toBe('/')
  })

  it('selecting Laptop stays on the Dashboard, writes the URL and scopes the status counts', async () => {
    const user = userEvent.setup()
    const fetchMock = renderDashboard()

    await user.click(await screen.findByRole('button', { name: 'Laptop 4' }))

    expect(location()).toBe(`/?equipmentTypeId=${LAPTOP}`)
    expect(typeButton('Laptop')).toHaveAttribute('aria-pressed', 'true')
    await findStatusHeading('Laptop')
    expect(await screen.findByRole('link', { name: /^Disponível: 4\./ })).toBeInTheDocument()
    expect(statsRequests(fetchMock)).toContain(LAPTOP)
  })

  it('clicking the selected type again clears the scope', async () => {
    const user = userEvent.setup()
    renderDashboard({ route: `/?equipmentTypeId=${LAPTOP}` })

    await findStatusHeading('Laptop')
    await user.click(typeButton('Laptop'))

    expect(location()).toBe('/')
    expect(typeButton('Laptop')).toHaveAttribute('aria-pressed', 'false')
    await findStatusHeading('Todos os tipos')
  })

  it('"Todos os tipos" clears the scope without leaving the Dashboard', async () => {
    const user = userEvent.setup()
    renderDashboard({ route: `/?equipmentTypeId=${LAPTOP}` })

    await findStatusHeading('Laptop')
    await user.click(
      screen.getByRole('button', { name: /^Todos os tipos \(remover o filtro Laptop\)$/ }),
    )

    expect(location()).toBe('/')
    await findStatusHeading('Todos os tipos')
  })

  it('renders and allows selecting every available type — no six-type cap', async () => {
    const user = userEvent.setup()
    renderDashboard()

    const group = await screen.findByRole('group', { name: 'Inventário disponível por tipo' })
    expect(within(group).getAllByRole('button')).toHaveLength(availableByType.length)
    expect(screen.queryByText(/outros tipos/)).not.toBeInTheDocument()

    const last = { equipmentTypeId: 'type-tablet', equipmentTypeName: 'Tablet' } // eighth type
    await user.click(typeButton(last.equipmentTypeName))
    expect(location()).toBe(`/?equipmentTypeId=${last.equipmentTypeId}`)
    expect(typeButton(last.equipmentTypeName)).toHaveAttribute('aria-pressed', 'true')
  })
})

describe('DashboardPage — URL restoration', () => {
  it('restores the Laptop scope from ?equipmentTypeId', async () => {
    const fetchMock = renderDashboard({ route: `/?equipmentTypeId=${LAPTOP}` })

    await findStatusHeading('Laptop')
    expect(typeButton('Laptop')).toHaveAttribute('aria-pressed', 'true')
    expect(await screen.findByRole('link', { name: /^Atribuído: 150\./ })).toBeInTheDocument()
    expect(statsRequests(fetchMock)).toContain(LAPTOP)
  })

  it('Dashboard Laptop → Disponível → Back returns to the Laptop-scoped Dashboard', async () => {
    const user = userEvent.setup()
    renderDashboard()

    await user.click(await screen.findByRole('button', { name: 'Laptop 4' }))
    await user.click(await screen.findByRole('link', { name: /^Disponível: 4\./ }))
    expect(location()).toBe(`/equipamentos?status=Available&equipmentTypeId=${LAPTOP}`)

    await user.click(screen.getByRole('button', { name: 'Voltar' }))

    expect(location()).toBe(`/?equipmentTypeId=${LAPTOP}`)
    await findStatusHeading('Laptop')
    expect(typeButton('Laptop')).toHaveAttribute('aria-pressed', 'true')
    expect(await screen.findByRole('link', { name: /^Disponível: 4\./ })).toBeInTheDocument()
  })

  it('falls back to all types for an unresolvable id, normalises the URL and never sends it to the API', async () => {
    const fetchMock = renderDashboard({ route: '/?equipmentTypeId=not-a-real-type' })

    await findStatusHeading('Todos os tipos')
    await waitFor(() => expect(location()).toBe('/'))
    for (const type of availableByType) {
      expect(typeButton(type.equipmentTypeName)).toHaveAttribute('aria-pressed', 'false')
    }
    expect(statsRequests(fetchMock)).not.toContain('not-a-real-type')
    expect(statsRequests(fetchMock).every((id) => id === null)).toBe(true)
  })
})

describe('DashboardPage — status navigation', () => {
  it('renders all eight statuses, with Damaged as "Avariado"', async () => {
    renderDashboard()
    await screen.findByRole('link', { name: /^Disponível: 30\./ })

    for (const label of [
      'Disponível',
      'Em trânsito',
      'Atribuído',
      'Devolução pendente',
      'Em inspeção',
      'Avariado',
      'Em manutenção',
      'Abatido',
    ]) {
      expect(statusLink(label)).toBeInTheDocument()
    }
    expect(statusLink('Avariado')).toHaveAttribute('href', '/equipamentos?status=Damaged')
  })

  it('counts Em trânsito as its own status, never as Disponível, and links to its filter', async () => {
    renderDashboard()

    const inTransit = await screen.findByRole('link', { name: /^Em trânsito: 7\./ })
    expect(inTransit).toHaveAttribute('href', '/equipamentos?status=InTransit')
    expect(statusLink('Disponível')).toHaveAccessibleName(/^Disponível: 30\./)
  })

  it('keeps Em trânsito visible at zero when the type scope has none, and carries the type', async () => {
    renderDashboard({ route: `/?equipmentTypeId=${LAPTOP}` })

    const inTransit = await screen.findByRole('link', { name: /^Em trânsito: 0\./ })
    expect(inTransit).toHaveAttribute('href', `/equipamentos?status=InTransit&equipmentTypeId=${LAPTOP}`)
  })

  it('links to the unscoped Equipment list when no type is selected', async () => {
    renderDashboard()
    expect(await screen.findByRole('link', { name: /^Disponível: 30\./ })).toHaveAttribute(
      'href',
      '/equipamentos?status=Available',
    )
  })

  it('carries the selected type into every status link', async () => {
    renderDashboard({ route: `/?equipmentTypeId=${LAPTOP}` })

    const available = await screen.findByRole('link', {
      name: 'Disponível: 4. Abrir equipamentos com estado Disponível, tipo Laptop',
    })
    expect(available).toHaveAttribute(
      'href',
      `/equipamentos?status=Available&equipmentTypeId=${LAPTOP}`,
    )
    expect(statusLink('Atribuído')).toHaveAttribute(
      'href',
      `/equipamentos?status=Assigned&equipmentTypeId=${LAPTOP}`,
    )
  })

  it('keeps zero-count statuses visible and navigable', async () => {
    renderDashboard({ route: `/?equipmentTypeId=${LAPTOP}` })

    const damaged = await screen.findByRole('link', { name: /^Avariado: 0\./ })
    expect(damaged).toHaveAttribute(
      'href',
      `/equipamentos?status=Damaged&equipmentTypeId=${LAPTOP}`,
    )
    expect(damaged).not.toHaveAttribute('aria-disabled')
    expect(statusLink('Abatido')).toHaveAttribute(
      'href',
      `/equipamentos?status=Retired&equipmentTypeId=${LAPTOP}`,
    )
  })
})

describe('DashboardPage — errors and loading', () => {
  it('shows an error, not zeros, when equipment stats fail — and retry refetches', async () => {
    const user = userEvent.setup()
    let fail = true
    renderDashboard({
      handlers: {
        'GET /api/equipment/stats': () =>
          fail ? json(500, { title: 'Erro ao obter estatísticas.' }) : json(200, globalStats),
      },
    })

    const alerts = await screen.findAllByRole('alert')
    expect(alerts.length).toBeGreaterThan(0)
    expect(alerts[0]).toHaveTextContent('Não foi possível carregar os dados.')
    expect(screen.queryByRole('link', { name: /^Disponível: 0/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /^Disponível:/ })).not.toBeInTheDocument()
    expect(screen.queryByText(/equipamentos disponíveis/)).not.toBeInTheDocument()
    // Attention rows that depend on the stats read as unavailable, not "0".
    expect(screen.getByRole('link', { name: /^Em inspeção.*indisponível$/ })).toHaveAttribute(
      'href',
      '/equipamentos?status=UnderInspection',
    )
    // Unrelated sections still load.
    expect(await screen.findByRole('link', { name: /Sofia Sample/ })).toBeInTheDocument()

    fail = false
    const inventory = screen.getByRole('region', { name: 'Inventário disponível' })
    await user.click(within(inventory).getByRole('button', { name: 'Tentar novamente' }))

    expect(await screen.findByRole('link', { name: /^Disponível: 30\./ })).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
  })

  it('shows an error for the scoped stats without breaking the type selector', async () => {
    renderDashboard({
      route: `/?equipmentTypeId=${LAPTOP}`,
      handlers: {
        'GET /api/equipment/stats': (_init, url) =>
          url.searchParams.get('equipmentTypeId')
            ? json(500, { title: 'Falhou.' })
            : json(200, globalStats),
      },
    })

    await findStatusHeading('Laptop')
    expect(await screen.findByRole('alert')).toHaveTextContent('Falhou.')
    expect(screen.queryByRole('link', { name: /^Disponível:/ })).not.toBeInTheDocument()
    expect(typeButton('Laptop')).toHaveAttribute('aria-pressed', 'true')
  })

  it('does not present a failed recent-Entregas query as "Sem entregas recentes"', async () => {
    renderDashboard({
      handlers: {
        'GET /api/equipment-handovers': (_init, url) =>
          url.searchParams.get('status') === 'Draft'
            ? json(200, paged(3))
            : json(500, { title: 'Indisponível.' }),
      },
    })

    expect(await screen.findByRole('alert')).toHaveTextContent('Indisponível.')
    expect(screen.queryByText('Sem entregas recentes')).not.toBeInTheDocument()
    // The Devoluções list and the equipment stats are unaffected.
    expect(await screen.findByRole('link', { name: /Tiago Example/ })).toBeInTheDocument()
    expect(await screen.findByRole('link', { name: /^Disponível: 30\./ })).toBeInTheDocument()
  })
})

describe('DashboardPage — permissions', () => {
  it('shows "Nova entrega" with EquipmentHandover.Create', async () => {
    renderDashboard()
    expect(await screen.findByRole('button', { name: 'Nova entrega' })).toBeInTheDocument()
  })

  it('hides "Nova entrega" without EquipmentHandover.Create', async () => {
    renderDashboard({
      user: {
        ...administrationUser,
        permissions: administrationUser.permissions.filter(
          (p) => p !== Permissions.EquipmentHandover.Create,
        ),
      },
    })
    await screen.findByRole('heading', { level: 1, name: 'Olá, Eva' })
    expect(screen.queryByRole('button', { name: 'Nova entrega' })).not.toBeInTheDocument()
  })
})

describe('DashboardPage — recent activity and attention', () => {
  it('links recent Entregas and Devoluções to their detail pages', async () => {
    renderDashboard()

    expect(await screen.findByRole('link', { name: /Sofia Sample/ })).toHaveAttribute(
      'href',
      '/entregas/handover-1',
    )
    expect(await screen.findByRole('link', { name: /Tiago Example/ })).toHaveAttribute(
      'href',
      '/devolucoes/return-1',
    )
  })

  it('keeps Atenção operacional global when a type is selected', async () => {
    renderDashboard({ route: `/?equipmentTypeId=${LAPTOP}` })

    await findStatusHeading('Laptop')
    // Laptop has 2 under inspection; the attention row still shows the global 6.
    expect(await screen.findByRole('link', { name: /^Atribuído: 150\./ })).toBeInTheDocument()
    const attention = screen.getByRole('region', { name: 'Atenção operacional' })
    const rows = within(attention).getAllByRole('link')
    expect(rows.map((row) => [row.textContent, row.getAttribute('href')])).toEqual([
      ['Entregas em rascunho: 3', '/entregas?status=Draft'],
      ['Devoluções pendentes: 24', '/devolucoes'],
      ['Em inspeção: 6', '/equipamentos?status=UnderInspection'],
      ['Equipamentos avariados: 5', '/equipamentos?status=Damaged'],
      ['Em manutenção: 19', '/manutencao'],
    ])
  })
})
