import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { StatusBadge } from './StatusBadge'
import * as statusMaps from '../status/statusMaps'
import type { StatusMeta } from '../status/statusMaps'

function isStatusMap(value: unknown): value is Record<string, StatusMeta> {
  return (
    typeof value === 'object' &&
    value !== null &&
    Object.values(value).every(
      (entry) => typeof entry === 'object' && entry !== null && 'tone' in entry,
    )
  )
}

const maps = Object.entries(statusMaps).filter(
  (entry): entry is [string, Record<string, StatusMeta>] => isStatusMap(entry[1]),
)

const allStatuses = maps.flatMap(([name, map]) =>
  Object.entries(map).map(([key, meta]) => [`${name}.${key}`, meta] as const),
)

describe('StatusBadge', () => {
  it('covers every status map in statusMaps.ts', () => {
    expect(maps.map(([name]) => name).sort()).toEqual(
      [
        'allocationStatusMeta',
        'assignmentStatusMeta',
        'damageStatusMeta',
        'deliveryStatusMeta',
        'equipmentConditionMeta',
        'equipmentRetirementRequestStatusMeta',
        'equipmentStatusMeta',
        'executionStatusMeta',
        'handoverOperationalStatusMeta',
        'handoverStatusMeta',
        'importSessionStatusMeta',
        'maintenanceOutcomeMeta',
        'maintenanceStatusMeta',
        'photoSetSourceMeta',
        'responsibilityTermStatusMeta',
        'returnStatusMeta',
      ].sort(),
    )
  })

  it.each(allStatuses)('%s renders its label with an icon', (_key, meta) => {
    const { container } = render(
      <StatusBadge label={meta.label} tone={meta.tone} icon={meta.icon} />,
    )
    expect(screen.getByText(meta.label)).toBeInTheDocument()
    expect(container.querySelector('svg[aria-hidden="true"]')).not.toBeNull()
  })

  it('shows "Avariado" for Damaged, as a badge with its own icon', () => {
    const damaged = statusMaps.equipmentStatusMeta.Damaged!
    expect(damaged.label).toBe('Avariado')
    const { container } = render(<StatusBadge {...damaged} />)
    expect(screen.getByText('Avariado')).toHaveClass('border-danger-border')
    expect(container.querySelector('svg')).toHaveClass('lucide-triangle-alert')
  })

  it('gives the eight lifecycle statuses eight distinct icons', () => {
    const icons = Object.values(statusMaps.equipmentStatusMeta).map((meta) => meta.icon)
    expect(icons).toHaveLength(8)
    expect(icons.every(Boolean)).toBe(true)
    expect(new Set(icons).size).toBe(8)
  })

  it('keeps settled tones quiet (no container) and attention tones as badges', () => {
    render(
      <>
        <StatusBadge label="Disponível" tone="success" />
        <StatusBadge label="Devolução pendente" tone="warning" />
      </>,
    )
    expect(screen.getByText('Disponível')).not.toHaveClass('border')
    expect(screen.getByText('Devolução pendente')).toHaveClass('border', 'border-warning-border')
  })

  it('falls back to a tone icon when a status has none', () => {
    const { container } = render(<StatusBadge label="Rejeitado" tone="neutral" />)
    expect(container.querySelector('svg')).not.toBeNull()
  })
})
