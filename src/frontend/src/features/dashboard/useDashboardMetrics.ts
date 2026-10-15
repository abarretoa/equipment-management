import { useQueries } from '@tanstack/react-query'
import { getEquipmentStats } from '../equipment/api'
import { listConsultants } from '../consultants/api'
import { listHandovers } from '../handovers/api'
import { listReturns } from '../returns/api'

/**
 * Equipment counts (by status + available-by-type) come from a single
 * `/api/equipment/stats` aggregate query (Phase 9 refinement) instead of
 * one `pageSize: 1` list call per status, which is what this used to do. The
 * remaining metrics have no equivalent aggregate and stay cheap
 * `pageSize: 1` calls against their existing paginated endpoints, reading
 * only `totalCount`.
 *
 * Each query is returned as-is — never collapsed into a number — so every
 * Dashboard section can render its own loading and error state instead of a
 * failed request reading as "0" (Visual System v1, Phase 3).
 */
export function useDashboardMetrics() {
  const [equipmentStats, consultantsActive, handoversDraft, returnsPending, returnsReceived] =
    useQueries({
      queries: [
        {
          queryKey: ['dashboard', 'equipment', 'stats'],
          queryFn: () => getEquipmentStats(),
        },
        {
          queryKey: ['dashboard', 'consultants', 'active'],
          queryFn: () => listConsultants({ isActive: true, page: 1, pageSize: 1 }),
        },
        {
          queryKey: ['dashboard', 'handovers', 'draft'],
          queryFn: () => listHandovers({ status: 'Draft', page: 1, pageSize: 1 }),
        },
        {
          queryKey: ['dashboard', 'returns', 'pending'],
          queryFn: () => listReturns({ status: 'Pending', page: 1, pageSize: 1 }),
        },
        {
          queryKey: ['dashboard', 'returns', 'received'],
          queryFn: () => listReturns({ status: 'Received', page: 1, pageSize: 1 }),
        },
      ],
    })

  return { equipmentStats, consultantsActive, handoversDraft, returnsPending, returnsReceived }
}
