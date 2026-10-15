import type { StatusTone } from '../../shared/components/StatusBadge'
import type { ImportRowStatus } from './types'

/** Row-level status (Valid/Warning/Error) — distinct from the session-level ImportSessionStatus in statusMaps.ts. */
export const importRowStatusMeta: Record<ImportRowStatus, { label: string; tone: StatusTone }> = {
  Valid: { label: 'Válida', tone: 'success' },
  Warning: { label: 'Aviso', tone: 'warning' },
  Error: { label: 'Erro', tone: 'danger' },
}
