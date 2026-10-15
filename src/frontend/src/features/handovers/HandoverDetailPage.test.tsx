import { Route, Routes, useLocation } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { QueryClient } from '@tanstack/react-query'
import { HandoverDetailPage } from './HandoverDetailPage'
import { HandoverCreatePage } from './HandoverCreatePage'
import { administrationUser } from '../../test/fixtures'
import { Permissions } from '../../shared/permissions'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import type { HandoverDetail, HandoverItem } from './types'
import type { ResponsibilityTerm } from '../responsibility-terms/types'

const ID = 'h-1'
const BASE = `/api/equipment-handovers/${ID}`

const laptopItem: HandoverItem = {
  id: 'i-1',
  equipmentId: 'eq-1',
  assetTag: 'EQ-NB-0412',
  equipmentTypeName: 'Portátil',
  manufacturer: 'Lenovo',
  model: 'ThinkPad T14',
  serialNumber: 'SNDEMO01',
  inspection: {
    id: 'ins-1',
    condition: 'Good',
    notes: null,
    inspectedByDisplayNameSnapshot: 'Ana Demo',
    inspectedAtUtc: '2026-10-08T09:00:00Z',
    photos: [],
  },
}

const phoneItem: HandoverItem = {
  ...laptopItem,
  id: 'i-2',
  equipmentId: 'eq-2',
  assetTag: 'EQ-TM-0077',
  equipmentTypeName: 'Telemóvel',
  manufacturer: 'Samsung',
  model: 'Galaxy A55',
  serialNumber: null,
}

const draft: HandoverDetail = {
  id: ID,
  consultantId: 'c-1',
  consultantFullName: 'Sofia Sample Example',
  status: 'Draft',
  operationalStatus: 'Draft',
  deliveryMethod: 'InPerson',
  deliveredOn: '2026-10-08',
  notes: 'Entregar com carregador.',
  createdByDisplayNameSnapshot: 'Ana Demo',
  createdAtUtc: '2026-10-08T08:00:00Z',
  updatedAtUtc: '2026-10-08T09:00:00Z',
  finalizedAtUtc: null,
  items: [laptopItem, phoneItem],
  dispatch: null,
}

const finalized: HandoverDetail = {
  ...draft,
  status: 'Finalized',
  operationalStatus: 'AwaitingSignedTerm',
  finalizedAtUtc: '2026-10-08T15:00:00Z',
}

const term = (overrides: Partial<ResponsibilityTerm>): ResponsibilityTerm => ({
  id: 't-2',
  equipmentHandoverId: ID,
  documentNumber: 'TR-2026-0042',
  version: 2,
  status: 'Generated',
  templateVersion: 1,
  generatedAtUtc: '2026-10-09T10:00:00Z',
  generatedByDisplayNameSnapshot: 'Eva Example',
  fileName: 'TR-2026-0042.pdf',
  sizeBytes: 48_000,
  sha256: 'abc',
  createdAtUtc: '2026-10-09T10:00:00Z',
  executionStatus: 'NotSent',
  sendAttemptCount: 0,
  signature: null,
  ...overrides,
})

const currentTerm = term({})
const supersededTerm = term({
  id: 't-1',
  version: 1,
  status: 'Superseded',
  generatedAtUtc: '2026-10-08T16:00:00Z',
  // A signature on an older version never stands in for the current one.
  signature: {
    signedOn: '2026-10-08',
    uploadedAtUtc: '2026-10-08T17:00:00Z',
    sizeBytes: 50_000,
    sha256: 'old',
    uploadedByDisplayNameSnapshot: 'Ana Demo',
  },
})

function LocationProbe() {
  const location = useLocation()
  return <span data-testid="location">{location.pathname}</span>
}

function renderDetail(
  handover: HandoverDetail,
  { terms = [], handlers = {} }: { terms?: ResponsibilityTerm[]; handlers?: Parameters<typeof stubApi>[0] } = {},
) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, administrationUser),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    [`GET ${BASE}`]: () => json(200, handover),
    [`GET ${BASE}/responsibility-terms`]: () => json(200, terms),
    ...handlers,
  })

  renderWithProviders(
    <>
      <Routes>
        <Route path="/entregas/:id" element={<HandoverDetailPage />} />
        <Route path="*" element={<p>Outra página</p>} />
      </Routes>
      <LocationProbe />
    </>,
    { route: `/entregas/${ID}` },
  )
  return fetchMock
}

