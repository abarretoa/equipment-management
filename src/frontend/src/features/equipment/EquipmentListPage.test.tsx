import { Link, Route, Routes, useLocation, useNavigate } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { EquipmentListPage } from './EquipmentListPage'
import { DashboardPage } from '../dashboard/DashboardPage'
import { administrationUser } from '../../test/fixtures'
import { json, renderWithProviders, stubApi } from '../../test/renderWithProviders'

const LAPTOP = '11111111-1111-4111-8111-111111111111'
const MONITOR = '22222222-2222-4222-8222-222222222222'
const ARCHIVED_TABLET = '33333333-3333-4333-8333-333333333333'
const UNKNOWN_TYPE = '44444444-4444-4444-8444-444444444444'

const type = (id: string, name: string, isActive = true) => ({
  id,
  name,
  kind: name === 'Laptop' ? 'Laptop' : 'Other',
  assetTagPrefix: 'X',
  isActive,
  createdAtUtc: '2026-01-01T00:00:00Z',
  updatedAtUtc: '2026-01-01T00:00:00Z',
})

const rows = [
  {
    id: 'e1',
    assetTag: 'EQ-NB-0001',
    equipmentTypeName: 'Laptop',
    manufacturer: 'Lenovo',
    model: 'ThinkPad T14',
    serialNumber: 'SNDEMO01',
    status: 'Available',
    createdAtUtc: '2026-03-12T10:00:00Z',
  },
  {
    id: 'e2',
    assetTag: 'EQ-NB-0002',
    equipmentTypeName: 'Laptop',
    manufacturer: 'Dell',
    model: 'Latitude 5440',
    status: 'Assigned',
    createdAtUtc: '2026-03-13T10:00:00Z',
    currentConsultantId: 'c1',
    currentConsultantName: 'Sofia Sample',
  },
]

type Handlers = Parameters<typeof stubApi>[0]

function LocationProbe() {
  const location = useLocation()
  const navigate = useNavigate()
  return (
    <>
      <output data-testid="location">{location.pathname + location.search}</output>
      <button type="button" onClick={() => void navigate(-1)}>
        Voltar
      </button>
      <button type="button" onClick={() => void navigate(1)}>
        Avançar
      </button>
      {/* Stands in for the rail's NavLink to the bare list route. */}
      <Link to="/equipamentos">Navegação Equipamentos</Link>
    </>
  )
}

function renderList({
  route = '/equipamentos',
  handlers = {},
  totalCount = 2,
}: { route?: string; handlers?: Handlers; totalCount?: number } = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, administrationUser),
    'GET /api/equipment-types': () =>
      json(200, {
        items: [type(LAPTOP, 'Laptop'), type(MONITOR, 'Monitor')],
        page: 1,
        pageSize: 100,
        totalCount: 2,
      }),
    [`GET /api/equipment-types/${ARCHIVED_TABLET}`]: () =>
      json(200, type(ARCHIVED_TABLET, 'Tablet', false)),
    [`GET /api/equipment-types/${UNKNOWN_TYPE}`]: () =>
      json(404, { title: 'Tipo de equipamento não encontrado.' }),
    'GET /api/equipment/stats': () =>
      json(200, {
        totalCount: 412,
        byStatus: [
          { status: 'Available', count: 106 },
          { status: 'Assigned', count: 274 },
        ],
        availableByType: [],
      }),
    'GET /api/equipment': () => json(200, { items: rows, page: 1, pageSize: 20, totalCount }),
    ...handlers,
  })

  const utils = renderWithProviders(
    <>
      <Routes>
        <Route path="/equipamentos" element={<EquipmentListPage />} />
        <Route path="/equipamentos/:id" element={<p>Detalhe do equipamento</p>} />
        <Route path="/consultores/:id" element={<p>Detalhe do consultor</p>} />
      </Routes>
      <LocationProbe />
    </>,
    { route },
  )
  return { fetchMock, ...utils }
}

/** Query params of every `/api/equipment` list request, oldest first. */
function listRequests(fetchMock: ReturnType<typeof stubApi>) {
  return fetchMock.mock.calls
    .map(
      ([input]) =>
        new URL(
          typeof input === 'string' ? input : input instanceof URL ? input.href : input.url,
          'http://localhost',
        ),
    )
    .filter((url) => url.pathname === '/api/equipment')
    .map((url) => Object.fromEntries(url.searchParams))
}

