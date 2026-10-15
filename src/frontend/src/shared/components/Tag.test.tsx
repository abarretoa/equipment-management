import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Tag } from './Tag'
import { FilterBar } from './FilterBar'

describe('Tag', () => {
  it('renders a filter chip as "prefix: value" with an accessible remove action', async () => {
    const onRemove = vi.fn()
    render(<Tag prefix="Tipo" label="Laptop" onRemove={onRemove} />)

    expect(screen.getByText('Laptop').tagName).toBe('B')
    expect(screen.getByText(/Tipo:/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Remover filtro Tipo: Laptop' }))
    expect(onRemove).toHaveBeenCalledOnce()
  })

  it('keeps the existing plain tag behaviour and remove label', async () => {
    const onRemove = vi.fn()
    render(<Tag label="Aguarda 1ª alteração" onRemove={onRemove} />)
    await userEvent.click(screen.getByRole('button', { name: 'Remover Aguarda 1ª alteração' }))
    expect(onRemove).toHaveBeenCalledOnce()
  })

  it('is presentational without onRemove', () => {
    render(<Tag label="RH" />)
    expect(screen.getByText('RH')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })
})

describe('FilterBar', () => {
  it('shows chips and "Limpar filtros" only when filters are active', async () => {
    const onClear = vi.fn()
    const { rerender } = render(<FilterBar onClear={onClear} hasActiveFilters={false} />)
    expect(screen.queryByRole('button', { name: 'Limpar filtros' })).not.toBeInTheDocument()

    rerender(
      <FilterBar
        onClear={onClear}
        hasActiveFilters
        chips={
          <>
            <Tag prefix="Tipo" label="Laptop" onRemove={() => {}} />
            <Tag prefix="Estado" label="Disponível" onRemove={() => {}} />
          </>
        }
        summary="4 equipamentos"
      />,
    )
    expect(screen.getByRole('button', { name: 'Remover filtro Tipo: Laptop' })).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Remover filtro Estado: Disponível' }),
    ).toBeInTheDocument()
    expect(screen.getByText('4 equipamentos')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Limpar filtros' }))
    expect(onClear).toHaveBeenCalledOnce()
  })

  it('wires the search field', async () => {
    const onSearchChange = vi.fn()
    render(<FilterBar search="" onSearchChange={onSearchChange} />)
    await userEvent.type(screen.getByRole('textbox', { name: 'Pesquisar' }), 'a')
    expect(onSearchChange).toHaveBeenCalledWith('a')
  })
})
