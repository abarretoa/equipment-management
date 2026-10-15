import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Tabs } from './Tabs'

const items = [
  { value: '', label: 'Todos' },
  { value: 'Available', label: 'Disponível' },
  { value: 'Assigned', label: 'Atribuído' },
]

describe('Tabs', () => {
  it('keeps the historical default accessible name and accepts a custom one', () => {
    const { rerender } = render(<Tabs items={items} value="" onValueChange={() => {}} />)
    expect(screen.getByRole('tablist', { name: 'Secções' })).toBeInTheDocument()

    rerender(
      <Tabs items={items} value="" onValueChange={() => {}} aria-label="Filtrar por estado" />,
    )
    expect(screen.getByRole('tablist', { name: 'Filtrar por estado' })).toBeInTheDocument()
  })

  it('by default (automatic) activates a tab as soon as arrow keys move focus to it', async () => {
    const onValueChange = vi.fn()
    render(<Tabs items={items} value="" onValueChange={onValueChange} />)

    screen.getByRole('tab', { name: 'Todos' }).focus()
    await userEvent.keyboard('{ArrowRight}')

    expect(onValueChange).toHaveBeenLastCalledWith('Available')
  })

  it('with activationMode="manual" moves focus only, and activates on Enter', async () => {
    const onValueChange = vi.fn()
    render(<Tabs items={items} value="" onValueChange={onValueChange} activationMode="manual" />)

    screen.getByRole('tab', { name: 'Todos' }).focus()
    await userEvent.keyboard('{ArrowRight}')
    expect(screen.getByRole('tab', { name: 'Disponível' })).toHaveFocus()
    expect(onValueChange).not.toHaveBeenCalled()

    await userEvent.keyboard('{Enter}')
    expect(onValueChange).toHaveBeenCalledWith('Available')
  })

  it('marks the active tab as selected', () => {
    render(<Tabs items={items} value="Assigned" onValueChange={() => {}} />)
    expect(screen.getByRole('tab', { name: 'Atribuído' })).toHaveAttribute('aria-selected', 'true')
  })
})
