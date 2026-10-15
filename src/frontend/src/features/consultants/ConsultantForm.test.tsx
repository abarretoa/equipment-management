import { Route, Routes, useLocation } from 'react-router'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { ConsultantCreatePage, ConsultantEditPage } from './ConsultantForm'
import { administrationUser } from '../../test/fixtures'
import { json, recordedRequests, renderWithProviders, stubApi } from '../../test/renderWithProviders'
import type { ConsultantDetail } from './types'

const existing: ConsultantDetail = {
  id: 'c-1',
  fullName: 'Sofia Sample Example',
  nif: '123456789',
  email: 'sofia.sample@example.test',
  address: 'Rua Exemplo 12, Lisboa',
  phone: '+351 910 000 000',
  isActive: true,
  createdAtUtc: '2026-10-01T10:00:00Z',
  updatedAtUtc: '2026-10-01T10:00:00Z',
  currentAllocation: null,
  allocationHistory: [],
  currentAssignedEquipment: [],
}

function LocationProbe() {
  const location = useLocation()
  return <span data-testid="location">{location.pathname}</span>
}

function renderForm(route: string, handlers: Parameters<typeof stubApi>[0] = {}) {
  const fetchMock = stubApi({
    'GET /api/auth/me': () => json(200, administrationUser),
    'GET /api/antiforgery/token': () => json(200, { token: 'csrf' }),
    'GET /api/consultants/c-1': () => json(200, existing),
    ...handlers,
  })
  renderWithProviders(
    <>
      <Routes>
        <Route path="/consultores/novo" element={<ConsultantCreatePage />} />
        <Route path="/consultores/:id/editar" element={<ConsultantEditPage />} />
        <Route path="/consultores/:id" element={<p>Detalhe do consultor</p>} />
      </Routes>
      <LocationProbe />
    </>,
    { route },
  )
  return fetchMock
}

const posted = (fetchMock: ReturnType<typeof stubApi>, method: string) =>
  recordedRequests(fetchMock).filter((r) => r.method === method).map((r) => r.body)

describe('ConsultantForm', () => {
  it('keeps exactly the real person fields — no account, login or role fields', async () => {
    renderForm('/consultores/novo')
    await screen.findByRole('heading', { level: 1, name: 'Novo consultor' })
    for (const label of [/Nome completo/, /NIF/, /E-mail/, /Telefone/, /Morada/]) {
      expect(screen.getByLabelText(label)).toBeInTheDocument()
    }
    expect(screen.getAllByRole('textbox')).toHaveLength(5)
    expect(document.body.textContent).not.toMatch(/palavra-passe|password|utilizador|perfil|permiss/i)
  })

  it('keeps required validation and does not submit an empty form', async () => {
    const fetchMock = renderForm('/consultores/novo')
    await userEvent.click(await screen.findByRole('button', { name: 'Criar consultor' }))
    for (const message of ['Indique o nome completo', 'Indique o NIF', 'Indique o e-mail', 'Indique o telefone', 'Indique a morada']) {
      expect(await screen.findByText(message)).toBeInTheDocument()
    }
    expect(screen.getByLabelText(/Nome completo/)).toHaveAttribute('aria-invalid', 'true')
    expect(posted(fetchMock, 'POST')).toHaveLength(0)
  })

  it('creates with the same payload and opens the new consultant', async () => {
    const fetchMock = renderForm('/consultores/novo', { 'POST /api/consultants': () => json(201, { ...existing, id: 'c-9' }) })
    await userEvent.type(await screen.findByLabelText(/Nome completo/), 'Rui Example')
    await userEvent.type(screen.getByLabelText(/NIF/), '999999990')
    await userEvent.type(screen.getByLabelText(/E-mail/), 'ana@example.test')
    await userEvent.type(screen.getByLabelText(/Telefone/), '910000001')
    await userEvent.type(screen.getByLabelText(/Morada/), 'Porto')
    await userEvent.click(screen.getByRole('button', { name: 'Criar consultor' }))
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/consultores/c-9'))
    expect(posted(fetchMock, 'POST')).toEqual([
      { fullName: 'Rui Example', nif: '999999990', email: 'ana@example.test', address: 'Porto', phone: '910000001' },
    ])
  })

  it('prefills the edit form and saves with PUT', async () => {
    const fetchMock = renderForm('/consultores/c-1/editar', { 'PUT /api/consultants/c-1': () => json(200, existing) })
    expect(await screen.findByLabelText(/Nome completo/)).toHaveValue('Sofia Sample Example')
    expect(screen.getByLabelText(/NIF/)).toHaveValue('123456789')
    await userEvent.click(screen.getByRole('button', { name: 'Guardar alterações' }))
    await waitFor(() => expect(posted(fetchMock, 'PUT')).toHaveLength(1))
  })
})
