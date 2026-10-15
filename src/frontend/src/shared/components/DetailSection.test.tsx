import { MemoryRouter } from 'react-router'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Image } from 'lucide-react'
import { DetailSection } from './DetailSection'
import { KeyValueList } from './KeyValueList'
import { PageHeader } from './PageHeader'
import { EmptyState } from './EmptyState'
import { StepIndicator } from './StepIndicator'
import { Button } from './Button'

describe('DetailSection', () => {
  it('keeps the panel variant by default (every current caller)', () => {
    const { container } = render(
      <DetailSection title="Resumo" actions={<Button size="sm">Ver</Button>}>
        <p>Conteúdo</p>
      </DetailSection>,
    )
    expect(screen.getByRole('heading', { level: 2, name: 'Resumo' })).toBeInTheDocument()
    expect(container.querySelector('section')).toHaveClass('rounded-lg', 'border')
    expect(screen.getByRole('button', { name: 'Ver' })).toBeInTheDocument()
  })

  it('offers the ruled variant with a label column', () => {
    const { container } = render(
      <DetailSection
        variant="ruled"
        title="Fotografias atuais"
        description="Pertencem ao equipamento."
      >
        <KeyValueList items={[{ label: 'Origem', value: 'Devolução' }]} />
      </DetailSection>,
    )
    const section = container.querySelector('section')
    expect(section).toHaveClass('md:grid-cols-[184px_minmax(0,1fr)]', 'border-b')
    expect(section).not.toHaveClass('rounded-lg')
    expect(screen.getByText('Pertencem ao equipamento.')).toBeInTheDocument()
    expect(screen.getByRole('term')).toHaveTextContent('Origem')
    expect(screen.getByRole('definition')).toHaveTextContent('Devolução')
  })
})

describe('PageHeader', () => {
  it('renders breadcrumbs as an ordered trail with the current page marked', () => {
    render(
      <MemoryRouter>
        <PageHeader
          title="EQ-NB-0412"
          breadcrumbs={[{ label: 'Equipamentos', to: '/equipamentos' }, { label: 'EQ-NB-0412' }]}
          actions={<Button>Editar</Button>}
        />
      </MemoryRouter>,
    )
    const trail = screen.getByRole('navigation', { name: 'Localização' })
    expect(screen.getByRole('link', { name: 'Equipamentos' })).toHaveAttribute(
      'href',
      '/equipamentos',
    )
    expect(trail.querySelector('[aria-current="page"]')).toHaveTextContent('EQ-NB-0412')
    expect(screen.getByRole('heading', { level: 1, name: 'EQ-NB-0412' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Editar' })).toBeInTheDocument()
  })
})

describe('EmptyState', () => {
  it('shows icon tile, title, guidance and an optional real action', () => {
    render(
      <EmptyState
        tone="warning"
        icon={<Image data-testid="icon" />}
        title="Fotografias atuais não registadas."
        description="São exigidas exatamente 3 fotografias."
        action={<Button>Registar fotografias</Button>}
      />,
    )
    expect(screen.getByTestId('icon')).toBeInTheDocument()
    expect(screen.getByText('Fotografias atuais não registadas.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Registar fotografias' })).toBeInTheDocument()
  })
})

describe('StepIndicator', () => {
  it('keeps done/current/upcoming states and marks the current step', () => {
    render(
      <StepIndicator
        steps={[
          { key: 'preparation', label: 'Preparação' },
          { key: 'dispatch', label: 'Envio' },
          { key: 'delivery', label: 'Entrega' },
        ]}
        currentKey="dispatch"
        completedKeys={['preparation']}
      />,
    )
    const list = screen.getByRole('list', { name: 'Progresso do processo' })
    expect(list.querySelector('[aria-current="step"]')).toHaveTextContent('Envio')
    expect(screen.getByText('(concluído)')).toBeInTheDocument()
    expect(screen.getAllByRole('listitem')).toHaveLength(3)
  })
})
