export type ResponsibilityTermStatus = 'Generated' | 'Superseded'
export type ExecutionStatus = 'NotSent' | 'SendPending' | 'Sent' | 'Signed'
export type DeliveryStatus = 'Attempting' | 'Sent' | 'Failed'

export interface ResponsibilityTermSignature {
  signedOn?: string | null
  uploadedAtUtc: string
  sizeBytes: number
  sha256: string
  uploadedByDisplayNameSnapshot: string
}

export interface ResponsibilityTerm {
  id: string
  equipmentHandoverId: string
  documentNumber: string
  version: number
  status: ResponsibilityTermStatus
  templateVersion: number
  generatedAtUtc: string
  generatedByDisplayNameSnapshot: string
  fileName: string
  sizeBytes: number
  sha256: string
  createdAtUtc: string
  executionStatus: ExecutionStatus
  lastSentAtUtc?: string | null
  sendAttemptCount: number
  signature?: ResponsibilityTermSignature | null
}

export interface ResponsibilityTermDelivery {
  id: string
  recipientEmailSnapshot: string
  recipientOverridden: boolean
  status: DeliveryStatus
  attemptedAtUtc: string
  sentAtUtc?: string | null
  failureCode?: string | null
  sentByDisplayNameSnapshot: string
  createdAtUtc: string
}
