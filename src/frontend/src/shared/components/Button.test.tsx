import type { FormEvent } from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Button, type ButtonSize, type ButtonVariant } from './Button'
import { IconButton } from './IconButton'

describe('Button', () => {
  it.each<ButtonVariant>(['primary', 'secondary', 'ghost', 'danger', 'danger-outline'])(
    'renders the %s variant as a clickable button',
    async (variant) => {
      const onClick = vi.fn()
      render(
        <Button variant={variant} onClick={onClick}>
          Guardar
        </Button>,
      )
      await userEvent.click(screen.getByRole('button', { name: 'Guardar' }))
      expect(onClick).toHaveBeenCalledOnce()
    },
  )

  it.each<[ButtonSize, string]>([
    ['sm', 'h-8'],
    ['md', 'h-9'],
    ['lg', 'lg:h-10'],
    ['touch', 'h-11'],
  ])('applies the %s size', (size, heightClass) => {
    render(<Button size={size}>Guardar</Button>)
    expect(screen.getByRole('button')).toHaveClass(heightClass)
  })

  it('defaults to the secondary 36px button', () => {
    render(<Button>Cancelar</Button>)
    expect(screen.getByRole('button', { name: 'Cancelar' })).toHaveClass(
      'h-9',
      'border-border-control',
    )
  })

  it('blocks a second submit while loading and stays labelled', async () => {
    const onClick = vi.fn()
    render(
      <Button variant="primary" loading onClick={onClick}>
        A guardar…
      </Button>,
    )
    const button = screen.getByRole('button', { name: 'A guardar…' })
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute('aria-busy', 'true')
    await userEvent.click(button)
    expect(onClick).not.toHaveBeenCalled()
  })
})

describe('IconButton', () => {
  it('always has an accessible name from its label', () => {
    render(<IconButton icon={<svg />} label="Remover fotografia" size="touch" />)
    expect(screen.getByRole('button', { name: 'Remover fotografia' })).toHaveClass('size-11')
  })

  it('defaults to type="button" and keeps an explicit type override', () => {
    render(
      <>
        <IconButton icon={<svg />} label="Remover" />
        <IconButton icon={<svg />} label="Enviar" type="submit" />
      </>,
    )
    expect(screen.getByRole('button', { name: 'Remover' })).toHaveAttribute('type', 'button')
    expect(screen.getByRole('button', { name: 'Enviar' })).toHaveAttribute('type', 'submit')
  })

  it('submits a surrounding form only when explicitly type="submit"', async () => {
    const onSubmit = vi.fn((event: FormEvent) => event.preventDefault())
    render(
      <form onSubmit={onSubmit}>
        <IconButton icon={<svg />} label="Enviar" type="submit" />
      </form>,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Enviar' }))
    expect(onSubmit).toHaveBeenCalledOnce()
  })

  it('never submits a surrounding form', async () => {
    const onSubmit = vi.fn((event: FormEvent) => event.preventDefault())
    render(
      <form onSubmit={onSubmit}>
        <IconButton icon={<svg />} label="Remover fotografia" />
      </form>,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Remover fotografia' }))
    expect(onSubmit).not.toHaveBeenCalled()
  })
})
