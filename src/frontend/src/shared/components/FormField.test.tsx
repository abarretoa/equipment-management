import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { FormField } from './FormField'
import { Input } from './Input'
import { Select } from './Select'
import { Textarea } from './Textarea'

describe('FormField', () => {
  it('labels the control and announces the error, linked by id', () => {
    render(
      <FormField id="endDate" label="Data de fim" error="Indique a data de fim" required>
        <Input id="endDate" invalid aria-describedby="endDate-error" />
      </FormField>,
    )

    const input = screen.getByRole('textbox', { name: /Data de fim/ })
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input).toHaveAttribute('aria-describedby', 'endDate-error')
    const alert = screen.getByRole('alert')
    expect(alert).toHaveAttribute('id', 'endDate-error')
    expect(alert).toHaveTextContent('Indique a data de fim')
    expect(alert.querySelector('svg[aria-hidden="true"]')).not.toBeNull()
  })

  it('renders "(opcional)" and helper text without an alert', () => {
    render(
      <FormField id="notes" label="Notas" optional helperText="Observações internas.">
        <Textarea id="notes" />
      </FormField>,
    )
    expect(screen.getByText('(opcional)')).toBeInTheDocument()
    expect(screen.getByText('Observações internas.')).toHaveAttribute('id', 'notes-helper')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: /Notas/ })).not.toHaveAttribute('aria-invalid')
  })

  it('keeps the required marker out of the accessible name', () => {
    render(
      <FormField id="type" label="Tipo" required>
        <Select id="type">
          <option>Portátil</option>
        </Select>
      </FormField>,
    )
    expect(screen.getByRole('combobox', { name: 'Tipo' })).toBeInTheDocument()
  })

  it('supports the touch control size without setting the native size attribute', () => {
    render(<Input aria-label="Pesquisar" controlSize="touch" />)
    const input = screen.getByRole('textbox', { name: 'Pesquisar' })
    expect(input).toHaveClass('h-11')
    expect(input).not.toHaveAttribute('size')
  })
})