const lastListRequest = (fetchMock: ReturnType<typeof stubApi>) => listRequests(fetchMock).at(-1)

const location = () => screen.getByTestId('location').textContent
const tab = (name: RegExp) => screen.getByRole('tab', { name })
const typeSelect = () => screen.getByRole('combobox', { name: 'Filtrar por tipo' })
const table = () => screen.getByRole('table')

async function waitForRows() {
  await within(await screen.findByRole('table')).findByRole('link', { name: 'EQ-NB-0001' })
}

describe('EquipmentListPage — URL-backed filters', () => {
  it('applies status and type from the URL and shows both chips', async () => {
    const { fetchMock } = renderList({
      route: `/equipamentos?status=Available&equipmentTypeId=${LAPTOP}`,
    })

    await waitForRows()
    await waitFor(() =>
      expect(lastListRequest(fetchMock)).toMatchObject({
        status: 'Available',
        equipmentTypeId: LAPTOP,
        page: '1',
      }),
    )
    expect(await screen.findByText('Laptop', { selector: 'b' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Remover filtro Tipo: Laptop' })).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Remover filtro Estado: Disponível' }),
    ).toBeInTheDocument()
    expect(tab(/^Disponível/)).toHaveAttribute('aria-selected', 'true')
    expect(typeSelect()).toHaveValue(LAPTOP)
  })

  it('removing the Estado chip keeps only the type', async () => {
    const user = userEvent.setup()
    renderList({ route: `/equipamentos?status=Available&equipmentTypeId=${LAPTOP}` })

    await user.click(
      await screen.findByRole('button', { name: 'Remover filtro Estado: Disponível' }),
    )
    expect(location()).toBe(`/equipamentos?equipmentTypeId=${LAPTOP}`)
    expect(tab(/^Todos/)).toHaveAttribute('aria-selected', 'true')
  })

  it('removing the Tipo chip keeps only the status', async () => {
    const user = userEvent.setup()
    renderList({ route: `/equipamentos?status=Available&equipmentTypeId=${LAPTOP}` })

    await user.click(await screen.findByRole('button', { name: 'Remover filtro Tipo: Laptop' }))
    expect(location()).toBe('/equipamentos?status=Available')
    expect(typeSelect()).toHaveValue('')
  })

  it('"Limpar filtros" returns to /equipamentos and clears the local search', async () => {
    const user = userEvent.setup()
    renderList({
      route: `/equipamentos?status=Available&equipmentTypeId=${LAPTOP}&page=2`,
      totalCount: 45,
    })

    await user.type(await screen.findByRole('textbox', { name: 'Pesquisar' }), 'think')
    await user.click(screen.getAllByRole('button', { name: 'Limpar filtros' })[0]!)

    expect(location()).toBe('/equipamentos')
    expect(screen.getByRole('textbox', { name: 'Pesquisar' })).toHaveValue('')
  })

  it('the "Em trânsito" tab filters by InTransit and keeps the type', async () => {
    const user = userEvent.setup()
    const { fetchMock } = renderList({ route: `/equipamentos?equipmentTypeId=${LAPTOP}` })

    await waitForRows()
    await user.click(tab(/^Em trânsito/))

    expect(location()).toBe(`/equipamentos?equipmentTypeId=${LAPTOP}&status=InTransit`)
    await waitFor(() =>
      expect(lastListRequest(fetchMock)).toMatchObject({ status: 'InTransit', equipmentTypeId: LAPTOP }),
    )
  })

  it('a status tab updates status, keeps the type and resets the page', async () => {
    const user = userEvent.setup()
    const { fetchMock } = renderList({
      route: `/equipamentos?equipmentTypeId=${LAPTOP}&page=2`,
      totalCount: 45,
    })

    await waitForRows()
    await user.click(tab(/^Atribuído/))

    expect(location()).toBe(`/equipamentos?equipmentTypeId=${LAPTOP}&status=Assigned`)
    await waitFor(() =>
      expect(lastListRequest(fetchMock)).toMatchObject({
        status: 'Assigned',
        equipmentTypeId: LAPTOP,
        page: '1',
      }),
    )
  })

  it('status tabs are a labelled, manually-activated tablist', async () => {
    const user = userEvent.setup()
    renderList()
    await waitForRows()

    const tablist = screen.getByRole('tablist', { name: 'Filtrar por estado' })
    tab(/^Todos/).focus()
    await user.keyboard('{ArrowRight}')
    // Focus moved, but no filter was applied (and no history entry was created).
    expect(within(tablist).getByRole('tab', { name: /^Disponível/ })).toHaveFocus()
    expect(location()).toBe('/equipamentos')
    await user.keyboard('{Enter}')
    expect(location()).toBe('/equipamentos?status=Available')
  })

  it('changing the type updates it, keeps the status and resets the page', async () => {
    const user = userEvent.setup()
    renderList({ route: '/equipamentos?status=Available&page=3', totalCount: 65 })

    await waitForRows()
    await user.selectOptions(typeSelect(), MONITOR)
    expect(location()).toBe(`/equipamentos?status=Available&equipmentTypeId=${MONITOR}`)
  })

  it('pagination writes the page to the URL', async () => {
    const user = userEvent.setup()
    const { fetchMock } = renderList({ route: '/equipamentos?status=Available', totalCount: 45 })

    await waitForRows()
    await user.click(screen.getByRole('button', { name: 'Página seguinte' }))
    expect(location()).toBe('/equipamentos?status=Available&page=2')
    await waitFor(() =>
      expect(lastListRequest(fetchMock)).toMatchObject({ status: 'Available', page: '2' }),
    )
  })

  it('restores the same filters after a remount (refresh / bookmark)', async () => {
    const route = `/equipamentos?status=Assigned&equipmentTypeId=${LAPTOP}&page=2`
    const first = renderList({ route, totalCount: 45 })
    await waitForRows()
    first.unmount()

    const { fetchMock } = renderList({ route, totalCount: 45 })
    await waitForRows()
    expect(tab(/^Atribuído/)).toHaveAttribute('aria-selected', 'true')
    expect(typeSelect()).toHaveValue(LAPTOP)
    expect(listRequests(fetchMock)[0]).toMatchObject({
      status: 'Assigned',
      equipmentTypeId: LAPTOP,
      page: '2',
    })
  })

  it('Back and Forward restore the previous and later filter states', async () => {
    const user = userEvent.setup()
    renderList({ route: '/equipamentos?status=Available' })

    await waitForRows()
    await user.click(tab(/^Atribuído/))
    expect(location()).toBe('/equipamentos?status=Assigned')

    await user.click(screen.getByRole('button', { name: 'Voltar' }))
    expect(location()).toBe('/equipamentos?status=Available')
    expect(tab(/^Disponível/)).toHaveAttribute('aria-selected', 'true')
    expect(
      screen.getByRole('button', { name: 'Remover filtro Estado: Disponível' }),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Avançar' }))
    expect(location()).toBe('/equipamentos?status=Assigned')
    expect(tab(/^Atribuído/)).toHaveAttribute('aria-selected', 'true')
  })

  it('navigating to the bare /equipamentos (sidebar) clears the previous filters', async () => {
    const user = userEvent.setup()
    const { fetchMock } = renderList({
      route: `/equipamentos?status=Available&equipmentTypeId=${LAPTOP}`,
    })

    await waitForRows()
    await user.click(screen.getByRole('link', { name: 'Navegação Equipamentos' }))

    expect(location()).toBe('/equipamentos')
    expect(tab(/^Todos/)).toHaveAttribute('aria-selected', 'true')
    expect(typeSelect()).toHaveValue('')
    expect(screen.queryByRole('button', { name: /^Remover filtro/ })).not.toBeInTheDocument()
    await waitFor(() => expect(lastListRequest(fetchMock)).toEqual({ page: '1', pageSize: '20' }))
  })
})

describe('EquipmentListPage — defensive URL input', () => {
  it('ignores and removes an unknown status', async () => {
    const { fetchMock } = renderList({ route: '/equipamentos?status=Banana' })

    await waitForRows()
    await waitFor(() => expect(location()).toBe('/equipamentos'))
    expect(tab(/^Todos/)).toHaveAttribute('aria-selected', 'true')
    expect(listRequests(fetchMock).every((params) => params.status === undefined)).toBe(true)
  })

  it('removes a malformed type id without sending it to the API', async () => {
    const { fetchMock } = renderList({
      route: '/equipamentos?status=Available&equipmentTypeId=not-a-guid',
    })

    await waitForRows()
    await waitFor(() => expect(location()).toBe('/equipamentos?status=Available'))
    expect(screen.queryByRole('button', { name: /^Remover filtro Tipo/ })).not.toBeInTheDocument()
    expect(listRequests(fetchMock).every((params) => params.equipmentTypeId === undefined)).toBe(
      true,
    )
  })

  it('removes a well-formed type id that does not exist', async () => {
    renderList({ route: `/equipamentos?equipmentTypeId=${UNKNOWN_TYPE}` })

    await waitFor(() => expect(location()).toBe('/equipamentos'))
    expect(screen.queryByRole('button', { name: /^Remover filtro Tipo/ })).not.toBeInTheDocument()
  })

  it('names an archived type in its chip instead of filtering invisibly', async () => {
    const { fetchMock } = renderList({ route: `/equipamentos?equipmentTypeId=${ARCHIVED_TABLET}` })

    expect(
      await screen.findByRole('button', { name: 'Remover filtro Tipo: Tablet' }),
    ).toBeInTheDocument()
    expect(typeSelect()).toHaveValue(ARCHIVED_TABLET)
    expect(
      within(typeSelect()).getByRole('option', { name: 'Tablet (inativo)' }),
    ).toBeInTheDocument()
    expect(lastListRequest(fetchMock)).toMatchObject({ equipmentTypeId: ARCHIVED_TABLET })
  })
})

describe('EquipmentListPage — search, rows and empty states', () => {
  it('keeps search local: typing never writes to the URL', async () => {
    const user = userEvent.setup()
    const { fetchMock } = renderList({ route: '/equipamentos?status=Available' })

    await waitForRows()
    expect(screen.getByRole('textbox', { name: 'Pesquisar' })).toHaveAttribute('placeholder', 'Pesquisar equipamentos')
    await user.type(screen.getByRole('textbox', { name: 'Pesquisar' }), 'thinkpad')

    expect(screen.getByRole('textbox', { name: 'Pesquisar' })).toHaveValue('thinkpad')
    expect(location()).toBe('/equipamentos?status=Available')
    await waitFor(() =>
      expect(lastListRequest(fetchMock)).toMatchObject({ search: 'thinkpad', status: 'Available' }),
    )
  })

  it('a new search drops the page from the URL without adding history', async () => {
    const user = userEvent.setup()
    renderList({ route: '/equipamentos?status=Available&page=2', totalCount: 45 })

    await waitForRows()
    await user.type(screen.getByRole('textbox', { name: 'Pesquisar' }), 'x')
    expect(location()).toBe('/equipamentos?status=Available')
  })

  it('the consultant link opens the consultant, not the equipment', async () => {
    const user = userEvent.setup()
    renderList()

    await waitForRows()
    await user.click(within(table()).getByRole('link', { name: 'Sofia Sample' }))
    expect(location()).toBe('/consultores/c1')
  })

  it('the identifier is a keyboard-reachable link to the detail page', async () => {
    const user = userEvent.setup()
    renderList()

    await waitForRows()
    const link = within(table()).getByRole('link', { name: 'EQ-NB-0001' })
    expect(link).toHaveAttribute('href', '/equipamentos/e1')
    link.focus()
    await user.keyboard('{Enter}')
    expect(location()).toBe('/equipamentos/e1')
  })

  it('a filtered empty result offers to clear the filters', async () => {
    const user = userEvent.setup()
    renderList({
      route: '/equipamentos?status=Damaged',
      handlers: {
        'GET /api/equipment': () => json(200, { items: [], page: 1, pageSize: 20, totalCount: 0 }),
      },
    })

    expect(await screen.findByText('Nenhum equipamento encontrado')).toBeInTheDocument()
    expect(screen.getByText(/corresponde aos filtros aplicados/)).toBeInTheDocument()
    const clearButtons = screen.getAllByRole('button', { name: 'Limpar filtros' })
    await user.click(clearButtons[clearButtons.length - 1]!)
    expect(location()).toBe('/equipamentos')
  })

  it('an empty inventory without filters says so', async () => {
    renderList({
      handlers: {
        'GET /api/equipment': () => json(200, { items: [], page: 1, pageSize: 20, totalCount: 0 }),
      },
    })

    expect(await screen.findByText('Ainda não existem equipamentos registados')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Limpar filtros' })).not.toBeInTheDocument()
  })

  it('shows the error state with retry when the list fails', async () => {
    renderList({
      handlers: { 'GET /api/equipment': () => json(500, { title: 'Falha ao listar.' }) },
    })

    expect(await screen.findByRole('alert')).toHaveTextContent('Falha ao listar.')
    expect(screen.getByRole('button', { name: 'Tentar novamente' })).toBeInTheDocument()
  })
})

describe('EquipmentListPage — status counts follow the type scope', () => {
  // Distinct per-scope numbers, so a tab can only show the right one.
  const statsByType: Record<string, { totalCount: number; byStatus: { status: string; count: number }[] }> = {
    global: { totalCount: 412, byStatus: [{ status: 'Available', count: 106 }, { status: 'Assigned', count: 274 }, { status: 'Retired', count: 32 }] },
    [LAPTOP]: { totalCount: 41, byStatus: [{ status: 'Available', count: 2 }, { status: 'Assigned', count: 39 }] },
    [MONITOR]: { totalCount: 22, byStatus: [{ status: 'Available', count: 22 }] },
  }
  const scopedStats: Handlers = {
    'GET /api/equipment/stats': (_init, url) =>
      json(200, { ...statsByType[url?.searchParams.get('equipmentTypeId') ?? 'global'], availableByType: [] }),
  }

  /** Every /api/equipment/stats request's equipmentTypeId (null = global), oldest first. */
  function statsScopes(fetchMock: ReturnType<typeof stubApi>) {
    return fetchMock.mock.calls
      .map(([input]) => new URL(typeof input === 'string' ? input : input instanceof URL ? input.href : input.url, 'http://localhost'))
      .filter((url) => url.pathname === '/api/equipment/stats')
      .map((url) => ({ type: url.searchParams.get('equipmentTypeId'), keys: [...url.searchParams.keys()] }))
  }

  const expectTabs = async (counts: Record<string, string>) => {
    for (const [label, count] of Object.entries(counts)) {
      await waitFor(() => expect(tab(new RegExp(`^${label}`))).toHaveTextContent(new RegExp(`^${label}\\s*${count}$`)))
    }
  }

  it('without a type filter, the counts are inventory-wide and zero statuses stay visible', async () => {
    const { fetchMock } = renderList({ handlers: scopedStats })

    await waitForRows()
    await expectTabs({ Todos: '412', Disponível: '106', Atribuído: '274', Abatido: '32', 'Em manutenção': '0' })
    expect(statsScopes(fetchMock).every((s) => s.type === null)).toBe(true)
  })

  it('with a type filter, every tab counts only that type — the active status does not narrow the others', async () => {
    const { fetchMock } = renderList({ route: `/equipamentos?status=Available&equipmentTypeId=${LAPTOP}`, handlers: scopedStats })

    await waitForRows()
    await expectTabs({ Todos: '41', Disponível: '2', Atribuído: '39', 'Em manutenção': '0', Abatido: '0' })
    expect(lastListRequest(fetchMock)).toMatchObject({ status: 'Available', equipmentTypeId: LAPTOP })
    expect(statsScopes(fetchMock).at(-1)).toEqual({ type: LAPTOP, keys: ['equipmentTypeId'] })
  })

  it('changing the type rescopes the counts, and removing the Tipo chip returns them to global', async () => {
    const user = userEvent.setup()
    renderList({ route: `/equipamentos?equipmentTypeId=${LAPTOP}`, handlers: scopedStats })

    await waitForRows()
    await expectTabs({ Todos: '41', Disponível: '2' })

    await user.selectOptions(typeSelect(), MONITOR)
    await expectTabs({ Todos: '22', Disponível: '22', Atribuído: '0' })

    await user.click(screen.getByRole('button', { name: 'Remover filtro Tipo: Monitor' }))
    expect(location()).toBe('/equipamentos')
    await expectTabs({ Todos: '412', Disponível: '106', Atribuído: '274' })
  })

  it('removing only the status keeps the type and its scoped counts', async () => {
    const user = userEvent.setup()
    renderList({ route: `/equipamentos?status=Available&equipmentTypeId=${LAPTOP}`, handlers: scopedStats })

    await waitForRows()
    await user.click(screen.getByRole('button', { name: 'Remover filtro Estado: Disponível' }))
    expect(location()).toBe(`/equipamentos?equipmentTypeId=${LAPTOP}`)
    await expectTabs({ Todos: '41', Atribuído: '39' })
  })

  it('search and pagination never reach the counts', async () => {
    const user = userEvent.setup()
    const { fetchMock } = renderList({ route: `/equipamentos?equipmentTypeId=${LAPTOP}`, handlers: scopedStats, totalCount: 45 })

    await waitForRows()
    await user.click(screen.getByRole('button', { name: 'Página seguinte' }))
    await user.type(screen.getByRole('textbox', { name: 'Pesquisar' }), 'thinkpad')
    await waitFor(() => expect(lastListRequest(fetchMock)).toMatchObject({ search: 'thinkpad' }))

    await expectTabs({ Todos: '41', Disponível: '2' })
    expect(statsScopes(fetchMock).every((s) => s.keys.every((k) => k === 'equipmentTypeId'))).toBe(true)
  })

  it('an invalid type id is normalized away and the counts stay global', async () => {
    const { fetchMock } = renderList({ route: '/equipamentos?equipmentTypeId=not-a-guid', handlers: scopedStats })

    await waitForRows()
    await waitFor(() => expect(location()).toBe('/equipamentos'))
    await expectTabs({ Todos: '412', Disponível: '106' })
    expect(statsScopes(fetchMock).every((s) => s.type === null)).toBe(true)
  })
})

describe('Dashboard → Equipment list (required flow)', () => {
  it('Laptop → Disponível lands on the list with both chips, matching records, independent removal and Back', async () => {
    const user = userEvent.setup()
    const fetchMock = stubApi({
      'GET /api/auth/me': () => json(200, administrationUser),
      'GET /api/equipment-types': () =>
        json(200, {
          items: [type(LAPTOP, 'Laptop'), type(MONITOR, 'Monitor')],
          page: 1,
          pageSize: 100,
          totalCount: 2,
        }),
      'GET /api/equipment/stats': () =>
        json(200, {
          totalCount: 412,
          byStatus: [{ status: 'Available', count: 106 }],
          availableByType: [
            { equipmentTypeId: LAPTOP, equipmentTypeName: 'Laptop', count: 41 },
            { equipmentTypeId: MONITOR, equipmentTypeName: 'Monitor', count: 22 },
          ],
        }),
      'GET /api/equipment': (_init, url) =>
        json(200, {
          items: rows.filter(
            (r) => !url.searchParams.get('status') || r.status === url.searchParams.get('status'),
          ),
          page: 1,
          pageSize: 20,
          totalCount: 1,
        }),
      'GET /api/consultants': () => json(200, { items: [], page: 1, pageSize: 1, totalCount: 0 }),
      'GET /api/equipment-handovers': () =>
        json(200, { items: [], page: 1, pageSize: 5, totalCount: 0 }),
      'GET /api/equipment-returns': () =>
        json(200, { items: [], page: 1, pageSize: 5, totalCount: 0 }),
    })

    renderWithProviders(
      <>
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/equipamentos" element={<EquipmentListPage />} />
        </Routes>
        <LocationProbe />
      </>,
    )

    await user.click(await screen.findByRole('button', { name: 'Laptop 41' }))
    await user.click(await screen.findByRole('link', { name: /^Disponível: \d+\. .*tipo Laptop$/ }))

    expect(location()).toBe(`/equipamentos?status=Available&equipmentTypeId=${LAPTOP}`)
    expect(
      await screen.findByRole('button', { name: 'Remover filtro Tipo: Laptop' }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Remover filtro Estado: Disponível' }),
    ).toBeInTheDocument()
    await waitForRows()
    expect(lastListRequest(fetchMock)).toMatchObject({
      status: 'Available',
      equipmentTypeId: LAPTOP,
    })

    await user.click(screen.getByRole('button', { name: 'Remover filtro Estado: Disponível' }))
    expect(location()).toBe(`/equipamentos?equipmentTypeId=${LAPTOP}`)
    expect(screen.getByRole('button', { name: 'Remover filtro Tipo: Laptop' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Voltar' }))
    await user.click(screen.getByRole('button', { name: 'Voltar' }))
    expect(location()).toBe(`/?equipmentTypeId=${LAPTOP}`)
    expect(await screen.findByRole('button', { name: 'Laptop 41' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })
})
