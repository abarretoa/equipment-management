import { useState } from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Combobox } from './Combobox'

const options = [
  { value: 'c1', label: 'Sofia Sample Example', secondary: 'Externo · Cliente Exemplo' },
  { value: 'c2', label: 'Rui Miguel Example' },
]

function Harness({
  initial = null,
  onChange,
}: {
  initial?: string | null
  onChange: (value: string) => void
}) {
  const [value, setValue] = useState<string | null>(initial)
  const [search, setSearch] = useState('')
  return (
    <Combobox
      aria-label="Consultor"
      value={value}
      onChange={(next) => {
        setValue(next || null)
        onChange(next)
      }}
      options={options}
      search={search}
      onSearchChange={setSearch}
    />
  )
}

describe('Combobox', () => {
  it('selects an option from the popover', async () => {
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)

    await userEvent.click(screen.getByRole('button', { name: 'Consultor' }))
    await userEvent.click(await screen.findByRole('option', { name: /Rui Miguel Example/ }))

    expect(onChange).toHaveBeenCalledWith('c2')
    expect(screen.getByRole('button', { name: 'Consultor' })).toHaveTextContent(
      'Rui Miguel Example',
    )
  })

  it('exposes "Limpar seleção" as its own button outside the trigger, and clears without opening', async () => {
    const onChange = vi.fn()
    render(<Harness initial="c1" onChange={onChange} />)

    const trigger = screen.getByRole('button', { name: 'Consultor' })
    const clear = screen.getByRole('button', { name: 'Limpar seleção' })
    expect(trigger).not.toContainElement(clear)

    await userEvent.click(clear)
    expect(onChange).toHaveBeenCalledWith('')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Limpar seleção' })).not.toBeInTheDocument()
  })

  it('offers no clear action when disabled', () => {
    render(
      <Combobox
        aria-label="Consultor"
        value="c1"
        onChange={() => {}}
        options={options}
        search=""
        onSearchChange={() => {}}
        disabled
      />,
    )
    expect(screen.queryByRole('button', { name: 'Limpar seleção' })).not.toBeInTheDocument()
  })
})
