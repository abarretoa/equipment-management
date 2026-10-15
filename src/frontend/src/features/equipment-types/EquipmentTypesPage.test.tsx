import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { EquipmentTypesPage } from './EquipmentTypesPage'
import { administrationUser, hrUser } from '../../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import type { CurrentUser } from '../auth/types'
import type { EquipmentType } from './types'

const types: EquipmentType[] = [
  { id: 't-1', name: 'Portátil', kind: 'Laptop', assetTagPrefix: 'NB', isActive: true, createdAtUtc: '', updatedAtUtc: '' },
  { id: 't-2', name: 'Monitor antigo', kind: 'Generic', assetTagPrefix: 'MON', isActive: false, createdAtUtc: '', updatedAtUtc: '' },
]

function renderPage({ user = administrationUser, handlers = {} }: { user?: CurrentUser; handlers?: Parameters<typeof stubApi>[0] } = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, user),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    'GET /api/equipment-types': () => json(200, { items: types, page: 1, pageSize: 20, totalCount: types.length }),
    ...handlers,
  })
  renderWithProviders(<EquipmentTypesPage />)
  return fetchMock
}

async function openMenu(name: string) {
  const table = await screen.findByRole('table')
  within(table).getByRole('button', { name: `Mais ações: ${name}` }).focus()
  await userEvent.keyboard('{Enter}')
  return screen.findByRole('menu')
}

const bodies = (fetchMock: ReturnType<typeof stubApi>, method: string) =>
  recordedRequests(fetchMock).filter((r) => r.method === method).map((r) => ({ url: r.url.pathname, body: r.body }))

describe('EquipmentTypesPage', () => {
  it('lists types with category, prefix and state', async () => {
    renderPage()
    const table = await screen.findByRole('table')
    for (const header of ['Nome', 'Categoria', 'Prefixo', 'Estado']) {
      expect(within(table).getByRole('columnheader', { name: header })).toBeInTheDocument()
    }
    const allRows = within(table).getAllByRole('row')
    const active = allRows[1]!
    const archived = allRows[2]!
    expect(active).toHaveTextContent('Portátil')
    expect(active).toHaveTextContent('NB')
    expect(active).toHaveTextContent('Ativo')
    // An archived type stays listed and reads clearly as Inativo (icon + text).
    expect(archived).toHaveTextContent('Genérico')
    expect(within(archived).getByText('Inativo').querySelector('svg')).not.toBeNull()
  })

  it('shows Desativar/Ativar only with EquipmentType.Archive', async () => {
    renderPage({ user: hrUser })
    const menu = await openMenu('Portátil')
    expect(within(menu).getByRole('menuitem', { name: 'Editar' })).toBeInTheDocument()
    expect(within(menu).queryByRole('menuitem', { name: /Desativar|Ativar/ })).not.toBeInTheDocument()
  })

  it('offers Desativar (destructive) for an active type and calls the same endpoint', async () => {
    const fetchMock = renderPage({ handlers: { 'POST /api/equipment-types/t-1/deactivate': () => json(204) } })
    const menu = await openMenu('Portátil')
    const item = within(menu).getByRole('menuitem', { name: 'Desativar' })
    expect(item).toHaveClass('text-danger')
    await userEvent.click(item)
    const dialog = await screen.findByRole('alertdialog', { name: 'Desativar tipo de equipamento' })
    expect(dialog).toHaveTextContent('Os equipamentos já registados não são afetados.')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Desativar' }))
    await waitFor(() => expect(bodies(fetchMock, 'POST').map((c) => c.url)).toEqual(['/api/equipment-types/t-1/deactivate']))
  })

  it('offers Ativar for an archived type', async () => {
    renderPage()
    const menu = await openMenu('Monitor antigo')
    expect(within(menu).getByRole('menuitem', { name: 'Ativar' })).not.toHaveClass('text-danger')
  })

  it('keeps create validation and payload', async () => {
    const fetchMock = renderPage({ handlers: { 'POST /api/equipment-types': () => json(201, types[0]) } })
    await screen.findByRole('table')
    await userEvent.click(screen.getByRole('button', { name: 'Novo tipo' }))
    const dialog = await screen.findByRole('dialog', { name: 'Novo tipo de equipamento' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Criar tipo de equipamento' }))
    expect(await within(dialog).findByText('Indique o nome')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique o prefixo')).toBeInTheDocument()
    await userEvent.type(within(dialog).getByLabelText(/Nome/), 'Teclado')
    await userEvent.type(within(dialog).getByLabelText(/Prefixo/), 'K-1')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Criar tipo de equipamento' }))
    expect(await within(dialog).findByText('Apenas letras e números')).toBeInTheDocument()
    await userEvent.clear(within(dialog).getByLabelText(/Prefixo/))
    await userEvent.type(within(dialog).getByLabelText(/Prefixo/), 'KB')
    await userEvent.selectOptions(within(dialog).getByLabelText(/Categoria/), 'Generic')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Criar tipo de equipamento' }))
    await waitFor(() =>
      expect(bodies(fetchMock, 'POST').map((c) => c.body)).toEqual([{ name: 'Teclado', kind: 'Generic', assetTagPrefix: 'KB' }]),
    )
  })

  it('keeps the category locked on edit and sends only name + prefix', async () => {
    const fetchMock = renderPage({ handlers: { 'PUT /api/equipment-types/t-1': () => json(200, types[0]) } })
    const menu = await openMenu('Portátil')
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar tipo de equipamento' })
    expect(within(dialog).getByLabelText(/Categoria/)).toBeDisabled()
    expect(within(dialog).getByText('A categoria não pode ser alterada após a criação.')).toBeInTheDocument()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar alterações' }))
    await waitFor(() => expect(bodies(fetchMock, 'PUT').map((c) => c.body)).toEqual([{ name: 'Portátil', assetTagPrefix: 'NB' }]))
  })
})
