import { Route, Routes } from 'react-router'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { AppLayout } from './AppLayout'
import { administrationUser, hrUser } from '../test/fixtures'
import { json, renderWithProviders, stubApi } from '../test/renderWithProviders'
import type { CurrentUser } from '../features/auth/types'

function renderShell(user: CurrentUser, route = '/') {
  stubApi({ 'GET /api/auth/me': () => json(200, user) })
  return renderWithProviders(
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={<p>Conteúdo do Dashboard</p>} />
        <Route path="*" element={<p>Conteúdo da página</p>} />
      </Route>
    </Routes>,
    { route },
  )
}

/** The desktop rail (the drawer renders the same list only while open). */
async function findRail() {
  const rail = await screen.findByRole('complementary')
  await within(rail).findByRole('link', { name: 'Dashboard' })
  return rail
}

function linkNames(container: HTMLElement, { hidden = false } = {}) {
  return within(container)
    .getAllByRole('link', { hidden })
    .map((link) => link.textContent)
}

describe('navigation rail — permission filtering', () => {
  it('shows Administration every section, including the admin-only entries', async () => {
    renderShell(administrationUser)
    const rail = await findRail()

    expect(linkNames(rail)).toEqual([
      'Dashboard',
      'Consultores',
      'Empresas',
      'Utilizadores',
      'Equipamentos',
      'Tipos de equipamento',
      'Entregas',
      'Devoluções',
      'Manutenção',
      'Consultores',
      'Equipamentos',
      'Inventário atual',
      'Solicitações de abate',
      'Auditoria',
    ])
    expect(within(rail).getByText('Administração')).toBeInTheDocument()
  })

  it('hides Utilizadores, Solicitações de abate and Auditoria from RH, and the then-empty Administração section', async () => {
    renderShell(hrUser)
    const rail = await findRail()
    const names = linkNames(rail)

    expect(names).not.toContain('Utilizadores')
    expect(names).not.toContain('Solicitações de abate')
    expect(names).not.toContain('Auditoria')
    expect(within(rail).queryByText('Administração')).not.toBeInTheDocument()
    // Inventário atual needs ALL of Consultant.Import, Equipment.Import and Equipment.Assign — RH has them.
    expect(names).toContain('Inventário atual')
    expect(names).toContain('Consultores')
    expect(names).toContain('Entregas')
  })

  it('hides Inventário atual when only some of its required permissions are present', async () => {
    renderShell({
      ...hrUser,
      permissions: hrUser.permissions.filter((p) => p !== 'Equipment.Assign'),
    })
    const rail = await findRail()

    expect(linkNames(rail)).not.toContain('Inventário atual')
  })
})

describe('navigation rail — active item', () => {
  it('marks only the current destination with aria-current="page"', async () => {
    renderShell(administrationUser, '/equipamentos')
    const rail = await findRail()

    const [equipmentList, equipmentImport] = within(rail).getAllByRole('link', {
      name: 'Equipamentos',
    })
    expect(equipmentList).toHaveAttribute('href', '/equipamentos')
    expect(equipmentList).toHaveAttribute('aria-current', 'page')
    expect(equipmentImport).toHaveAttribute('href', '/importacao-exportacao/equipamentos')
    expect(equipmentImport).not.toHaveAttribute('aria-current')
    expect(within(rail).getByRole('link', { name: 'Dashboard' })).not.toHaveAttribute(
      'aria-current',
    )
  })

  it('does not keep Dashboard current on nested routes', async () => {
    renderShell(administrationUser, '/entregas/123')
    const rail = await findRail()

    expect(within(rail).getByRole('link', { name: 'Entregas' })).toHaveAttribute(
      'aria-current',
      'page',
    )
    expect(within(rail).getByRole('link', { name: 'Dashboard' })).not.toHaveAttribute(
      'aria-current',
    )
  })
})

describe('narrow-viewport navigation drawer', () => {
  it('opens with the same permission-filtered navigation, traps focus, and closes on Escape returning focus to the trigger', async () => {
    const user = userEvent.setup()
    renderShell(hrUser)
    await findRail()

    const trigger = screen.getByRole('button', { name: 'Abrir navegação' })
    await user.click(trigger)

    const drawer = await screen.findByRole('dialog', { name: 'Navegação principal' })
    // The rail is aria-hidden behind the modal drawer, hence `hidden` for the comparison.
    expect(linkNames(drawer)).toEqual(
      linkNames(screen.getByRole('complementary', { hidden: true }), { hidden: true }),
    )
    expect(within(drawer).queryByRole('link', { name: 'Auditoria' })).not.toBeInTheDocument()
    expect(drawer).toContainElement(document.activeElement as HTMLElement)

    await user.keyboard('{Escape}')

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(trigger).toHaveFocus()
  })

  it('closes after choosing a destination and navigates there', async () => {
    const user = userEvent.setup()
    renderShell(administrationUser)
    await findRail()

    await user.click(screen.getByRole('button', { name: 'Abrir navegação' }))
    const drawer = await screen.findByRole('dialog', { name: 'Navegação principal' })
    await user.click(within(drawer).getByRole('link', { name: 'Entregas' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByText('Conteúdo da página')).toBeInTheDocument()
    expect(
      within(screen.getByRole('complementary')).getByRole('link', { name: 'Entregas' }),
    ).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('button', { name: 'Abrir navegação' })).toHaveFocus()
  })

  it('closes from its own close button', async () => {
    const user = userEvent.setup()
    renderShell(administrationUser)
    await findRail()

    await user.click(screen.getByRole('button', { name: 'Abrir navegação' }))
    await user.click(await screen.findByRole('button', { name: 'Fechar navegação' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
})

describe('top bar', () => {
  it('contains the account menu only — no search and no notifications', async () => {
    renderShell(administrationUser)
    await findRail()

    // Desktop top-bar trigger and the compact trigger in the narrow-viewport header.
    expect(screen.getAllByRole('button', { name: /Eva Example/ })).toHaveLength(2)
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /notifica/i })).not.toBeInTheDocument()
  })
})