const findTitle = () => screen.findByRole('heading', { level: 1, name: 'Entrega: Sofia Sample Example' })
const mutations = (fetchMock: ReturnType<typeof stubApi>) =>
  recordedRequests(fetchMock).filter((r) => r.method !== 'GET')

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('HandoverDetailPage — Draft', () => {
  it('shows the draft state, the real equipment and the next operational action', async () => {
    renderDetail(draft)
    await findTitle()

    expect(screen.getByText('Rascunho')).toBeInTheDocument()
    const items = screen.getByRole('list', { name: 'Equipamentos da entrega' })
    expect(within(items).getByRole('link', { name: 'EQ-NB-0412' })).toHaveAttribute('href', '/equipamentos/eq-1')
    expect(within(items).getByRole('link', { name: 'EQ-TM-0077' })).toHaveAttribute('href', '/equipamentos/eq-2')
    expect(screen.getByRole('button', { name: 'Confirmar entrega' })).toBeEnabled()
    // The Responsibility Term only exists once the delivery is confirmed.
    expect(screen.queryByRole('heading', { name: 'Termo de Responsabilidade' })).not.toBeInTheDocument()
  })

  it('has no condition-photo upload anywhere in the draft', async () => {
    renderDetail(draft)
    await findTitle()
    expect(document.querySelector('input[type="file"]')).toBeNull()
    expect(screen.queryByText(/Arraste um ficheiro/)).not.toBeInTheDocument()
  })

  it('adds equipment through the existing items endpoint — picking only Available equipment, reserving nothing', async () => {
    const fetchMock = renderDetail(draft, {
      handlers: {
        'GET /api/equipment': () =>
          json(200, {
            items: [{ id: 'eq-9', assetTag: 'EQ-NB-0500', equipmentTypeName: 'Portátil', manufacturer: 'Dell', model: 'Latitude', status: 'Available' }],
            page: 1,
            pageSize: 20,
            totalCount: 1,
          }),
        [`POST ${BASE}/items`]: () => json(200, draft),
      },
    })
    await findTitle()

    await userEvent.click(screen.getByRole('button', { name: 'Adicionar equipamento' }))
    const dialog = await screen.findByRole('dialog', { name: 'Adicionar equipamento' })
    await userEvent.click(await within(dialog).findByRole('button', { name: 'Adicionar EQ-NB-0500' }))

    await waitFor(() => expect(mutations(fetchMock)).toHaveLength(1))
    const [add] = mutations(fetchMock)
    expect(add?.url.pathname).toBe(`${BASE}/items`)
    expect(add?.body).toEqual({ equipmentId: 'eq-9' })
    const picker = recordedRequests(fetchMock).find((r) => r.url.pathname === '/api/equipment')
    expect(picker?.url.searchParams.get('status')).toBe('Available')
  })

  it('shows a rejected add inside the dialog', async () => {
    renderDetail(draft, {
      handlers: {
        'GET /api/equipment': () =>
          json(200, {
            items: [{ id: 'eq-9', assetTag: 'EQ-NB-0500', equipmentTypeName: 'Portátil', status: 'Available' }],
            page: 1,
            pageSize: 20,
            totalCount: 1,
          }),
        [`POST ${BASE}/items`]: () => json(409, { title: 'O equipamento já não está disponível.' }),
      },
    })
    await findTitle()
    await userEvent.click(screen.getByRole('button', { name: 'Adicionar equipamento' }))
    const dialog = await screen.findByRole('dialog', { name: 'Adicionar equipamento' })
    await userEvent.click(await within(dialog).findByRole('button', { name: 'Adicionar EQ-NB-0500' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('O equipamento já não está disponível.')
  })

  it('removes equipment — even after its inspection was saved — through a confirmation and the existing DELETE endpoint', async () => {
    // Both draft items already have a saved delivery inspection.
    const fetchMock = renderDetail(draft, {
      handlers: { [`DELETE ${BASE}/items/i-2`]: () => new Response(null, { status: 204 }) },
    })
    await findTitle()

    const remove = screen.getByRole('button', { name: 'Remover equipamento EQ-TM-0077' })
    expect(remove).toHaveTextContent('Remover equipamento')
    await userEvent.click(remove)

    const dialog = await screen.findByRole('alertdialog', { name: 'Remover equipamento da entrega?' })
    expect(dialog).toHaveTextContent('O equipamento EQ-TM-0077 será removido desta entrega.')
    expect(dialog).toHaveTextContent('A inspeção registada para esta entrega deixará de ser utilizada.')
    expect(dialog).toHaveTextContent('O equipamento continuará disponível para uma nova entrega.')
    // Nothing is removed until confirmed; Cancelar is the initial focus.
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toHaveFocus()
    expect(mutations(fetchMock)).toEqual([])

    await userEvent.click(within(dialog).getByRole('button', { name: 'Remover equipamento' }))
    await waitFor(() => expect(mutations(fetchMock).map((r) => `${r.method} ${r.url.pathname}`)).toEqual([`DELETE ${BASE}/items/i-2`]))
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
  })

  it('cancelling the removal sends nothing', async () => {
    const fetchMock = renderDetail(draft)
    await findTitle()

    await userEvent.click(screen.getByRole('button', { name: 'Remover equipamento EQ-NB-0412' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Remover equipamento da entrega?' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancelar' }))

    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
    expect(mutations(fetchMock)).toEqual([])
  })

  it('shows a rejected removal inside the confirmation dialog', async () => {
    renderDetail(draft, {
      handlers: { [`DELETE ${BASE}/items/i-2`]: () => json(409, { title: 'A entrega já não está em preparação.' }) },
    })
    await findTitle()

    await userEvent.click(screen.getByRole('button', { name: 'Remover equipamento EQ-TM-0077' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Remover equipamento da entrega?' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Remover equipamento' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('A entrega já não está em preparação.')
  })

  it('omits the inspection sentence when the item has no saved inspection', async () => {
    renderDetail({ ...draft, items: [{ ...phoneItem, inspection: null }] })
    await findTitle()

    await userEvent.click(screen.getByRole('button', { name: 'Remover equipamento EQ-TM-0077' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Remover equipamento da entrega?' })
    expect(dialog).not.toHaveTextContent('A inspeção registada')
  })

  it('confirms the delivery with a single finalize call', async () => {
    const fetchMock = renderDetail(draft, { handlers: { [`POST ${BASE}/finalize`]: () => json(200, finalized) } })
    await findTitle()

    await userEvent.click(screen.getByRole('button', { name: 'Confirmar entrega' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Confirmar entrega' })
    // Cancel first: the confirming action is never the initial focus.
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toHaveFocus()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirmar entrega' }))

    await waitFor(() => expect(mutations(fetchMock).map((r) => `${r.method} ${r.url.pathname}`)).toEqual([`POST ${BASE}/finalize`]))
  })

  it('shows the full MissingCurrentPhotoSet message inside the dialog — no link derived from its text, never an upload', async () => {
    const message =
      'Não é possível confirmar esta entrega.\nO equipamento EQ-NB-0412 não tem fotografias atuais registadas. Registe as 3 fotografias do equipamento antes de confirmar a entrega.'
    renderDetail(draft, { handlers: { [`POST ${BASE}/finalize`]: () => json(409, { title: message }) } })
    await findTitle()

    await userEvent.click(screen.getByRole('button', { name: 'Confirmar entrega' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Confirmar entrega' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirmar entrega' }))

    const alert = await within(dialog).findByRole('alert')
    // The backend message is shown whole and unchanged (line break preserved).
    expect(alert.querySelector('p')?.textContent).toBe(message)
    // The contract carries no equipment id, so no navigation is manufactured from the text.
    expect(within(dialog).queryByRole('link')).not.toBeInTheDocument()
    expect(document.querySelector('input[type="file"]')).toBeNull()
  })

  it('Carrier before dispatch: "Registar envio" is the next step and posts the expected dates', async () => {
    const carrier: HandoverDetail = { ...draft, deliveryMethod: 'Carrier' }
    const fetchMock = renderDetail(carrier, {
      handlers: {
        [`POST ${BASE}/dispatch`]: () =>
          json(200, {
            id: 'd-1',
            equipmentHandoverId: ID,
            expectedDeliveryFromDate: '2026-10-12',
            dispatchedAtUtc: '2026-10-09T10:00:00Z',
            dispatchedByDisplayNameSnapshot: 'Ana Demo',
          }),
      },
    })
    await findTitle()

    expect(screen.queryByRole('button', { name: 'Confirmar entrega' })).not.toBeInTheDocument()
    expect(screen.getByText('Registe o envio antes de confirmar a entrega.')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Envio' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Registar envio' }))
    const dialog = await screen.findByRole('dialog', { name: 'Registar envio' })
    await userEvent.type(within(dialog).getByLabelText(/Previsão de entrega/), '2026-10-12')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Registar envio' }))

    await waitFor(() => expect(mutations(fetchMock)).toHaveLength(1))
    expect(mutations(fetchMock)[0]?.url.pathname).toBe(`${BASE}/dispatch`)
    expect(mutations(fetchMock)[0]?.body).toEqual({ expectedDeliveryFromDate: '2026-10-12' })
  })

  it('keeps an item another delivery shipped, marks it unavailable and blocks the confirmation', async () => {
    renderDetail({ ...draft, items: [laptopItem, { ...phoneItem, equipmentStatus: 'InTransit' }] })
    await findTitle()

    const items = screen.getByRole('list', { name: 'Equipamentos da entrega' })
    expect(within(items).getAllByRole('listitem')).toHaveLength(2)
    expect(within(items).getByText('Já não disponível para esta entrega.')).toBeInTheDocument()
    expect(within(items).getByText('Em trânsito')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Confirmar entrega' })).toBeDisabled()
    expect(screen.getByText('Há equipamentos que já não estão disponíveis para esta entrega.')).toBeInTheDocument()
  })

  it('Carrier before dispatch: an item no longer Available blocks "Registar envio"', async () => {
    renderDetail({
      ...draft,
      deliveryMethod: 'Carrier',
      items: [{ ...laptopItem, equipmentStatus: 'Assigned' }, { ...phoneItem, equipmentStatus: 'Available' }],
    })
    await findTitle()

    expect(screen.getByRole('button', { name: 'Registar envio' })).toBeDisabled()
    expect(screen.getByText('Há equipamentos que já não estão disponíveis para esta entrega.')).toBeInTheDocument()
    expect(screen.getAllByText('Já não disponível para esta entrega.')).toHaveLength(1)
  })

  it('Carrier after dispatch: its own InTransit items are not flagged', async () => {
    renderDetail({
      ...draft,
      deliveryMethod: 'Carrier',
      items: [{ ...laptopItem, equipmentStatus: 'InTransit' }, { ...phoneItem, equipmentStatus: 'InTransit' }],
      dispatch: {
        id: 'd-1',
        equipmentHandoverId: ID,
        expectedDeliveryFromDate: '2026-10-12',
        dispatchedAtUtc: '2026-10-09T10:00:00Z',
        dispatchedByDisplayNameSnapshot: 'Ana Demo',
      },
    })
    await findTitle()

    expect(screen.queryByText('Já não disponível para esta entrega.')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Confirmar entrega' })).toBeEnabled()
  })

  it('Carrier after dispatch: composition is locked and delivery can be confirmed', async () => {
    renderDetail({
      ...draft,
      deliveryMethod: 'Carrier',
      dispatch: {
        id: 'd-1',
        equipmentHandoverId: ID,
        expectedDeliveryFromDate: '2026-10-12',
        expectedDeliveryToDate: '2026-10-14',
        dispatchedAtUtc: '2026-10-09T10:00:00Z',
        dispatchedByDisplayNameSnapshot: 'Ana Demo',
      },
    })
    await findTitle()

    expect(screen.queryByRole('button', { name: 'Adicionar equipamento' })).not.toBeInTheDocument()
    // Carrier: the composition locks at Registar envio — even with saved inspections, no removal.
    expect(screen.queryByRole('button', { name: /^Remover / })).not.toBeInTheDocument()
    expect(screen.getByText('12/10/2026 a 14/10/2026')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Confirmar entrega' })).toBeEnabled()
  })
})

describe('HandoverDetailPage — Eliminar rascunho', () => {
  const dispatched: HandoverDetail = {
    ...draft,
    deliveryMethod: 'Carrier',
    dispatch: {
      id: 'd-1',
      equipmentHandoverId: ID,
      expectedDeliveryFromDate: '2026-10-12',
      expectedDeliveryToDate: null,
      dispatchedAtUtc: '2026-10-09T10:00:00Z',
      dispatchedByDisplayNameSnapshot: 'Ana Demo',
    },
  }

  it('offers a restrained destructive action on an eligible Draft, behind a confirmation', async () => {
    const fetchMock = renderDetail(draft)
    await findTitle()

    const action = screen.getByRole('button', { name: 'Eliminar rascunho' })
    // Danger-outline, never the filled primary: it must not compete with the next step.
    expect(action.className).toContain('text-danger')
    expect(action.className).not.toContain('bg-primary')
    await userEvent.click(action)

    const dialog = await screen.findByRole('alertdialog', { name: 'Eliminar rascunho?' })
    expect(dialog).toHaveTextContent('Esta entrega em rascunho será eliminada.')
    expect(dialog).toHaveTextContent('Os equipamentos incluídos continuarão disponíveis para outras entregas.')
    expect(dialog).toHaveTextContent('Esta ação não pode ser anulada.')
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toHaveFocus()
    expect(mutations(fetchMock)).toEqual([])
  })

  it('Cancelar closes the dialog and sends nothing', async () => {
    const fetchMock = renderDetail(draft)
    await findTitle()

    await userEvent.click(screen.getByRole('button', { name: 'Eliminar rascunho' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Eliminar rascunho?' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancelar' }))

    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
    expect(mutations(fetchMock)).toEqual([])
    expect(screen.getByTestId('location')).toHaveTextContent(`/entregas/${ID}`)
  })

  it('confirming deletes through DELETE, refreshes the lists and returns to /entregas', async () => {
    const invalidate = vi.spyOn(QueryClient.prototype, 'invalidateQueries')
    const remove = vi.spyOn(QueryClient.prototype, 'removeQueries')
    const fetchMock = renderDetail(draft, {
      handlers: { [`DELETE ${BASE}`]: () => new Response(null, { status: 204 }) },
    })
    await findTitle()

    await userEvent.click(screen.getByRole('button', { name: 'Eliminar rascunho' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Eliminar rascunho?' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Eliminar rascunho' }))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(/^\/entregas$/))
    expect(mutations(fetchMock).map((r) => `${r.method} ${r.url.pathname}`)).toEqual([`DELETE ${BASE}`])
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['handovers'] })
    expect(remove).toHaveBeenCalledWith({ queryKey: ['handover', ID] })
    invalidate.mockRestore()
    remove.mockRestore()
  })

  it('shows a server rejection inside the dialog and stays on the detail', async () => {
    renderDetail(draft, {
      handlers: {
        [`DELETE ${BASE}`]: () => json(409, { title: 'Esta entrega já tem um envio registado e não pode ser eliminada.' }),
      },
    })
    await findTitle()

    await userEvent.click(screen.getByRole('button', { name: 'Eliminar rascunho' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Eliminar rascunho?' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Eliminar rascunho' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Esta entrega já tem um envio registado e não pode ser eliminada.',
    )
    expect(screen.getByTestId('location')).toHaveTextContent(`/entregas/${ID}`)
  })

  it('is absent once the carrier dispatch is registered', async () => {
    renderDetail(dispatched)
    await findTitle()

    expect(screen.getByRole('button', { name: 'Confirmar entrega' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Eliminar rascunho' })).not.toBeInTheDocument()
  })

  it('is absent once the delivery is confirmed', async () => {
    renderDetail(finalized)
    await findTitle()

    expect(screen.queryByRole('button', { name: 'Eliminar rascunho' })).not.toBeInTheDocument()
  })

  it('is absent without permission to edit handovers', async () => {
    renderDetail(draft, {
      handlers: {
        'GET /api/auth/me': () =>
          json(200, { ...administrationUser, permissions: [Permissions.EquipmentHandover.Read, Permissions.Equipment.Read] }),
      },
    })
    await findTitle()

    expect(screen.queryByRole('button', { name: 'Eliminar rascunho' })).not.toBeInTheDocument()
  })
})

describe('HandoverDetailPage — Anular envio', () => {
  const dispatched: HandoverDetail = {
    ...draft,
    deliveryMethod: 'Carrier',
    items: [{ ...laptopItem, equipmentStatus: 'InTransit' }, { ...phoneItem, equipmentStatus: 'InTransit' }],
    dispatch: {
      id: 'd-1',
      equipmentHandoverId: ID,
      expectedDeliveryFromDate: '2026-10-12',
      dispatchedAtUtc: '2026-10-09T10:00:00Z',
      dispatchedByDisplayNameSnapshot: 'Ana Demo',
    },
  }

  it('is offered as a restrained action on an active carrier dispatch and requires a reason', async () => {
    const fetchMock = renderDetail(dispatched, { handlers: { [`POST ${BASE}/dispatch/cancel`]: () => new Response(null, { status: 204 }) } })
    await findTitle()

    await userEvent.click(screen.getByRole('button', { name: 'Anular envio' }))
    const dialog = await screen.findByRole('dialog', { name: 'Anular envio?' })
    expect(
      within(dialog).getByText(
        'Os equipamentos voltarão a ficar disponíveis e poderão ser utilizados noutra entrega. A anulação ficará registada no histórico.',
      ),
    ).toBeInTheDocument()
    const confirm = within(dialog).getByRole('button', { name: 'Anular envio' })
    expect(confirm).toBeDisabled()

    await userEvent.type(within(dialog).getByLabelText(/Motivo da anulação/), '   ')
    expect(confirm).toBeDisabled()
    await userEvent.type(within(dialog).getByLabelText(/Motivo da anulação/), 'Transportadora falhou  ')
    await userEvent.click(confirm)

    await waitFor(() => expect(mutations(fetchMock)).toHaveLength(1))
    expect(mutations(fetchMock)[0]?.url.pathname).toBe(`${BASE}/dispatch/cancel`)
    expect(mutations(fetchMock)[0]?.body).toEqual({ reason: 'Transportadora falhou' })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Anular envio?' })).not.toBeInTheDocument())
  })

  it('"Voltar" closes the dialog and sends nothing', async () => {
    const fetchMock = renderDetail(dispatched)
    await findTitle()

    await userEvent.click(screen.getByRole('button', { name: 'Anular envio' }))
    const dialog = await screen.findByRole('dialog', { name: 'Anular envio?' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Voltar' }))

    expect(screen.queryByRole('dialog', { name: 'Anular envio?' })).not.toBeInTheDocument()
    expect(mutations(fetchMock)).toHaveLength(0)
  })

  it('shows a server rejection inside the dialog', async () => {
    renderDetail(dispatched, {
      handlers: {
        [`POST ${BASE}/dispatch/cancel`]: () =>
          json(409, { title: 'A entrega já foi confirmada e o envio já não pode ser anulado.', status: 409 }),
      },
    })
    await findTitle()

    await userEvent.click(screen.getByRole('button', { name: 'Anular envio' }))
    const dialog = await screen.findByRole('dialog', { name: 'Anular envio?' })
    await userEvent.type(within(dialog).getByLabelText(/Motivo da anulação/), 'Motivo')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Anular envio' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('A entrega já foi confirmada e o envio já não pode ser anulado.')
  })

  it('is absent on a carrier Draft before dispatch', async () => {
    renderDetail({ ...draft, deliveryMethod: 'Carrier' })
    await findTitle()
    expect(screen.queryByRole('button', { name: 'Anular envio' })).not.toBeInTheDocument()
  })

  it('is absent on an in-person Draft', async () => {
    renderDetail({ ...draft, deliveryMethod: 'InPerson' })
    await findTitle()
    expect(screen.queryByRole('button', { name: 'Anular envio' })).not.toBeInTheDocument()
  })

  it('is absent without EquipmentHandover.CancelDispatch', async () => {
    renderDetail(dispatched, {
      handlers: {
        'GET /api/auth/me': () =>
          json(200, {
            ...administrationUser,
            permissions: administrationUser.permissions.filter((p) => p !== Permissions.EquipmentHandover.CancelDispatch),
          }),
      },
    })
    await findTitle()
    expect(screen.queryByRole('button', { name: 'Anular envio' })).not.toBeInTheDocument()
  })

  it('is absent once the delivery is confirmed', async () => {
    renderDetail({ ...finalized, deliveryMethod: 'Carrier', dispatch: dispatched.dispatch })
    await findTitle()
    expect(screen.queryByRole('button', { name: 'Anular envio' })).not.toBeInTheDocument()
  })

  it('lists cancelled attempts as history, rendering the reason as plain text, and offers Registar envio again', async () => {
    renderDetail({
      ...draft,
      deliveryMethod: 'Carrier',
      dispatch: null,
      cancelledDispatches: [
        {
          id: 'd-0',
          dispatchedAtUtc: '2026-10-08T10:00:00Z',
          dispatchedByDisplayNameSnapshot: 'Ana Demo',
          cancelledAtUtc: '2026-10-09T09:00:00Z',
          cancelledByDisplayNameSnapshot: 'Nina Example',
          cancellationReason: '<b>Morada errada</b>',
        },
      ],
    })
    await findTitle()

    const history = screen.getByRole('list', { name: 'Envios anulados' })
    expect(within(history).getByText(/Nina Example/)).toBeInTheDocument()
    expect(within(history).getByText('Motivo: <b>Morada errada</b>')).toBeInTheDocument()
    expect(history.querySelector('b')).toBeNull()
    expect(screen.getByRole('button', { name: 'Registar envio' })).toBeEnabled()
    expect(screen.queryByRole('button', { name: 'Anular envio' })).not.toBeInTheDocument()
  })
})

describe('HandoverDetailPage — historical evidence photos', () => {
  it('opens historical delivery evidence read-only in the shared viewer', async () => {
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: () => 'blob:photo', revokeObjectURL: () => {} }))
    const evidence = (id: string, name: string) => ({ id, originalFileName: name, sizeBytes: 2048, uploadedAtUtc: '2026-10-08T09:00:00Z' })
    const withEvidence: HandoverDetail = {
      ...finalized,
      items: [
        { ...laptopItem, inspection: { ...laptopItem.inspection!, photos: [evidence('p-1', 'frente.jpg'), evidence('p-2', 'verso.jpg')] } },
        phoneItem,
      ],
    }
    const photo = () => new Response(new Blob(['x'], { type: 'image/jpeg' }))
    const fetchMock = renderDetail(withEvidence, {
      handlers: {
        [`GET ${BASE}/items/i-1/inspection/photos/p-1/content`]: photo,
        [`GET ${BASE}/items/i-1/inspection/photos/p-2/content`]: photo,
      },
    })
    await findTitle()

    // Historical evidence stays read-only: viewable, never removable or replaceable.
    expect(screen.queryByRole('button', { name: /^Remover fotografia/ })).not.toBeInTheDocument()
    const thumbnail = screen.getByRole('button', { name: 'Ampliar Fotografia 1 de 2 (frente.jpg)' })
    await userEvent.click(thumbnail)

    const viewer = screen.getByRole('dialog', { name: 'Evidência da entrega · EQ-NB-0412' })
    expect(await within(viewer).findByRole('img', { name: 'Fotografia 1 de 2 (frente.jpg)' })).toBeInTheDocument()
    await userEvent.click(within(viewer).getByRole('button', { name: 'Fotografia seguinte' }))
    expect(await within(viewer).findByRole('img', { name: 'Fotografia 2 de 2 (verso.jpg)' })).toBeInTheDocument()
    expect(within(viewer).getByText('2 de 2')).toBeInTheDocument()

    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(thumbnail).toHaveFocus()
    expect(mutations(fetchMock)).toEqual([])
  })
})

describe('HandoverDetailPage — carrier term prepared after dispatch', () => {
  const dispatch = {
    id: 'd-1',
    equipmentHandoverId: ID,
    expectedDeliveryFromDate: '2026-10-12',
    expectedDeliveryToDate: null,
    dispatchedAtUtc: '2026-10-09T10:00:00Z',
    dispatchedByDisplayNameSnapshot: 'Ana Demo',
  }
  const dispatchedCarrier: HandoverDetail = { ...draft, deliveryMethod: 'Carrier', dispatch }
  const finalizedCarrier: HandoverDetail = { ...finalized, deliveryMethod: 'Carrier', dispatch }
  const preparedTerm = term({ id: 't-1', version: 1 })
  const termHeading = () => screen.queryByRole('heading', { name: 'Termo de Responsabilidade' })

  it('is not offered on a carrier Draft before the dispatch', async () => {
    renderDetail({ ...draft, deliveryMethod: 'Carrier' })
    await findTitle()
    expect(termHeading()).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Gerar termo' })).not.toBeInTheDocument()
  })

  it('is not offered on an in-person Draft (unchanged: generated only after Confirmar entrega)', async () => {
    renderDetail(draft)
    await findTitle()
    expect(termHeading()).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Gerar termo' })).not.toBeInTheDocument()
  })

  it('after the dispatch, offers Gerar termo through the existing endpoint without implying delivery', async () => {
    const fetchMock = renderDetail(dispatchedCarrier, {
      handlers: { [`POST ${BASE}/responsibility-terms`]: () => json(200, preparedTerm) },
    })
    await findTitle()

    expect(await screen.findByRole('heading', { name: 'Termo de Responsabilidade' })).toBeInTheDocument()
    expect(screen.getByText('A composição da entrega está bloqueada. O termo já pode ser preparado.')).toBeInTheDocument()
    // Still the pre-delivery state: delivery is the next step, nothing says it happened.
    expect(screen.getByRole('button', { name: 'Confirmar entrega' })).toBeInTheDocument()
    expect(screen.queryByText(/Entrega efetuada/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Entregue e atribuído/)).not.toBeInTheDocument()

    await userEvent.click(await screen.findByRole('button', { name: 'Gerar termo' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Gerar termo' })
    expect(within(dialog).getByText(/O termo poderá ser enviado após a confirmação da entrega\./)).toBeInTheDocument()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Gerar' }))

    await waitFor(() =>
      expect(mutations(fetchMock).map((r) => `${r.method} ${r.url.pathname}`)).toEqual([`POST ${BASE}/responsibility-terms`]),
    )
  })

  it('shows the prepared term as current and downloadable, with no send or signed upload before confirmation', async () => {
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: () => 'blob:term', revokeObjectURL: () => {} }))
    const fetchMock = renderDetail(dispatchedCarrier, {
      terms: [preparedTerm],
      handlers: { 'GET /api/responsibility-terms/t-1/content': () => new Response(new Blob(['%PDF'], { type: 'application/pdf' })) },
    })
    await findTitle()

    expect(await screen.findByText('TR-2026-0042')).toBeInTheDocument()
    expect(screen.getByText('Atual')).toBeInTheDocument()
    expect(screen.getByText('O termo poderá ser enviado após a confirmação da entrega.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Enviar' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Carregar documento assinado' })).not.toBeInTheDocument()
    expect(screen.queryByText('Documento assinado desta versão ainda não registado.')).not.toBeInTheDocument()
    expect(screen.queryByText('Aguardando termo assinado')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Descarregar PDF original' }))
    await waitFor(() =>
      expect(recordedRequests(fetchMock).some((r) => r.url.pathname === '/api/responsibility-terms/t-1/content')).toBe(true),
    )
    expect(screen.queryByText('Não foi possível descarregar o documento.')).not.toBeInTheDocument()
  })

  it('after Confirmar entrega keeps the same current term and only then offers Enviar and the signed upload', async () => {
    let current = dispatchedCarrier
    const fetchMock = renderDetail(dispatchedCarrier, {
      terms: [preparedTerm],
      handlers: {
        [`GET ${BASE}`]: () => json(200, current),
        [`POST ${BASE}/finalize`]: () => {
          current = finalizedCarrier
          return json(200, finalizedCarrier)
        },
      },
    })
    await findTitle()
    expect(await screen.findByText('TR-2026-0042')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Enviar' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Confirmar entrega' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Confirmar entrega' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirmar entrega' }))

    expect(await screen.findByRole('button', { name: 'Enviar' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Carregar documento assinado' })).toBeInTheDocument()
    expect(screen.getByText('Documento assinado desta versão ainda não registado.')).toBeInTheDocument()
    expect(screen.getByText('TR-2026-0042')).toBeInTheDocument()
    expect(screen.getByText('· versão 1')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Regenerar termo' })).toBeInTheDocument()
    // The prepared term is reused: confirming never regenerates it.
    expect(mutations(fetchMock).map((r) => `${r.method} ${r.url.pathname}`)).toEqual([`POST ${BASE}/finalize`])
  })
})

describe('HandoverDetailPage — after delivery', () => {
  it('AwaitingSignedTerm: says so, offers no draft actions and shows the current Generated term', async () => {
    renderDetail(finalized, { terms: [currentTerm, supersededTerm] })
    await findTitle()

    expect(screen.getAllByText('Aguardando termo assinado').length).toBeGreaterThan(0)
    expect(screen.getByText('Entrega efetuada. A aguardar submissão do termo de responsabilidade assinado.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Confirmar entrega' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Adicionar equipamento' })).not.toBeInTheDocument()
    // In person, the composition locks at Confirmar entrega: no removal afterwards.
    expect(screen.queryByRole('button', { name: /^Remover equipamento/ })).not.toBeInTheDocument()

    expect((await screen.findAllByText('TR-2026-0042')).length).toBe(2)
    expect(screen.getByText('Atual')).toBeInTheDocument()
    expect(screen.getByText('Não enviado')).toBeInTheDocument()
    expect(screen.getByText('Documento assinado desta versão ainda não registado.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Carregar documento assinado' })).toBeInTheDocument()
  })

  it('keeps superseded versions distinguishable and read-only (download only)', async () => {
    renderDetail(finalized, { terms: [currentTerm, supersededTerm] })
    await findTitle()

    const previous = await screen.findByRole('list', { name: 'Versões anteriores do termo' })
    const row = within(previous).getByRole('listitem')
    expect(within(row).getByText('Substituído')).toBeInTheDocument()
    expect(within(row).getAllByRole('button').map((b) => b.getAttribute('aria-label'))).toEqual([
      'Descarregar PDF TR-2026-0042 versão 1',
    ])
    // The old version's signature does not mark the current version as signed.
    expect(screen.queryByText('Documento assinado registado')).not.toBeInTheDocument()
  })

  it('uploads the externally signed PDF against the current version', async () => {
    const fetchMock = renderDetail(finalized, {
      terms: [currentTerm],
      handlers: { 'POST /api/responsibility-terms/t-2/signed-document': () => new Response(null, { status: 204 }) },
    })
    await findTitle()

    await userEvent.click(await screen.findByRole('button', { name: 'Carregar documento assinado' }))
    const dialog = await screen.findByRole('dialog', { name: 'Carregar documento assinado' })
    const input = dialog.querySelector<HTMLInputElement>('input[type="file"]')!
    expect(input).toHaveAttribute('accept', 'application/pdf')
    await userEvent.upload(input, new File(['%PDF'], 'termo-assinado.pdf', { type: 'application/pdf' }))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Carregar' }))

    await waitFor(() =>
      expect(mutations(fetchMock).map((r) => `${r.method} ${r.url.pathname}`)).toEqual([
        'POST /api/responsibility-terms/t-2/signed-document',
      ]),
    )
  })

  it('shows a rejected signed-document upload inside its dialog', async () => {
    renderDetail(finalized, {
      terms: [currentTerm],
      handlers: {
        'POST /api/responsibility-terms/t-2/signed-document': () => json(400, { title: 'O ficheiro tem de ser um PDF.' }),
      },
    })
    await findTitle()

    await userEvent.click(await screen.findByRole('button', { name: 'Carregar documento assinado' }))
    const dialog = await screen.findByRole('dialog', { name: 'Carregar documento assinado' })
    await userEvent.upload(dialog.querySelector<HTMLInputElement>('input[type="file"]')!, new File(['x'], 'a.pdf', { type: 'application/pdf' }))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Carregar' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('O ficheiro tem de ser um PDF.')
  })

  it('Completed: shows the signed artifact with its own download and delivery history', async () => {
    renderDetail(
      { ...finalized, operationalStatus: 'Completed' },
      {
        terms: [
          term({
            executionStatus: 'Signed',
            signature: {
              signedOn: '2026-10-09',
              uploadedAtUtc: '2026-10-09T12:00:00Z',
              sizeBytes: 51_000,
              sha256: 'new',
              uploadedByDisplayNameSnapshot: 'Eva Example',
            },
          }),
        ],
        handlers: {
          'GET /api/responsibility-terms/t-2/deliveries': () =>
            json(200, [
              {
                id: 'del-1',
                recipientEmailSnapshot: 'sofia@example.test',
                recipientOverridden: false,
                status: 'Failed',
                attemptedAtUtc: '2026-10-09T10:05:00Z',
                failureCode: 'SmtpUnavailable',
                sentByDisplayNameSnapshot: 'Eva Example',
                createdAtUtc: '2026-10-09T10:05:00Z',
              },
            ]),
        },
      },
    )
    await findTitle()

    expect(screen.getAllByText('Finalizada').length).toBeGreaterThan(0)
    expect(screen.getByText('Entrega concluída. O termo de responsabilidade assinado está registado.')).toBeInTheDocument()
    expect(await screen.findByText('Documento assinado registado')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Descarregar documento assinado' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Carregar documento assinado' })).not.toBeInTheDocument()
    expect(await screen.findByText('Falhou')).toBeInTheDocument()
  })
})

describe('HandoverCreatePage', () => {
  it('creates a Draft from consultant, delivery method and notes — with no photo upload', async () => {
    const fetchMock = stubApi({
      'GET /api/auth/me': () => json(200, administrationUser),
      'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
      'GET /api/consultants': () =>
        json(200, {
          items: [{ id: 'c-1', fullName: 'Sofia Sample Example', isActive: true }],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        }),
      'POST /api/equipment-handovers': () => json(201, draft),
      [`GET ${BASE}`]: () => json(200, draft),
      [`GET ${BASE}/responsibility-terms`]: () => json(200, []),
    })
    renderWithProviders(
      <>
        <Routes>
          <Route path="/entregas/nova" element={<HandoverCreatePage />} />
          <Route path="/entregas/:id" element={<p>Detalhe</p>} />
        </Routes>
        <LocationProbe />
      </>,
      { route: '/entregas/nova' },
    )

    await screen.findByRole('heading', { level: 1, name: 'Nova entrega' })
    expect(document.querySelector('input[type="file"]')).toBeNull()

    await userEvent.click(await screen.findByRole('button', { name: /Consultor/ }))
    await userEvent.click(await screen.findByRole('option', { name: /Sofia Sample Example/ }))
    await userEvent.click(screen.getByRole('radio', { name: /Entrega presencial/ }))
    await userEvent.type(screen.getByLabelText(/Notas/), 'Com carregador')
    await userEvent.click(screen.getByRole('button', { name: 'Criar entrega' }))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(`/entregas/${ID}`))
    const create = recordedRequests(fetchMock).find((r) => r.method === 'POST')
    expect(create?.body).toEqual({ consultantId: 'c-1', deliveryMethod: 'InPerson', notes: 'Com carregador' })
  })
})

describe('DeliveryMethodPicker keyboard', () => {
  it('create form: one Tab stop, and the arrow keys select the method (local form state only)', async () => {
    const fetchMock = stubApi({
      'GET /api/auth/me': () => json(200, administrationUser),
      'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
      'GET /api/consultants': () =>
        json(200, { items: [{ id: 'c-1', fullName: 'Sofia Sample Example', isActive: true }], page: 1, pageSize: 20, totalCount: 1 }),
    })
    renderWithProviders(
      <Routes>
        <Route path="/entregas/nova" element={<HandoverCreatePage />} />
      </Routes>,
      { route: '/entregas/nova' },
    )
    await screen.findByRole('heading', { level: 1, name: 'Nova entrega' })

    const inPerson = screen.getByRole('radio', { name: /Entrega presencial/ })
    const carrier = screen.getByRole('radio', { name: /Transportadora|Envio/ })
    // Nothing chosen yet: only the first option is in the Tab order.
    expect(inPerson).toHaveAttribute('tabindex', '0')
    expect(carrier).toHaveAttribute('tabindex', '-1')

    await userEvent.click(inPerson)
    expect(inPerson).toHaveAttribute('aria-checked', 'true')
    await userEvent.keyboard('{ArrowRight}')
    expect(carrier).toHaveFocus()
    expect(carrier).toHaveAttribute('aria-checked', 'true')
    expect(carrier).toHaveAttribute('tabindex', '0')
    expect(inPerson).toHaveAttribute('tabindex', '-1')

    await userEvent.keyboard('{ArrowLeft}')
    expect(inPerson).toHaveFocus()
    expect(inPerson).toHaveAttribute('aria-checked', 'true')
    expect(recordedRequests(fetchMock).filter((r) => r.method !== 'GET')).toEqual([])
  })

  it('existing Draft: arrow keys never save the method — only a deliberate Space/Enter or click does', async () => {
    const fetchMock = renderDetail(draft, {
      handlers: { [`PUT ${BASE}/delivery-method`]: () => json(200, { ...draft, deliveryMethod: 'Carrier' }) },
    })
    await findTitle()

    const inPerson = screen.getByRole('radio', { name: /Entrega presencial/ })
    const carrier = screen.getByRole('radio', { name: /Transportadora|Envio/ })
    // Clicking the already-selected method saves nothing; it just takes focus.
    await userEvent.click(inPerson)
    expect(inPerson).toHaveFocus()
    await userEvent.keyboard('{ArrowRight}{ArrowDown}{ArrowLeft}')
    expect(mutations(fetchMock)).toEqual([])

    // Each option is its own Tab stop on the saved Handover.
    await userEvent.tab()
    expect(carrier).toHaveFocus()
    await userEvent.keyboard(' ')
    await waitFor(() => expect(mutations(fetchMock).map((r) => `${r.method} ${r.url.pathname}`)).toEqual([`PUT ${BASE}/delivery-method`]))
    expect(mutations(fetchMock)[0]?.body).toEqual({ deliveryMethod: 'Carrier' })
  })
})
