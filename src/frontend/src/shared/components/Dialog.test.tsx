import { useState } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Dialog } from './Dialog'
import { ConfirmDialog } from './ConfirmDialog'
import { Button } from './Button'

function DialogHarness() {
  const [open, setOpen] = useState(false)
  return (
    <>
      <Button onClick={() => setOpen(true)}>Iniciar manutenção</Button>
      <Dialog
        open={open}
        onOpenChange={setOpen}
        title="Iniciar manutenção"
        description="Descreva a intervenção."
      >
        <input aria-label="Observações" />
      </Dialog>
    </>
  )
}

describe('Dialog', () => {
  it('keeps role="dialog", traps focus, closes on Escape and returns focus to the trigger', async () => {
    render(<DialogHarness />)
    const trigger = screen.getByRole('button', { name: 'Iniciar manutenção' })
    await userEvent.click(trigger)

    const dialog = await screen.findByRole('dialog', { name: 'Iniciar manutenção' })
    expect(dialog).toHaveAccessibleDescription('Descreva a intervenção.')
    expect(dialog).toContainElement(document.activeElement as HTMLElement)

    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(trigger).toHaveFocus()
  })

  it('closes from the labelled close button', async () => {
    render(<DialogHarness />)
    await userEvent.click(screen.getByRole('button', { name: 'Iniciar manutenção' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Fechar' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
})

describe('ConfirmDialog', () => {
  function renderConfirm(props: Partial<React.ComponentProps<typeof ConfirmDialog>> = {}) {
    const onConfirm = vi.fn()
    const onOpenChange = vi.fn()
    render(
      <ConfirmDialog
        open
        onOpenChange={onOpenChange}
        title="Abater equipamento"
        description='O equipamento "EQ-NB-0201" deixa permanentemente de estar disponível para atribuição.'
        confirmLabel="Abater"
        destructive
        onConfirm={onConfirm}
        {...props}
      />,
    )
    return { onConfirm, onOpenChange }
  }

  it('is an alertdialog described by its message, with initial focus on Cancelar', async () => {
    renderConfirm()
    const dialog = await screen.findByRole('alertdialog', { name: 'Abater equipamento' })
    expect(dialog).toHaveAccessibleDescription(/deixa permanentemente de estar disponível/)
    await waitFor(() => expect(screen.getByRole('button', { name: 'Cancelar' })).toHaveFocus())
  })

  it('confirms and cancels through the same callbacks as before', async () => {
    const { onConfirm, onOpenChange } = renderConfirm()
    await userEvent.click(await screen.findByRole('button', { name: 'Abater' }))
    expect(onConfirm).toHaveBeenCalledOnce()
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar' }))
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('still renders the business-rule error inside the dialog', async () => {
    renderConfirm({
      title: 'Confirmar entrega',
      confirmLabel: 'Confirmar entrega',
      destructive: false,
      error: 'O equipamento EQ-NB-0377 não tem fotografias atuais registadas.',
    })
    const dialog = await screen.findByRole('alertdialog', { name: 'Confirmar entrega' })
    expect(dialog).toContainElement(screen.getByRole('alert'))
    expect(screen.getByRole('alert')).toHaveTextContent('não tem fotografias atuais registadas')
  })

  it('accepts rich description content', async () => {
    renderConfirm({
      description: (
        <div>
          <p>Confirme apenas quando os equipamentos tiverem sido efetivamente entregues.</p>
        </div>
      ),
    })
    expect(await screen.findByRole('alertdialog')).toHaveAccessibleDescription(
      'Confirme apenas quando os equipamentos tiverem sido efetivamente entregues.',
    )
  })

  it('disables Cancelar and keeps Confirm labelled while loading', async () => {
    renderConfirm({ loading: true })
    expect(await screen.findByRole('button', { name: 'Cancelar' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Abater' })).toHaveAttribute('aria-busy', 'true')
  })
})
