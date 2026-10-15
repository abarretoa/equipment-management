import { Route, Routes } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ReturnDetailPage } from './ReturnDetailPage'
import { ReturnGroupDetailPage } from './ReturnGroupDetailPage'
import { administrationUser } from '../../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import type { EquipmentReturnDetail, ReturnGroupDetail } from './types'

const ID = 'r-1'
const BASE = `/api/equipment-returns/${ID}`

const pending: EquipmentReturnDetail = {
  id: ID,
  equipmentAssignmentId: 'a-1',
  equipmentId: 'eq-1',
  assetTag: 'EQ-NB-0412',
  equipmentTypeName: 'Portátil',
  consultantId: 'c-1',
  consultantFullName: 'Sofia Sample Example',
  status: 'Pending',
  reason: 'Fim de projeto',
  expectedReturnDate: '2026-10-12',
  createdByDisplayNameSnapshot: 'Ana Demo',
  createdAtUtc: '2026-10-08T10:00:00Z',
  updatedAtUtc: '2026-10-08T10:00:00Z',
  inspection: null,
  returnGroupId: 'g-1',
  returnGroupSize: 3,
}

const received: EquipmentReturnDetail = {
  ...pending,
  status: 'Received',
  receivedOn: '2026-10-09',
  receivedAtUtc: '2026-10-09T10:00:00Z',
  receivedByDisplayNameSnapshot: 'Eva Example',
}

const laptop = { id: 'eq-1', laptopSpecification: { processor: 'i7', memory: '16 GB', storage: '512 GB' } }
const phone = { id: 'eq-1', mobilePhoneSpecification: { imei: '356789012345678' } }
const monitor = { id: 'eq-1' }

const photo = (name: string) => new File(['x'], name, { type: 'image/jpeg' })

function renderDetail(ret: EquipmentReturnDetail, equipment: object, extra: Parameters<typeof stubApi>[0] = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, administrationUser),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    [`GET ${BASE}`]: () => json(200, ret),
    'GET /api/equipment/eq-1': () => json(200, equipment),
    'GET /api/users': () => json(200, { items: [], page: 1, pageSize: 100, totalCount: 0 }),
    ...extra,
  })
  renderWithProviders(
    <Routes>
      <Route path="/devolucoes/:id" element={<ReturnDetailPage />} />
      <Route path="/devolucoes/grupos/:groupId" element={<ReturnGroupDetailPage />} />
    </Routes>,
    { route: `/devolucoes/${ID}` },
  )
  return fetchMock
}

const findTitle = () => screen.findByRole('heading', { level: 1, name: /Devolução: EQ-NB-0412/ })
const posts = (fetchMock: ReturnType<typeof stubApi>) => fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')
const equipmentFetches = (fetchMock: ReturnType<typeof stubApi>) =>
  recordedRequests(fetchMock).filter((r) => r.method === 'GET' && r.url.pathname === '/api/equipment/eq-1').length

async function openReceive() {
  await findTitle()
  await userEvent.click(screen.getByRole('button', { name: 'Receber equipamento' }))
  return screen.findByRole('dialog', { name: 'Receber equipamento' })
}

