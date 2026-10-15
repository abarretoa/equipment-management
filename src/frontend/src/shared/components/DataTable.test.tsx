import { Link, MemoryRouter, Route, Routes } from 'react-router'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Laptop } from 'lucide-react'
import { DataTable, type DataTableColumn } from './DataTable'

interface Row {
  id: string
  assetTag: string
  consultantId: string
  consultantName: string
}

const rows: Row[] = [
  { id: 'e1', assetTag: 'EQ-NB-0412', consultantId: 'c1', consultantName: 'Sofia Sample' },
  { id: 'e2', assetTag: 'EQ-NB-0377', consultantId: 'c2', consultantName: 'Rui Example' },
]

const columns: DataTableColumn<Row>[] = [
  { key: 'assetTag', header: 'Identificador', render: (row) => row.assetTag },
  {
    key: 'consultant',
    header: 'Atribuído a',
    render: (row) => <Link to={`/consultores/${row.consultantId}`}>{row.consultantName}</Link>,
  },
]

function renderTable(ui: React.ReactElement) {
  return render(
    <MemoryRouter initialEntries={['/equipamentos']}>
      <Routes>
        <Route path="/equipamentos" element={ui} />
        <Route path="/equipamentos/:id" element={<p>Detalhe do equipamento</p>} />
        <Route path="/consultores/:id" element={<p>Detalhe do consultor</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('DataTable', () => {
  it('keeps the existing onRowClick usage working', async () => {
    const onRowClick = vi.fn()
    renderTable(
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(row) => row.id}
        emptyTitle="Nenhum equipamento encontrado"
        onRowClick={onRowClick}
      />,
    )

    expect(screen.getByRole('columnheader', { name: 'Identificador' })).toBeInTheDocument()
    await userEvent.click(screen.getByText('EQ-NB-0412'))
    expect(onRowClick).toHaveBeenCalledWith(rows[0])
  })

  it('does not fire the row action when a nested link inside the row is clicked', async () => {
    const onRowClick = vi.fn()
    renderTable(
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(row) => row.id}
        emptyTitle="Nenhum equipamento encontrado"
        onRowClick={onRowClick}
      />,
    )

    await userEvent.click(screen.getByRole('link', { name: 'Rui Example' }))
    expect(onRowClick).not.toHaveBeenCalled()
    expect(screen.getByText('Detalhe do consultor')).toBeInTheDocument()
  })

  it('renders the primary row destination as a keyboard-reachable link', async () => {
    renderTable(
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(row) => row.id}
        emptyTitle="Nenhum equipamento encontrado"
        getRowHref={(row) => `/equipamentos/${row.id}`}
      />,
    )

    const rowLink = screen.getByRole('link', { name: 'EQ-NB-0412' })
    expect(rowLink).toHaveAttribute('href', '/equipamentos/e1')

    await userEvent.tab()
    expect(rowLink).toHaveFocus()
    await userEvent.keyboard('{Enter}')
    expect(screen.getByText('Detalhe do equipamento')).toBeInTheDocument()
  })

  it('follows the row destination from elsewhere on the row, but not from a nested link', async () => {
    const { unmount } = renderTable(
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(row) => row.id}
        emptyTitle="Nenhum equipamento encontrado"
        getRowHref={(row) => `/equipamentos/${row.id}`}
      />,
    )
    await userEvent.click(screen.getByRole('link', { name: 'Sofia Sample' }))
    expect(screen.getByText('Detalhe do consultor')).toBeInTheDocument()
    unmount()

    renderTable(
      <DataTable
        columns={[...columns, { key: 'type', header: 'Tipo', render: () => 'Portátil' }]}
        rows={rows.slice(0, 1)}
        rowKey={(row) => row.id}
        emptyTitle="Nenhum equipamento encontrado"
        getRowHref={(row) => `/equipamentos/${row.id}`}
      />,
    )
    await userEvent.click(screen.getByText('Portátil'))
    expect(screen.getByText('Detalhe do equipamento')).toBeInTheDocument()
  })

  it('can put the row link on a chosen column', () => {
    renderTable(
      <DataTable
        columns={[{ key: 'type', header: 'Tipo', render: () => 'Portátil' }, ...columns]}
        rows={rows.slice(0, 1)}
        rowKey={(row) => row.id}
        emptyTitle="Nenhum equipamento encontrado"
        getRowHref={(row) => `/equipamentos/${row.id}`}
        rowLinkColumn="assetTag"
      />,
    )
    expect(screen.getByRole('link', { name: 'EQ-NB-0412' })).toHaveAttribute(
      'href',
      '/equipamentos/e1',
    )
    expect(screen.queryByRole('link', { name: 'Portátil' })).not.toBeInTheDocument()
  })

  it('shows emptyIcon in the icon tile and does not treat it as an action', () => {
    renderTable(
      <DataTable
        columns={columns}
        rows={[]}
        rowKey={(row) => row.id}
        emptyTitle="Nenhum equipamento encontrado"
        emptyDescription="Ajuste os filtros ou registe um novo equipamento."
        emptyIcon={<Laptop data-testid="empty-icon" />}
      />,
    )
    expect(screen.getByText('Nenhum equipamento encontrado')).toBeInTheDocument()
    expect(screen.getByTestId('empty-icon')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('keeps the error state with retry', async () => {
    const onRetry = vi.fn()
    renderTable(
      <DataTable
        columns={columns}
        rows={[]}
        rowKey={(row) => row.id}
        emptyTitle="—"
        error={new Error('boom')}
        onRetry={onRetry}
      />,
    )
    expect(screen.getByRole('alert')).toHaveTextContent('Não foi possível carregar os dados')
    await userEvent.click(screen.getByRole('button', { name: 'Tentar novamente' }))
    expect(onRetry).toHaveBeenCalledOnce()
  })

  it('keeps server pagination semantics', async () => {
    const onPageChange = vi.fn()
    renderTable(
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(row) => row.id}
        emptyTitle="—"
        pagination={{ page: 1, pageSize: 20, totalCount: 45, onPageChange }}
      />,
    )
    expect(screen.getByText('1–20 de 45')).toBeInTheDocument()
    expect(screen.getByText('1 / 3')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Página anterior' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Página seguinte' }))
    expect(onPageChange).toHaveBeenCalledWith(2)
  })

  it('hides secondary-priority columns below 1280px only', () => {
    renderTable(
      <DataTable
        columns={[
          columns[0]!,
          { key: 'sn', header: 'Nº de série', priority: 'secondary', render: () => 'SNDEMO01' },
        ]}
        rows={rows}
        rowKey={(row) => row.id}
        emptyTitle="—"
      />,
    )
    expect(screen.getByRole('columnheader', { name: 'Nº de série' })).toHaveClass(
      'hidden',
      'xl:table-cell',
    )
    expect(screen.getByRole('columnheader', { name: 'Identificador' })).not.toHaveClass('hidden')
  })

  it('renders stacked mobile rows as one link each, and keeps the table for wider screens', async () => {
    renderTable(
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(row) => row.id}
        emptyTitle="—"
        getRowHref={(row) => `/equipamentos/${row.id}`}
        renderMobileRow={(row) => <span>{`${row.assetTag} · ${row.consultantName}`}</span>}
      />,
    )
    const mobileLink = screen.getByRole('link', { name: 'EQ-NB-0412 · Sofia Sample' })
    expect(mobileLink).toHaveAttribute('href', '/equipamentos/e1')
    expect(mobileLink.closest('ul')).toHaveClass('md:hidden')
    expect(screen.getByRole('table').parentElement).toHaveClass('hidden', 'md:block')
    await userEvent.click(mobileLink)
    expect(screen.getByText('Detalhe do equipamento')).toBeInTheDocument()
  })
})

describe('DataTable — wide comparison tables', () => {
  it('scrolls a wide table inside its own focusable, labelled region and keeps pagination outside it', () => {
    renderTable(
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(row) => row.id}
        emptyTitle="Sem dados"
        minTableWidth="900px"
        scrollLabel="Linhas importadas"
        pagination={{ page: 1, pageSize: 20, totalCount: 2, onPageChange: () => {} }}
      />,
    )

    const region = screen.getByRole('region', { name: 'Linhas importadas' })
    expect(region).toHaveAttribute('tabindex', '0')
    // Positioned, so absolutely positioned content (sr-only text) cannot widen the page.
    expect(region).toHaveClass('overflow-x-auto', 'relative')
    expect(screen.getByRole('table')).toHaveStyle({ minWidth: '900px' })
    expect(region).not.toContainElement(screen.getByRole('button', { name: 'Página seguinte' }))
  })

  it('without minTableWidth, nothing changes (no region, no min width)', () => {
    renderTable(<DataTable columns={columns} rows={rows} rowKey={(row) => row.id} emptyTitle="Sem dados" />)
    expect(screen.queryByRole('region')).not.toBeInTheDocument()
    expect(screen.getByRole('table').getAttribute('style')).toBeNull()
  })
})
