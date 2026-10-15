export interface AuditLog {
  id: string
  actorUserId?: string | null
  actorDisplayNameSnapshot?: string | null
  action: string
  entityType: string
  entityId?: string | null
  occurredAtUtc: string
  correlationId?: string | null
  ipAddress?: string | null
  oldValuesJson?: string | null
  newValuesJson?: string | null
  metadataJson?: string | null
}

export interface AuditListParams {
  actorUserId?: string
  action?: string
  entityType?: string
  entityId?: string
  fromUtc?: string
  toUtc?: string
  page: number
  pageSize: number
}
