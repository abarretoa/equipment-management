import { Route, Routes, useLocation } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { EquipmentDetailPage } from './EquipmentDetailPage'
import { mobileNumberLabel } from '../../branding/brand.config'
import { administrationUser, hrUser } from '../../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import { PHOTO_PRIVACY_HINT } from '../../shared/components/PhotoPrivacyHint'
import { Permissions } from '../../shared/permissions'
import type { CurrentUser } from '../auth/types'
import type { EquipmentDetail, EquipmentHistoryEvent } from './types'

const ID = 'eq-1'

const photo = (n: number) => ({
  id: `p${n}`,
  originalFileName: `frente-${n}.jpg`,
  contentType: 'image/jpeg',
  sizeBytes: 1000,
  sortOrder: n,
  uploadedAtUtc: '2026-10-02T10:00:00Z',
})

const laptop: EquipmentDetail = {
  id: ID,
  equipmentTypeId: 't-laptop',
  equipmentTypeName: 'Portátil',
  assetTag: 'EQ-NB-0412',
  manufacturer: 'Lenovo',
  model: 'ThinkPad T14 Gen 4',
  serialNumber: 'SNDEMO01',
  notes: 'Inclui carregador USB-C 65 W.',
  status: 'Available',
  createdAtUtc: '2024-03-12T10:00:00Z',
  updatedAtUtc: '2026-10-08T10:42:00Z',
  laptopSpecification: { processor: 'Intel Core i7-1365U', memory: '32 GB', storage: '1 TB SSD' },
  purchaseDate: '2024-03-12',
  invoiceNumber: 'FT 2024/1187',
  purchaseVendor: 'Fornecedor Exemplo, Lda.',
  currentPhotoSet: {
    id: 'set-1',
    source: 'Return',
    createdAtUtc: '2026-10-02T10:00:00Z',
    photos: [photo(1), photo(2), photo(3)],
  },
}

const custody = {
  consultantId: 'c-1',
  consultantFullName: 'Sofia Sample Example',
  workforceType: 'External' as const,
  companyName: 'Cliente Exemplo, S.A.',
  roleOrFunction: 'Consultora de Sistemas',
  assignedAtUtc: '2026-10-08T10:00:00Z',
  equipmentHandoverId: 'h-1',
  origin: 'Handover' as const,
}

const history: EquipmentHistoryEvent[] = [
  {
    eventType: 'Assigned',
    occurredAtUtc: '2026-10-08T10:00:00Z',
    consultantName: 'Sofia Sample Example',
    companyName: 'Cliente Exemplo, S.A.',
    role: 'Consultora de Sistemas',
    handoverId: 'h-1',
  },
  {
    eventType: 'ReturnInspectionCompleted',
    occurredAtUtc: '2026-10-02T10:00:00Z',
    condition: 'Good',
    returnId: 'r-9',
  },
  { eventType: 'Registered', occurredAtUtc: '2024-03-12T10:00:00Z' },
]

const withoutPermissions = (user: CurrentUser, ...removed: string[]): CurrentUser => ({
  ...user,
  permissions: user.permissions.filter((p) => !removed.includes(p)),
})

function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname}</output>
}

function renderDetail(
  equipment: EquipmentDetail,
  { user = administrationUser, handlers = {} } = {},
) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, user),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    [`GET /api/equipment/${ID}`]: () => json(200, equipment),
    [`GET /api/equipment/${ID}/history`]: () => json(200, history),
    ...Object.fromEntries(
      [1, 2, 3].map((n) => [
        `GET /api/equipment/${ID}/photos/p${n}/content`,
        () => new Response(new Blob(['x'], { type: 'image/jpeg' })),
      ]),
    ),
    ...handlers,
  })

  renderWithProviders(
    <>
      <Routes>
        <Route path="/equipamentos/:id" element={<EquipmentDetailPage />} />
        <Route path="*" element={<p>Outra página</p>} />
      </Routes>
      <LocationProbe />
    </>,
    { route: `/equipamentos/${ID}` },
  )
  return fetchMock
}

const findTitle = () => screen.findByRole('heading', { level: 1, name: 'EQ-NB-0412' })
const button = (name: string) => screen.queryByRole('button', { name })

beforeEach(() => {
  // jsdom has no object URLs; the gallery only needs a stable string.
  vi.stubGlobal(
    'URL',
    Object.assign(URL, { createObjectURL: () => 'blob:photo', revokeObjectURL: () => {} }),
  )
})
afterEach(() => {
  vi.unstubAllGlobals()
})

