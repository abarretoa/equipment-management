import type { EquipmentAssignment } from '../assignments/types'

export type WorkforceType = 'Internal' | 'External'
export type EngagementType = 'ServiceProvider' | 'EmploymentContract'
export type AllocationStatus = 'Active' | 'Ended'

export interface ConsultantSummary {
  id: string
  fullName: string
  isActive: boolean
  createdAtUtc: string
  currentWorkforceType?: WorkforceType | null
  currentCompanyId?: string | null
  currentCompanyName?: string | null
  currentEngagementType?: EngagementType | null
  currentRoleOrFunction?: string | null
}

export interface ConsultantAllocation {
  id: string
  consultantId: string
  companyId?: string | null
  companyName?: string | null
  workforceType: WorkforceType
  engagementType: EngagementType
  roleOrFunction: string
  startDate: string
  endDate?: string | null
  status: AllocationStatus
  isEndOfEngagement: boolean
  expectedCollectionFromDate?: string | null
  expectedCollectionToDate?: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ConsultantDetail {
  id: string
  fullName: string
  nif: string
  email: string
  address: string
  phone: string
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string
  currentAllocation?: ConsultantAllocation | null
  allocationHistory: ConsultantAllocation[]
  currentAssignedEquipment: EquipmentAssignment[]
}

export interface ConsultantListParams {
  search?: string
  isActive?: boolean
  nif?: string
  workforceType?: WorkforceType
  companyId?: string
  /** Only Consultants eligible for a new handover (at least one Active allocation). Used by the Handover creation picker only. */
  hasActiveAllocation?: boolean
  /** Only Consultants who currently hold equipment (an Active assignment), whatever their allocation. Used by the manual-return picker only. */
  hasActiveCustody?: boolean
  page: number
  pageSize: number
}

export interface ConsultantFormValues {
  fullName: string
  nif: string
  email: string
  address: string
  phone: string
}

export interface CreateAllocationValues {
  workforceType: WorkforceType
  engagementType: EngagementType
  roleOrFunction: string
  companyId?: string
  startDate: string
  endDate?: string
}

/**
 * `isEndOfEngagement` is required on every call — ending an allocation is not
 * always offboarding (a consultant can end one allocation while starting
 * another), so neither the collection notification nor the automatic return
 * initiation may fire just because an end date was supplied.
 * `expectedCollectionFromDate` is required only when `isEndOfEngagement` is
 * true and the consultant currently holds active equipment.
 */
export interface EndAllocationValues {
  endDate: string
  isEndOfEngagement: boolean
  expectedCollectionFromDate?: string
  expectedCollectionToDate?: string
}

/**
 * The ended allocation plus what an end of engagement did with the
 * consultant's equipment: returns it initiated, and returns that were already
 * pending and left untouched. Both are 0 when the ending was not an end of
 * engagement or the consultant held no equipment.
 */
export interface EndAllocationResult extends ConsultantAllocation {
  returnsCreatedCount: number
  returnsAlreadyPendingCount: number
  /** The group the initiated returns belong to; null when none was initiated. */
  returnGroupId?: string | null
}
