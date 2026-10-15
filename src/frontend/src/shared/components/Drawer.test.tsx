import { useState } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { Drawer } from './Drawer'

/** Like the Audit/Maintenance pages: the drawer is opened from a plain control, not a Radix Trigger. */
function DrawerHarness() {
  const [open, setOpen] = useState(false)
  return (
    <>
      <button onClick={() => setOpen(true)}>Abrir detalhe</button>
      <Drawer open={open} onOpenChange={setOpen} title="Detalhe do registo de auditoria">
        <p>Resumo</p>
      </Drawer>
    </>
  )
}

describe('Drawer', () => {
  it('traps focus while open and returns it to the opener on Escape', async () => {
    render(<DrawerHarness />)
    const opener = screen.getByRole('button', { name: 'Abrir detalhe' })
    await userEvent.click(opener)

    const drawer = await screen.findByRole('dialog', { name: 'Detalhe do registo de auditoria' })
    expect(drawer).toContainElement(document.activeElement as HTMLElement)

    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(opener).toHaveFocus()
  })

  it('returns focus to the opener when closed from its close button', async () => {
    render(<DrawerHarness />)
    const opener = screen.getByRole('button', { name: 'Abrir detalhe' })
    await userEvent.click(opener)

    await userEvent.click(await screen.findByRole('button', { name: 'Fechar' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(opener).toHaveFocus()
  })
})