beforeEach(() => {
  vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: () => 'blob:photo', revokeObjectURL: () => {} }))
})
afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ReturnDetailPage — receiving', () => {
  it.each([
    ['Laptop', laptop],
    ['MobilePhone', phone],
  ])('%s: receipt requires exactly 3 current photos, sent with the receive call', async (_kind, equipment) => {
    const fetchMock = renderDetail(pending, equipment, { [`POST ${BASE}/receive`]: () => json(200, received) })
    const dialog = await openReceive()

    const group = await within(dialog).findByRole('group', { name: /Fotografias do estado atual/ })
    expect(within(group).getByText(/Passam a ser as fotografias atuais do equipamento/)).toBeInTheDocument()
    const input = group.querySelector<HTMLInputElement>('input[type="file"]')!
    await userEvent.upload(input, [photo('frente.jpg'), photo('verso.jpg')])
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirmar receção' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'São exigidas exatamente 3 fotografias do estado do equipamento (2/3 selecionadas).',
    )
    expect(posts(fetchMock)).toHaveLength(0)

    await userEvent.upload(group.querySelector<HTMLInputElement>('input[type="file"]')!, photo('lateral.jpg'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirmar receção' }))

    await waitFor(() => expect(posts(fetchMock)).toHaveLength(1))
    expect(recordedRequests(fetchMock).find((r) => r.method === 'POST')?.url.pathname).toBe(`${BASE}/receive`)
    const form = posts(fetchMock)[0]![1]?.body as FormData
    expect(form.getAll('Photos').map((f) => (f as File).name)).toEqual(['frente.jpg', 'verso.jpg', 'lateral.jpg'])
    expect(form.get('ReceivedByUserId')).toBe(administrationUser.id)
  })

  it('a type without condition photos gains no photo requirement', async () => {
    const fetchMock = renderDetail(pending, monitor, { [`POST ${BASE}/receive`]: () => json(200, received) })
    const dialog = await openReceive()

    expect(within(dialog).queryByRole('group', { name: /Fotografias do estado atual/ })).not.toBeInTheDocument()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirmar receção' }))

    await waitFor(() => expect(posts(fetchMock)).toHaveLength(1))
    expect((posts(fetchMock)[0]![1]?.body as FormData).getAll('Photos')).toHaveLength(0)
  })

  it('refreshes the equipment (its status and current photo set) after a successful receipt', async () => {
    const fetchMock = renderDetail(pending, laptop, { [`POST ${BASE}/receive`]: () => json(200, received) })
    const dialog = await openReceive()
    const input = (await within(dialog).findByRole('group', { name: /Fotografias/ })).querySelector<HTMLInputElement>('input[type="file"]')!
    await userEvent.upload(input, [photo('a.jpg'), photo('b.jpg'), photo('c.jpg')])
    const before = equipmentFetches(fetchMock)

    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirmar receção' }))

    await waitFor(() => expect(equipmentFetches(fetchMock)).toBeGreaterThan(before))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Receber equipamento' })).not.toBeInTheDocument())
  })

  it('shows a rejected cancellation inside its dialog', async () => {
    renderDetail(pending, laptop, {
      [`POST ${BASE}/cancel`]: () => json(409, { title: 'A devolução já não está pendente.' }),
    })
    await findTitle()

    await userEvent.click(screen.getByRole('button', { name: 'Cancelar devolução' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar devolução' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancelar devolução' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('A devolução já não está pendente.')
  })

  it('belongs to its offboarding collection: breadcrumb and context link lead back to the group', async () => {
    renderDetail(pending, laptop)
    await findTitle()

    const crumbs = screen.getByRole('navigation', { name: 'Localização' })
    expect(within(crumbs).getByRole('link', { name: 'Sofia Sample Example' })).toHaveAttribute('href', '/devolucoes/grupos/g-1')
    expect(screen.getByRole('link', { name: 'Ver conjunto (3 equipamentos)' })).toHaveAttribute('href', '/devolucoes/grupos/g-1')
    expect(screen.getByRole('link', { name: 'EQ-NB-0412' })).toHaveAttribute('href', '/equipamentos/eq-1')
  })
})

describe('ReturnDetailPage — inspection', () => {
  it('no-damage path: saves the inspection and finalizes it', async () => {
    const fetchMock = renderDetail(received, laptop, {
      [`PUT ${BASE}/inspection`]: () => new Response(null, { status: 204 }),
      [`POST ${BASE}/inspection/finalize`]: () => json(200, { ...received, status: 'Completed' }),
    })
    await findTitle()

    expect(screen.queryByRole('heading', { name: 'Fotografias da avaria' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Guardar inspeção' }))
    await waitFor(() =>
      expect(recordedRequests(fetchMock).find((r) => r.method === 'PUT')?.body).toEqual({ condition: 'Good', hasDamage: false }),
    )

    await userEvent.click(screen.getByRole('button', { name: 'Finalizar inspeção' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Finalizar inspeção' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Finalizar' }))
    await waitFor(() =>
      expect(recordedRequests(fetchMock).some((r) => r.method === 'POST' && r.url.pathname === `${BASE}/inspection/finalize`)).toBe(true),
    )
  })

  it('damage path: requires a description and offers damage-evidence photos (≥1), separate from the current set', async () => {
    const fetchMock = renderDetail(received, laptop, {
      [`PUT ${BASE}/inspection`]: () => new Response(null, { status: 204 }),
      [`POST ${BASE}/damage/photos`]: () => new Response(null, { status: 204 }),
    })
    await findTitle()

    await userEvent.click(screen.getByRole('checkbox', { name: 'Equipamento com avaria' }))
    await userEvent.click(screen.getByRole('button', { name: 'Guardar inspeção' }))
    expect(await screen.findByText('Descreva a avaria encontrada')).toBeInTheDocument()
    expect(recordedRequests(fetchMock).some((r) => r.method === 'PUT')).toBe(false)

    await userEvent.type(screen.getByLabelText(/Descrição da avaria/), 'Ecrã partido')
    await userEvent.click(screen.getByRole('button', { name: 'Guardar inspeção' }))
    await waitFor(() =>
      expect(recordedRequests(fetchMock).find((r) => r.method === 'PUT')?.body).toEqual({
        condition: 'Good',
        hasDamage: true,
        damageDescription: 'Ecrã partido',
      }),
    )

    const damageSection = screen.getByRole('heading', { name: 'Fotografias da avaria' }).closest('section')!
    expect(within(damageSection).getByText('Pelo menos 1 fotografia.')).toBeInTheDocument()
    await userEvent.upload(damageSection.querySelector<HTMLInputElement>('input[type="file"]')!, photo('dano.jpg'))
    await waitFor(() =>
      expect(recordedRequests(fetchMock).some((r) => r.method === 'POST' && r.url.pathname === `${BASE}/damage/photos`)).toBe(true),
    )
  })

  it('shows a failed inspection save next to the form', async () => {
    renderDetail(received, laptop, {
      [`PUT ${BASE}/inspection`]: () => json(409, { title: 'A devolução não está em inspeção.' }),
    })
    await findTitle()
    await userEvent.click(screen.getByRole('button', { name: 'Guardar inspeção' }))
    expect(await screen.findByText('A devolução não está em inspeção.')).toBeInTheDocument()
  })

  it('evidence photos keep an always-visible, labelled remove action', async () => {
    renderDetail(
      {
        ...received,
        inspection: {
          id: 'ins-1',
          condition: 'Good',
          hasDamage: false,
          inspectedByDisplayNameSnapshot: 'Eva Example',
          inspectedAtUtc: '2026-10-09T11:00:00Z',
          photos: [{ id: 'p-1', originalFileName: 'detalhe.jpg', sizeBytes: 2048, uploadedAtUtc: '2026-10-09T11:00:00Z' }],
          damagePhotos: [],
        },
      },
      laptop,
      { [`GET ${BASE}/inspection/photos/p-1/content`]: () => new Response(new Blob(['x'], { type: 'image/jpeg' })) },
    )
    await findTitle()
    const remove = await screen.findByRole('button', { name: 'Remover fotografia detalhe.jpg' })
    expect(remove.className).not.toMatch(/opacity-0/)
    expect(remove.parentElement?.className ?? '').not.toMatch(/opacity-0/)
  })

  it('opens inspection and damage evidence in the shared viewer, through their own authenticated endpoints', async () => {
    const evidence = (id: string, name: string) => ({ id, originalFileName: name, sizeBytes: 2048, uploadedAtUtc: '2026-10-09T11:00:00Z' })
    const fetchMock = renderDetail(
      {
        ...received,
        inspection: {
          id: 'ins-1',
          condition: 'Fair',
          hasDamage: true,
          damageDescription: 'Ecrã riscado',
          inspectedByDisplayNameSnapshot: 'Eva Example',
          inspectedAtUtc: '2026-10-09T11:00:00Z',
          photos: [evidence('p-1', 'detalhe.jpg')],
          damagePhotos: [evidence('d-1', 'risco-1.jpg'), evidence('d-2', 'risco-2.jpg')],
        },
      },
      laptop,
      {
        [`GET ${BASE}/inspection/photos/p-1/content`]: () => new Response(new Blob(['x'], { type: 'image/jpeg' })),
        [`GET ${BASE}/damage/photos/d-1/content`]: () => new Response(new Blob(['x'], { type: 'image/jpeg' })),
        [`GET ${BASE}/damage/photos/d-2/content`]: () => new Response(new Blob(['x'], { type: 'image/jpeg' })),
      },
    )
    await findTitle()

    const evidenceThumb = await screen.findByRole('button', { name: 'Ampliar Fotografia 1 de 1 (detalhe.jpg)' })
    await userEvent.click(evidenceThumb)
    let viewer = screen.getByRole('dialog', { name: 'Fotografias da devolução' })
    expect(await within(viewer).findByRole('img', { name: 'Fotografia 1 de 1 (detalhe.jpg)' })).toBeInTheDocument()
    expect(within(viewer).queryByRole('button', { name: 'Fotografia seguinte' })).not.toBeInTheDocument()
    await userEvent.click(within(viewer).getByRole('button', { name: 'Fechar visualizador' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(evidenceThumb).toHaveFocus()

    await userEvent.click(screen.getByRole('button', { name: 'Ampliar Fotografia 1 de 2 (risco-1.jpg)' }))
    viewer = screen.getByRole('dialog', { name: 'Fotografias da avaria' })
    await userEvent.keyboard('{ArrowRight}')
    expect(await within(viewer).findByRole('img', { name: 'Fotografia 2 de 2 (risco-2.jpg)' })).toBeInTheDocument()

    const photoGets = recordedRequests(fetchMock)
      .filter((r) => r.url.pathname.endsWith('/content'))
      .map((r) => r.url.pathname)
    expect(new Set(photoGets)).toEqual(
      new Set([`${BASE}/inspection/photos/p-1/content`, `${BASE}/damage/photos/d-1/content`, `${BASE}/damage/photos/d-2/content`]),
    )
  })
})

describe('ReturnGroupDetailPage', () => {
  it('shows the collection progress and opens each item on its own return', async () => {
    const detail: ReturnGroupDetail = {
      group: {
        id: 'g-1',
        consultantId: 'c-1',
        consultantFullName: 'Sofia Sample Example',
        isEndOfEngagement: true,
        collectionStatus: 'Partial',
        expectedFromDate: '2026-10-10',
        expectedToDate: null,
        receivedCount: 1,
        createdByDisplayNameSnapshot: 'Ana Demo',
        createdAtUtc: '2026-10-08T10:00:00Z',
        items: [
          { returnId: 'r-1', equipmentId: 'eq-1', assetTag: 'EQ-NB-0412', equipmentTypeName: 'Portátil', status: 'Received', equipmentStatus: 'UnderInspection', receivedOn: '2026-10-09' },
          { returnId: 'r-2', equipmentId: 'eq-2', assetTag: 'EQ-TM-0077', equipmentTypeName: 'Telemóvel', status: 'Pending', equipmentStatus: 'ReturnPending' },
        ],
      },
      otherPendingReturns: [],
    }
    stubApi({
      'GET /api/auth/me': () => json(200, administrationUser),
      'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
      'GET /api/equipment-returns/groups/g-1': () => json(200, detail),
    })
    renderWithProviders(
      <Routes>
        <Route path="/devolucoes/grupos/:groupId" element={<ReturnGroupDetailPage />} />
      </Routes>,
      { route: '/devolucoes/grupos/g-1' },
    )

    expect(await screen.findByRole('heading', { level: 1, name: 'Sofia Sample Example' })).toBeInTheDocument()
    expect(screen.getByText('Recolha parcial')).toBeInTheDocument()
    expect(screen.getByText('2 equipamentos · 1 de 2 recebidos')).toBeInTheDocument()
    const table = screen.getByRole('table')
    expect(within(table).getByRole('columnheader', { name: 'Identificador' })).toBeInTheDocument()
    expect(within(table).getByRole('link', { name: 'EQ-TM-0077' })).toHaveAttribute('href', '/devolucoes/r-2')
    expect(within(table).getByText('Aguardando recolha')).toBeInTheDocument()
    expect(within(table).getByText('Recebido · em inspeção')).toBeInTheDocument()
    expect(within(table).getAllByRole('link', { name: 'Abrir devolução' })).toHaveLength(2)
  })
})
