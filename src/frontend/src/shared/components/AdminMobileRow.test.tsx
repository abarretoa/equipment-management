import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AdminMobileRow } from './AdminMobileRow'

describe('AdminMobileRow', () => {
  it('renders name, state, metadata and the menu control', () => {
    render(
      <AdminMobileRow
        name="Cliente Exemplo, S.A."
        status={<span>Ativo</span>}
        meta="Criada em 01/09/2026"
        menu={<button type="button">Mais ações</button>}
      />,
    )
    expect(screen.getByText('Cliente Exemplo, S.A.')).toBeInTheDocument()
    expect(screen.getByText('Ativo')).toBeInTheDocument()
    expect(screen.getByText('Criada em 01/09/2026')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Mais ações' })).toBeInTheDocument()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })

  it('works without a menu', () => {
    render(<AdminMobileRow name="Portátil" status={<span>Ativo</span>} meta="Portátil · NB" />)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })
})
