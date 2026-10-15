import { Route, Routes } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ConsultantImportExportPage } from './ConsultantImportExportPage'
import { EquipmentImportExportPage } from './EquipmentImportExportPage'
import { CurrentInventoryImportPage } from './CurrentInventoryImportPage'
import { RequirePermission } from '../auth/RouteGuards'
import { administrationUser, hrUser } from '../../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import { Permissions } from '../../shared/permissions'
import type { CurrentUser } from '../auth/types'
import type { ConsultantImportRow, CurrentInventoryImportPreviewResponse, CurrentInventoryImportRow, ImportPreviewResponse } from './types'

const consultantRow = (rowNumber: number, overrides: Partial<ConsultantImportRow> = {}): ConsultantImportRow => ({
  rowNumber,
  status: 'Valid',
  fullName: `Consultor ${rowNumber}`,
  nif: `12345678${rowNumber}`,
  email: `c${rowNumber}@example.test`,
  address: 'Rua 1',
  phone: '910000000',
  hasAllocation: false,
  issues: [],
  ...overrides,
})

const preview = (rows: ConsultantImportRow[]): ImportPreviewResponse<ConsultantImportRow> => ({
  importSessionId: 's-1',
  fileName: 'consultores.xlsx',
  fileSha256: 'x',
  status: 'Pending',
  totalRows: rows.length,
  validRows: rows.filter((r) => r.status === 'Valid').length,
  warningRows: rows.filter((r) => r.status === 'Warning').length,
  errorRows: rows.filter((r) => r.status === 'Error').length,
  createdAtUtc: '2026-10-10T10:00:00Z',
  rows,
})

const withWarning = consultantRow(2, {
  status: 'Warning',
  issues: [{ code: 'W', field: 'Phone', message: 'Telefone em formato pouco habitual.', severity: 'Warning' }],
})
const withError = consultantRow(3, {
  status: 'Error',
  issues: [{ code: 'E', field: 'Nif', message: 'O NIF já existe na plataforma.', severity: 'Error' }],
})

const xlsx = () => new File(['x'], 'consultores.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })

function render(ui: React.ReactElement, user: CurrentUser = administrationUser, handlers: Parameters<typeof stubApi>[0] = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, user),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    'GET /api/equipment-types': () => json(200, { items: [], page: 1, pageSize: 100, totalCount: 0 }),
    ...handlers,
  })
  renderWithProviders(
    <Routes>
      <Route path="/x" element={ui} />
    </Routes>,
    { route: '/x' },
  )
  return fetchMock
}

async function upload(file = xlsx()) {
  const input = await waitFor(() => {
    const el = document.querySelector<HTMLInputElement>('input[type="file"]')
    if (!el) throw new Error('no upload input yet')
    return el
  })
  await userEvent.upload(input, file)
}