describe('EquipmentDetailPage — header and actions', () => {
  it('Available (Administração): Editar plus a restrained "Abater equipamento"', async () => {
    renderDetail(laptop)
    await findTitle()

    expect(screen.getByText('Disponível')).toBeInTheDocument()
    expect(screen.getByText('Portátil · Lenovo ThinkPad T14 Gen 4')).toBeInTheDocument()
    expect(button('Editar')).toBeInTheDocument()
    expect(button('Abater equipamento')).toBeInTheDocument()
    expect(button('Solicitar abate')).not.toBeInTheDocument()
    expect(button('Iniciar devolução')).not.toBeInTheDocument()
    expect(button('Iniciar manutenção')).not.toBeInTheDocument()
  })

  it('Available (RH): can request retirement but not retire', async () => {
    renderDetail(laptop, { user: hrUser })
    await findTitle()

    expect(button('Solicitar abate')).toBeInTheDocument()
    expect(button('Abater equipamento')).not.toBeInTheDocument()
  })

  it('Assigned: shows custody, its links, and "Iniciar devolução"', async () => {
    const user = userEvent.setup()
    renderDetail({ ...laptop, status: 'Assigned', currentCustody: custody })
    await findTitle()

    const panel = screen.getByRole('region', { name: 'Custódia atual' })
    expect(within(panel).getByText('Sofia Sample Example')).toBeInTheDocument()
    expect(within(panel).getByText('Externo')).toBeInTheDocument()
    expect(within(panel).getByText('Cliente Exemplo, S.A.')).toBeInTheDocument()
    expect(within(panel).getByText('Consultora de Sistemas')).toBeInTheDocument()
    expect(within(panel).getByText('Atribuído desde')).toBeInTheDocument()
    expect(within(panel).queryByText('Origem')).not.toBeInTheDocument()
    expect(button('Iniciar devolução')).toBeInTheDocument()
    expect(button('Abater equipamento')).not.toBeInTheDocument()

    await user.click(within(panel).getByRole('button', { name: 'Ver entrega' }))
    expect(screen.getByTestId('location')).toHaveTextContent('/entregas/h-1')
  })

  it('Assigned: "Ver consultor" needs Consultant.Read and "Ver entrega" needs a handover', async () => {
    renderDetail(
      {
        ...laptop,
        status: 'Assigned',
        currentCustody: { ...custody, equipmentHandoverId: null, origin: 'CurrentInventoryImport' },
      },
      { user: withoutPermissions(administrationUser, Permissions.Consultant.Read) },
    )
    await findTitle()

    const panel = screen.getByRole('region', { name: 'Custódia atual' })
    expect(within(panel).queryByRole('button', { name: 'Ver consultor' })).not.toBeInTheDocument()
    expect(within(panel).queryByRole('button', { name: 'Ver entrega' })).not.toBeInTheDocument()
    expect(within(panel).getByText('Importação do inventário atual')).toBeInTheDocument()
  })

  it('Iniciar devolução still resolves the active assignment and creates the return', async () => {
    const user = userEvent.setup()
    const fetchMock = renderDetail(
      { ...laptop, status: 'Assigned', currentCustody: custody },
      {
        handlers: {
          'GET /api/equipment-assignments': () =>
            json(200, { items: [{ id: 'as-1' }], page: 1, pageSize: 1, totalCount: 1 }),
          'POST /api/equipment-assignments/as-1/return': () => json(201, { id: 'ret-7' }),
        },
      },
    )
    await findTitle()

    await user.click(screen.getByRole('button', { name: 'Iniciar devolução' }))
    const dialog = await screen.findByRole('dialog', { name: 'Iniciar devolução' })
    await user.click(within(dialog).getByRole('button', { name: 'Iniciar devolução' }))

    await waitFor(() =>
      expect(screen.getByTestId('location')).toHaveTextContent('/devolucoes/ret-7'),
    )
    const assignmentUrl = fetchMock.mock.calls
      .map(([input]) =>
        typeof input === 'string' ? input : input instanceof URL ? input.href : input.url,
      )
      .find((url) => url.includes('/api/equipment-assignments?'))
    expect(assignmentUrl).toContain(`equipmentId=${ID}`)
    expect(assignmentUrl).toContain('status=Active')
  })

  it('Damaged: maintenance is the primary action; retirement follows the permission', async () => {
    renderDetail({ ...laptop, status: 'Damaged' })
    await findTitle()
    expect(button('Iniciar manutenção')).toBeInTheDocument()
    expect(button('Abater equipamento')).toBeInTheDocument()
  })

  it('Damaged (RH): maintenance and "Solicitar abate"', async () => {
    renderDetail({ ...laptop, status: 'Damaged' }, { user: hrUser })
    await findTitle()
    expect(button('Iniciar manutenção')).toBeInTheDocument()
    expect(button('Solicitar abate')).toBeInTheDocument()
    expect(button('Abater equipamento')).not.toBeInTheDocument()
  })

  it('InTransit: shows "Em trânsito", no custodian, and no retirement or return action', async () => {
    renderDetail({ ...laptop, status: 'InTransit', currentCustody: null })
    await findTitle()

    expect(screen.getAllByText('Em trânsito').length).toBeGreaterThan(0)
    const panel = screen.getByRole('region', { name: 'Custódia atual' })
    expect(
      within(panel).getByText('Em trânsito por transportadora. Sem atribuição até à confirmação da entrega.'),
    ).toBeInTheDocument()
    expect(button('Abater equipamento')).not.toBeInTheDocument()
    expect(button('Iniciar devolução')).not.toBeInTheDocument()
    expect(button('Iniciar manutenção')).not.toBeInTheDocument()
  })

  it('a state with no permitted action says so', async () => {
    renderDetail({ ...laptop, status: 'Retired' })
    await findTitle()
    expect(screen.getByText('Sem ações disponíveis para o estado atual.')).toBeInTheDocument()
  })

  it('shows a failed direct retirement inside the confirmation dialog', async () => {
    const user = userEvent.setup()
    renderDetail(laptop, {
      handlers: {
        [`POST /api/equipment/${ID}/retire`]: () =>
          json(409, { title: 'O equipamento tem uma devolução em curso.' }),
      },
    })
    await findTitle()

    await user.click(screen.getByRole('button', { name: 'Abater equipamento' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Abater equipamento' })
    await user.click(within(dialog).getByRole('button', { name: 'Abater' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'O equipamento tem uma devolução em curso.',
    )
  })
})

describe('EquipmentDetailPage — sections', () => {
  it('keeps the general details, notes and purchase information', async () => {
    renderDetail(laptop)
    await findTitle()

    for (const text of [
      'Tipo',
      'Fabricante',
      'Modelo',
      'Número de série',
      'SNDEMO01',
      'Registado em',
      'Última atualização',
      'Notas',
      'Inclui carregador USB-C 65 W.',
      'Data de compra',
      'FT 2024/1187',
      'Fornecedor Exemplo, Lda.',
    ]) {
      expect(screen.getByText(text)).toBeInTheDocument()
    }
  })

  it('shows the Laptop specification', async () => {
    renderDetail(laptop)
    await findTitle()

    const section = screen
      .getByRole('heading', { name: 'Especificações do portátil' })
      .closest('section')!
    expect(within(section).getByText('Intel Core i7-1365U')).toBeInTheDocument()
    expect(within(section).getByText('32 GB')).toBeInTheDocument()
    expect(within(section).getByText('1 TB SSD')).toBeInTheDocument()
  })

  it('shows the MobilePhone specification: IMEI, the mobile number and Número SIM', async () => {
    renderDetail({
      ...laptop,
      equipmentTypeName: 'Telemóvel',
      laptopSpecification: null,
      mobilePhoneSpecification: {
        imei: '356938035643809',
        mobileNumber: '912345678',
        simSerialNumber: '8935101234567890',
      },
    })
    await findTitle()

    const section = screen
      .getByRole('heading', { name: 'Especificações do telemóvel' })
      .closest('section')!
    expect(within(section).getByText('IMEI')).toBeInTheDocument()
    expect(within(section).getByText('356938035643809')).toBeInTheDocument()
    expect(within(section).getByText(mobileNumberLabel)).toBeInTheDocument()
    expect(within(section).getByText('912345678')).toBeInTheDocument()
    expect(within(section).getByText('Número SIM')).toBeInTheDocument()
    expect(within(section).getByText('8935101234567890')).toBeInTheDocument()
  })

  it('shows the three current photos with their date and origin', async () => {
    renderDetail(laptop)
    await findTitle()

    const section = screen.getByRole('heading', { name: 'Fotografias atuais' }).closest('section')!
    expect(within(section).getAllByRole('button', { name: /^Ampliar Fotografia/ })).toHaveLength(3)
    expect(
      within(section).getByRole('button', { name: 'Ampliar Fotografia 1 de 3 (frente-1.jpg)' }),
    ).toBeInTheDocument()
    expect(within(section).getByText(/Atualizadas em/)).toHaveTextContent(
      'Atualizadas em 02/10/2026',
    )
    expect(within(section).getByText('Devolução')).toBeInTheDocument()
  })

  it('opens a current photo in the shared viewer, loaded from the authenticated photo endpoint', async () => {
    const fetchMock = renderDetail(laptop)
    await findTitle()

    const section = screen.getByRole('heading', { name: 'Fotografias atuais' }).closest('section')!
    const thumbnail = within(section).getByRole('button', {
      name: 'Ampliar Fotografia 2 de 3 (frente-2.jpg)',
    })
    await userEvent.click(thumbnail)

    const viewer = screen.getByRole('dialog', { name: 'Fotografias atuais' })
    expect(
      await within(viewer).findByRole('img', { name: 'Fotografia 2 de 3 (frente-2.jpg)' }),
    ).toHaveAttribute('src', 'blob:photo')
    expect(within(viewer).getByText('2 de 3')).toBeInTheDocument()
    expect(recordedRequests(fetchMock).map((r) => r.url.pathname)).toContain(
      `/api/equipment/${ID}/photos/p2/content`,
    )

    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(thumbnail).toHaveFocus()
  })

  it('labels an initial-registration photo set as "Registo inicial"', async () => {
    renderDetail({
      ...laptop,
      currentPhotoSet: { ...laptop.currentPhotoSet!, source: 'InitialRegistration' },
    })
    await findTitle()
    expect(screen.getByText('Registo inicial')).toBeInTheDocument()
  })

  it('legacy equipment without photos: missing state, and "Registar fotografias" enforces exactly 3', async () => {
    const user = userEvent.setup()
    renderDetail({ ...laptop, currentPhotoSet: null })
    await findTitle()

    expect(screen.getByText('Fotografias atuais não registadas.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Registar fotografias' }))
    const dialog = await screen.findByRole('dialog', { name: 'Registar fotografias atuais' })
    expect(within(dialog).getByText(PHOTO_PRIVACY_HINT)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Guardar fotografias' }))
    expect(within(dialog).getByRole('alert')).toHaveTextContent(
      'São exigidas exatamente 3 fotografias (0/3 selecionadas).',
    )
  })

  it('hides "Registar fotografias" without Equipment.PhotosManage', async () => {
    renderDetail(
      { ...laptop, currentPhotoSet: null },
      { user: withoutPermissions(administrationUser, Permissions.Equipment.PhotosManage) },
    )
    await findTitle()
    expect(screen.getByText('Fotografias atuais não registadas.')).toBeInTheDocument()
    expect(button('Registar fotografias')).not.toBeInTheDocument()
  })

  it('has no photo section for equipment kinds without condition photos', async () => {
    renderDetail({
      ...laptop,
      laptopSpecification: null,
      currentPhotoSet: null,
      equipmentTypeName: 'Monitor',
    })
    await findTitle()
    expect(screen.queryByRole('heading', { name: 'Fotografias atuais' })).not.toBeInTheDocument()
  })

  it('keeps a pending retirement request visible and stops a second request', async () => {
    renderDetail(
      {
        ...laptop,
        pendingRetirementRequest: {
          id: 'rr-1',
          requestedByDisplayName: 'Ana Demo',
          requestedAtUtc: '2026-10-05T09:30:00Z',
          requestReason: 'Ecrã partido sem reparação viável.',
        },
      },
      { user: hrUser },
    )
    await findTitle()

    const section = screen.getByRole('region', { name: 'Abate solicitado' })
    expect(within(section).getByText('Ana Demo')).toBeInTheDocument()
    expect(within(section).getByText('Motivo')).toBeInTheDocument()
    expect(within(section).getByText('Ecrã partido sem reparação viável.')).toBeInTheDocument()
    expect(button('Solicitar abate')).not.toBeInTheDocument()
  })
})

describe('EquipmentDetailPage — Alterar fotografias', () => {
  const correctable: EquipmentDetail = { ...laptop, currentPhotosCorrectable: true }
  const REPLACE = `POST /api/equipment/${ID}/current-photos/replacement`
  const newPhoto = (n: number) => ({ ...photo(n), id: `n${n}`, originalFileName: `novo-${n}.jpg` })
  const corrected: EquipmentDetail = {
    ...correctable,
    currentPhotoSet: { id: 'set-2', source: 'Return', createdAtUtc: '2026-10-11T10:00:00Z', photos: [photo(1), newPhoto(2), photo(3)] },
  }

  async function openDialog(user: ReturnType<typeof userEvent.setup>) {
    await user.click(await screen.findByRole('button', { name: 'Alterar fotografias' }))
    return screen.findByRole('dialog', { name: 'Alterar fotografias atuais' })
  }

  async function replaceSlot2(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement) {
    await user.upload(
      within(dialog).getByLabelText('Escolher nova fotografia 2'),
      new File(['img'], 'nova.jpg', { type: 'image/jpeg' }),
    )
  }

  it('is offered to an eligible Administration user, beside the current photos, updated date and origin', async () => {
    renderDetail(correctable)
    await findTitle()

    expect(await screen.findByRole('button', { name: 'Alterar fotografias' })).toBeInTheDocument()
    expect(screen.getByText('Atualizadas em')).toBeInTheDocument()
    expect(screen.getByText(/Origem:/)).toBeInTheDocument()
    // Never a per-photo destructive action.
    expect(screen.queryByRole('button', { name: /Remover|Eliminar fotografia/ })).not.toBeInTheDocument()
  })

  it('is absent for HR (no Equipment.PhotosReplace), even when the equipment is correctable', async () => {
    renderDetail(correctable, { user: hrUser })
    await findTitle()
    expect(button('Alterar fotografias')).not.toBeInTheDocument()
  })

  it('is absent when the server says the equipment is not correctable (custody or dispatched delivery)', async () => {
    renderDetail({ ...laptop, status: 'Assigned', currentCustody: custody, currentPhotosCorrectable: false })
    await findTitle()
    expect(button('Alterar fotografias')).not.toBeInTheDocument()
  })

  it('shows the 3 current photos; a replacement previews and can be reverted; Cancelar sends nothing', async () => {
    const user = userEvent.setup()
    const fetchMock = renderDetail(correctable)
    await findTitle()
    const dialog = await openDialog(user)

    expect(within(dialog).getAllByRole('listitem')).toHaveLength(3)
    expect(within(dialog).getByText(PHOTO_PRIVACY_HINT)).toBeInTheDocument()
    expect(within(dialog).getByRole('img', { name: 'Fotografia 2 — atual' })).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Guardar alterações' })).toBeDisabled()

    await replaceSlot2(user, dialog)
    expect(within(dialog).getByRole('img', { name: 'Fotografia 2 — nova' })).toBeInTheDocument()
    expect(within(dialog).getByText('Fotografia 2: será substituída')).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Repor fotografia 2 atual' }))
    expect(within(dialog).getByRole('img', { name: 'Fotografia 2 — atual' })).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }))
    expect(recordedRequests(fetchMock).filter((r) => r.method !== 'GET')).toHaveLength(0)
  })

  it('confirms, then posts only the replaced slot with the expected set, and shows the new set', async () => {
    const user = userEvent.setup()
    let current = correctable
    const fetchMock = renderDetail(correctable, {
      handlers: {
        [`GET /api/equipment/${ID}`]: () => json(200, current),
        [`GET /api/equipment/${ID}/photos/n2/content`]: () => new Response(new Blob(['y'], { type: 'image/jpeg' })),
        [REPLACE]: () => {
          current = corrected
          return json(200, corrected)
        },
      },
    })
    await findTitle()
    const dialog = await openDialog(user)
    await replaceSlot2(user, dialog)
    await user.click(within(dialog).getByRole('button', { name: 'Guardar alterações' }))

    const confirm = await screen.findByRole('alertdialog', { name: 'Alterar fotografias atuais?' })
    expect(within(confirm).getByText(/Registos e documentos históricos continuarão associados/)).toBeInTheDocument()
    expect(within(confirm).getByRole('button', { name: 'Cancelar' })).toHaveFocus()
    await user.click(within(confirm).getByRole('button', { name: 'Guardar fotografias' }))

    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
    const post = recordedRequests(fetchMock).find((r) => r.method === 'POST')!
    expect(post.url.pathname).toBe(`/api/equipment/${ID}/current-photos/replacement`)
    const [, init] = fetchMock.mock.calls.find(([, i]) => i?.method === 'POST')!
    const form = init!.body as FormData
    expect(form.get('ExpectedPhotoSetId')).toBe('set-1')
    expect(form.get('Photo2')).toBeInstanceOf(File)
    expect(form.get('Photo1')).toBeNull()
    expect(form.get('Photo3')).toBeNull()

    // The refreshed detail shows (and the viewer loads) the new set.
    await waitFor(() =>
      expect(recordedRequests(fetchMock).some((r) => r.url.pathname === `/api/equipment/${ID}/photos/n2/content`)).toBe(true),
    )
    await user.click(screen.getByRole('button', { name: 'Ampliar Fotografia 2 de 3 (novo-2.jpg)' }))
    expect(await screen.findByRole('dialog', { name: /Fotografias atuais/ })).toBeInTheDocument()
  })

  it('shows a server conflict in the confirmation and keeps the old set on the page', async () => {
    const user = userEvent.setup()
    renderDetail(correctable, {
      handlers: {
        [REPLACE]: () => json(409, { title: 'Este equipamento está atribuído a um consultor. As fotografias atuais só podem ser alteradas fora de custódia.' }),
      },
    })
    await findTitle()
    const dialog = await openDialog(user)
    await replaceSlot2(user, dialog)
    await user.click(within(dialog).getByRole('button', { name: 'Guardar alterações' }))
    const confirm = await screen.findByRole('alertdialog', { name: 'Alterar fotografias atuais?' })
    await user.click(within(confirm).getByRole('button', { name: 'Guardar fotografias' }))

    expect(await within(confirm).findByRole('alert')).toHaveTextContent('fora de custódia')
    // The page behind the dialog still shows the old set (aria-hidden while the dialog is open).
    expect(screen.getByRole('button', { name: 'Ampliar Fotografia 2 de 3 (frente-2.jpg)', hidden: true })).toBeInTheDocument()
  })
})

describe('EquipmentDetailPage — Registar fotografias confirmation', () => {
  it('asks for confirmation before saving the first current set', async () => {
    const user = userEvent.setup()
    const fetchMock = renderDetail(
      { ...laptop, currentPhotoSet: null },
      { handlers: { [`POST /api/equipment/${ID}/photos`]: () => json(200, laptop) } },
    )
    await findTitle()
    await user.click(screen.getByRole('button', { name: 'Registar fotografias' }))
    const dialog = await screen.findByRole('dialog', { name: 'Registar fotografias atuais' })
    expect(within(dialog).getByText(PHOTO_PRIVACY_HINT)).toBeInTheDocument()
    const input = dialog.querySelector<HTMLInputElement>('input[type="file"]')!
    await user.upload(input, [1, 2, 3].map((n) => new File(['img'], `f${n}.jpg`, { type: 'image/jpeg' })))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar fotografias' }))

    const confirm = await screen.findByRole('alertdialog', { name: 'Guardar fotografias atuais?' })
    expect(within(confirm).getByText('Estas 3 fotografias serão registadas como o estado atual do equipamento.')).toBeInTheDocument()
    expect(recordedRequests(fetchMock).filter((r) => r.method !== 'GET')).toHaveLength(0)

    await user.click(within(confirm).getByRole('button', { name: 'Guardar fotografias' }))
    await waitFor(() =>
      expect(recordedRequests(fetchMock).filter((r) => r.method === 'POST').map((r) => r.url.pathname)).toEqual([
        `/api/equipment/${ID}/photos`,
      ]),
    )
  })
})

describe('EquipmentDetailPage — history', () => {
  it('links Entrega and Devolução events when permitted', async () => {
    renderDetail(laptop)

    expect(
      await screen.findByRole('link', { name: 'Atribuído a Sofia Sample Example' }),
    ).toHaveAttribute('href', '/entregas/h-1')
    expect(screen.getByRole('link', { name: 'Inspeção de devolução concluída' })).toHaveAttribute(
      'href',
      '/devolucoes/r-9',
    )
    expect(screen.getByText('Equipamento registado')).toBeInTheDocument()
  })

  it('renders events as plain text without the read permission', async () => {
    renderDetail(laptop, {
      user: withoutPermissions(
        administrationUser,
        Permissions.EquipmentHandover.Read,
        Permissions.EquipmentReturn.Read,
      ),
    })

    expect(await screen.findByText('Atribuído a Sofia Sample Example')).toBeInTheDocument()
    expect(
      screen.queryByRole('link', { name: 'Atribuído a Sofia Sample Example' }),
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('link', { name: 'Inspeção de devolução concluída' }),
    ).not.toBeInTheDocument()
  })
})