beforeEach(() => {
  vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: () => 'blob:file', revokeObjectURL: () => {} }))
})
afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Consultant import', () => {
  it('accepts only .xlsx and shows the size rule', async () => {
    render(<ConsultantImportExportPage />)
    await screen.findByRole('heading', { name: 'Importar consultores' })
    expect(document.querySelector('input[type="file"]')).toHaveAttribute('accept', '.xlsx')
    expect(screen.getByText('Máximo 5 MB, formato .xlsx')).toBeInTheDocument()
    expect(screen.getByText('Carregar ficheiro')).toBeInTheDocument()
  })

  it('renders the real validation results; blocking errors read differently from warnings and block confirmation', async () => {
    render(<ConsultantImportExportPage />, administrationUser, {
      'POST /api/imports/consultants/preview': () => json(200, preview([consultantRow(1), withWarning, withError])),
    })
    await upload()

    const region = await screen.findByRole('region', { name: 'Linhas do ficheiro' })
    const table = within(region).getByRole('table')
    expect(table).toHaveStyle({ minWidth: '920px' })
    expect(within(table).getByText('Erro:', { exact: false }).closest('li')).toHaveTextContent('Erro: O NIF já existe na plataforma.')
    expect(within(table).getByText('Aviso:', { exact: false }).closest('li')).toHaveTextContent('Aviso: Telefone em formato pouco habitual.')
    expect(screen.getByText(/1 linha apresenta erros/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Confirmar importação' })).toBeDisabled()
  })

  it('warnings alone do not block: confirm posts to the existing endpoint and reports the result', async () => {
    const fetchMock = render(<ConsultantImportExportPage />, administrationUser, {
      'POST /api/imports/consultants/preview': () => json(200, preview([consultantRow(1), withWarning])),
      'POST /api/imports/consultants/s-1/confirm': () =>
        json(200, { importSessionId: 's-1', status: 'Completed', completedAtUtc: '2026-10-10T10:01:00Z', createdConsultants: 2, createdAllocations: 0 }),
    })
    await upload()

    expect(await screen.findByText('1 linha tem avisos. Será importada mesmo assim.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar importação' }))

    expect(await screen.findByText('Foram importados 2 consultores.')).toBeInTheDocument()
    expect(recordedRequests(fetchMock).filter((r) => r.method === 'POST').map((r) => r.url.pathname)).toEqual([
      '/api/imports/consultants/preview',
      '/api/imports/consultants/s-1/confirm',
    ])
  })

  it('shows a failed confirmation (previously silent) next to the actions', async () => {
    render(<ConsultantImportExportPage />, administrationUser, {
      'POST /api/imports/consultants/preview': () => json(200, preview([consultantRow(1)])),
      'POST /api/imports/consultants/s-1/confirm': () => json(410, { title: 'A sessão de importação expirou.' }),
    })
    await upload()
    await userEvent.click(await screen.findByRole('button', { name: 'Confirmar importação' }))
    expect(await screen.findByText('A sessão de importação expirou.')).toBeInTheDocument()
  })

  it('without the import permission only the export is offered', async () => {
    render(<ConsultantImportExportPage />, {
      ...administrationUser,
      permissions: administrationUser.permissions.filter((p) => p !== Permissions.Consultant.Import),
    })
    expect(await screen.findByRole('heading', { name: 'Exportar consultores' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Importar consultores' })).not.toBeInTheDocument()
    expect(document.querySelector('input[type="file"]')).toBeNull()
  })

  it('the route itself stays gated by the import permission', async () => {
    render(
      <RequirePermission permission={Permissions.Consultant.Import}>
        <ConsultantImportExportPage />
      </RequirePermission>,
      { ...hrUser, permissions: hrUser.permissions.filter((p) => p !== Permissions.Consultant.Import) },
    )
    expect(await screen.findByText('Sem permissão de acesso')).toBeInTheDocument()
  })

  it('export sends the chosen filters to the existing export endpoint, and reports a failure', async () => {
    const fetchMock = render(<ConsultantImportExportPage />, administrationUser, {
      'GET /api/exports/consultants': () => json(400, { title: 'Filtro inválido.' }),
    })
    await screen.findByRole('heading', { name: 'Exportar consultores' })
    await userEvent.selectOptions(screen.getByLabelText('Estado'), 'Ativos')
    await userEvent.selectOptions(screen.getByLabelText('Regime'), 'Externo')
    await userEvent.click(screen.getByRole('button', { name: 'Exportar' }))

    expect(await screen.findByText('Filtro inválido.')).toBeInTheDocument()
    const request = recordedRequests(fetchMock).find((r) => r.url.pathname === '/api/exports/consultants')
    expect(request?.url.searchParams.get('isActive')).toBe('true')
    expect(request?.url.searchParams.get('workforceType')).toBe('External')
  })
})

describe('Equipment import / export', () => {
  it('keeps both sections and their real actions', async () => {
    render(<EquipmentImportExportPage />)
    expect(await screen.findByRole('heading', { name: 'Importar equipamentos' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Exportar equipamentos' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Descarregar modelo' })).toBeInTheDocument()
    expect(document.querySelector('input[type="file"]')).toHaveAttribute('accept', '.xlsx')
  })
})

describe('Current Inventory import', () => {
  const ciRow = (rowNumber: number, overrides: Partial<CurrentInventoryImportRow> = {}): CurrentInventoryImportRow => ({
    rowNumber,
    status: 'Valid',
    equipmentType: 'Portátil',
    manufacturer: 'Lenovo',
    model: 'T14',
    serialNumber: `SN${rowNumber}`,
    willBeAssigned: true,
    fullName: 'Sofia Sample',
    nif: '123456789',
    workforceType: 'External',
    consultantAction: 'Create',
    allocationAction: 'Create',
    issues: [],
    ...overrides,
  })
  const ciPreview: CurrentInventoryImportPreviewResponse = {
    importSessionId: 'ci-1',
    fileName: 'inventario.xlsx',
    fileSha256: 'x',
    status: 'Pending',
    totalRows: 2,
    validRows: 2,
    warningRows: 0,
    errorRows: 0,
    createdAtUtc: '2026-10-10T10:00:00Z',
    rows: [ciRow(1), ciRow(2, { willBeAssigned: false, fullName: null, consultantAction: 'None', allocationAction: 'None' })],
    summary: {
      newConsultants: 1,
      reusedConsultants: 0,
      newAllocations: 1,
      reusedAllocations: 0,
      equipmentToCreate: 2,
      equipmentToAssign: 1,
      equipmentToStayAvailable: 1,
      internalConsultants: 0,
      externalConsultants: 1,
    },
  }

  it('keeps its own preview (planned outcome per row) and confirm endpoint', async () => {
    const fetchMock = render(<CurrentInventoryImportPage />, hrUser, {
      'POST /api/imports/current-inventory/preview': () => json(200, ciPreview),
      'POST /api/imports/current-inventory/ci-1/confirm': () =>
        json(200, {
          importSessionId: 'ci-1',
          status: 'Completed',
          createdConsultants: 1,
          reusedConsultants: 0,
          createdAllocations: 1,
          reusedAllocations: 0,
          createdEquipment: 2,
          createdAssignments: 1,
          completedAtUtc: '2026-10-10T10:01:00Z',
        }),
    })
    await upload(new File(['x'], 'inventario.xlsx'))

    const plan = await screen.findByRole('region', { name: 'Resultado previsto' })
    expect(within(plan).getByText('Serão atribuídos')).toBeInTheDocument()
    const table = within(screen.getByRole('region', { name: 'Linhas do ficheiro' })).getByRole('table')
    expect(within(table).getByText('Será atribuído')).toBeInTheDocument()
    expect(within(table).getByText('Ficará disponível')).toBeInTheDocument()
    expect(within(table).getByText('Sem pessoa (stock)')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Confirmar importação' }))
    expect(await screen.findByText(/2 equipamentos registados, 1 atribuição atual/)).toBeInTheDocument()
    expect(recordedRequests(fetchMock).filter((r) => r.method === 'POST').map((r) => r.url.pathname)).toEqual([
      '/api/imports/current-inventory/preview',
      '/api/imports/current-inventory/ci-1/confirm',
    ])
  })

  it('a stale preview (409) shows the re-validated rows and the server message', async () => {
    render(<CurrentInventoryImportPage />, hrUser, {
      'POST /api/imports/current-inventory/preview': () => json(200, ciPreview),
      'POST /api/imports/current-inventory/ci-1/confirm': () =>
        json(409, {
          title: 'Os dados mudaram desde a pré-visualização.',
          rows: [ciRow(1, { status: 'Error', issues: [{ code: 'E', field: 'Nif', message: 'NIF já em uso.', severity: 'Error' }] })],
        }),
    })
    await upload(new File(['x'], 'inventario.xlsx'))
    await userEvent.click(await screen.findByRole('button', { name: 'Confirmar importação' }))

    expect(await screen.findByText(/Os dados mudaram desde a pré-visualização\. Reveja as linhas assinaladas/)).toBeInTheDocument()
    expect(screen.getByText('NIF já em uso.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Confirmar importação' })).toBeDisabled()
  })
})
