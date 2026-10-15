/**
 * Administrator-facing wording for audit records. Presentation only: the raw
 * codes stay exactly as stored and returned by the API (and are what the
 * filters send). Mirrors EquipmentManagement.Domain/Auditing/AuditActions.cs.
 */
export const auditActionLabels: Readonly<Record<string, string>> = {
  // Sessão e conta
  'Auth.LoginSucceeded': 'Início de sessão',
  'Auth.LoginFailed': 'Início de sessão falhado',
  'Auth.Logout': 'Fim de sessão',
  'Auth.PasswordChanged': 'Palavra-passe alterada',
  'Auth.AdminPasswordResetRequested': 'Palavra-passe reposta pela Administração',
  'System.AdministrationUserBootstrapped': 'Utilizador de Administração inicial criado',

  // Utilizadores
  'User.Created': 'Utilizador criado',
  'User.Updated': 'Utilizador atualizado',
  'User.Activated': 'Utilizador ativado',
  'User.Deactivated': 'Utilizador desativado',
  'User.RoleChanged': 'Função do utilizador alterada',
  'User.ProfileUpdated': 'Perfil atualizado pelo próprio',

  // Empresas, consultores e alocações
  'Company.Created': 'Empresa criada',
  'Company.Updated': 'Empresa atualizada',
  'Company.Activated': 'Empresa ativada',
  'Company.Deactivated': 'Empresa desativada',
  'Consultant.Created': 'Consultor criado',
  'Consultant.Updated': 'Consultor atualizado',
  'Consultant.Activated': 'Consultor ativado',
  'Consultant.Deactivated': 'Consultor desativado',
  'ConsultantAllocation.Created': 'Alocação criada',
  'ConsultantAllocation.Updated': 'Alocação atualizada',
  'ConsultantAllocation.Ended': 'Alocação terminada',
  'ConsultantAllocation.CollectionEstimateRecorded': 'Previsão de recolha registada',
  'ConsultantAllocation.EndOfEngagementReturnsInitiated': 'Devoluções de fim de vínculo iniciadas',
  'ConsultantAllocation.CollectionNotificationSent': 'Notificação de recolha enviada',
  'ConsultantAllocation.CollectionNotificationFailed': 'Falha no envio da notificação de recolha',

  // Tipos de equipamento e equipamentos
  'EquipmentType.Created': 'Tipo de equipamento criado',
  'EquipmentType.Updated': 'Tipo de equipamento atualizado',
  'EquipmentType.Activated': 'Tipo de equipamento ativado',
  'EquipmentType.Deactivated': 'Tipo de equipamento desativado',
  'Equipment.Created': 'Equipamento registado',
  'Equipment.Updated': 'Equipamento atualizado',
  'Equipment.Retired': 'Equipamento abatido',
  'EquipmentPhotoSet.Established': 'Fotografias atuais registadas',
  'EquipmentPhotoSet.Updated': 'Fotografias atuais atualizadas',
  'EquipmentPhotoSet.Replaced': 'Fotografias atuais do equipamento alteradas',
  'EquipmentRetirementRequest.Created': 'Abate solicitado',
  'EquipmentRetirementRequest.Approved': 'Solicitação de abate aprovada',
  'EquipmentRetirementRequest.Rejected': 'Solicitação de abate rejeitada',

  // Entregas
  'EquipmentHandover.Created': 'Entrega criada',
  'EquipmentHandover.ItemAdded': 'Equipamento adicionado à entrega',
  'EquipmentHandover.ItemRemoved': 'Equipamento removido da entrega',
  'EquipmentHandover.InspectionRecorded': 'Inspeção de entrega registada',
  'EquipmentHandover.PhotoUploaded': 'Fotografia de entrega adicionada',
  'EquipmentHandover.PhotoDeleted': 'Fotografia de entrega removida',
  'EquipmentHandover.Finalized': 'Entrega confirmada',
  'EquipmentHandover.DeliveryMethodChanged': 'Método de entrega alterado',
  'EquipmentHandover.DraftDeleted': 'Rascunho de entrega eliminado',
  'EquipmentDispatch.Registered': 'Envio registado',
  'EquipmentDispatch.NotificationSent': 'Notificação de envio enviada',
  'EquipmentDispatch.NotificationFailed': 'Falha no envio da notificação de envio',
  'EquipmentDispatch.Cancelled': 'Envio anulado',
  'EquipmentAssignment.Started': 'Equipamento atribuído',
  'EquipmentAssignment.Ended': 'Atribuição terminada',

  // Devoluções, inspeção, avarias e manutenção
  'EquipmentReturn.Created': 'Devolução iniciada',
  'EquipmentReturn.Received': 'Equipamento recebido',
  'EquipmentReturn.Completed': 'Devolução concluída',
  'EquipmentReturn.Cancelled': 'Devolução cancelada',
  'EquipmentReturnGroup.Created': 'Grupo de devoluções criado',
  'ReturnInspection.Recorded': 'Inspeção de devolução registada',
  'ReturnInspection.PhotoAdded': 'Fotografia de inspeção adicionada',
  'ReturnInspection.PhotoRemoved': 'Fotografia de inspeção removida',
  'ReturnInspection.Finalized': 'Inspeção de devolução concluída',
  'Damage.Recorded': 'Avaria registada',
  'Damage.PhotoAdded': 'Fotografia de avaria adicionada',
  'Damage.PhotoRemoved': 'Fotografia de avaria removida',
  'Damage.Resolved': 'Avaria resolvida',
  'Maintenance.Started': 'Manutenção iniciada',
  'Maintenance.Completed': 'Manutenção concluída',

  // Termo de responsabilidade
  'ResponsibilityTerm.Generated': 'Termo de responsabilidade gerado',
  'ResponsibilityTerm.Superseded': 'Termo de responsabilidade substituído',
  'ResponsibilityTerm.Downloaded': 'Termo de responsabilidade descarregado',
  'ResponsibilityTerm.Sent': 'Termo de responsabilidade enviado',
  'ResponsibilityTerm.SendFailed': 'Falha no envio do termo de responsabilidade',
  'ResponsibilityTerm.SignedDocumentUploaded': 'Termo assinado carregado',
  'ResponsibilityTerm.SignedDocumentDownloaded': 'Termo assinado descarregado',

  // Importação e exportação
  'ConsultantImport.Completed': 'Importação de consultores concluída',
  'ConsultantImport.Cancelled': 'Importação de consultores cancelada',
  'EquipmentImport.Completed': 'Importação de equipamentos concluída',
  'EquipmentImport.Cancelled': 'Importação de equipamentos cancelada',
  'CurrentInventoryImport.Completed': 'Importação do inventário atual concluída',
  'CurrentInventoryImport.Cancelled': 'Importação do inventário atual cancelada',
  'ConsultantExport.Generated': 'Exportação de consultores gerada',
  'EquipmentExport.Generated': 'Exportação de equipamentos gerada',
}

/** The human label, or the raw code itself when unmapped — an audit action is never hidden or invented. */
export function auditActionLabel(action: string): string {
  return auditActionLabels[action] ?? action
}

/** Filter groups for the "Ação" select, in the order the product is used. */
export const auditActionGroups: readonly { label: string; prefixes: readonly string[] }[] = [
  { label: 'Sessão e utilizadores', prefixes: ['Auth.', 'System.', 'User.'] },
  { label: 'Empresas, consultores e alocações', prefixes: ['Company.', 'Consultant.', 'ConsultantAllocation.'] },
  { label: 'Equipamentos', prefixes: ['EquipmentType.', 'Equipment.', 'EquipmentPhotoSet.', 'EquipmentRetirementRequest.'] },
  { label: 'Entregas', prefixes: ['EquipmentHandover.', 'EquipmentDispatch.', 'EquipmentAssignment.'] },
  { label: 'Devoluções e manutenção', prefixes: ['EquipmentReturn.', 'EquipmentReturnGroup.', 'ReturnInspection.', 'Damage.', 'Maintenance.'] },
  { label: 'Termo de responsabilidade', prefixes: ['ResponsibilityTerm.'] },
  { label: 'Importação e exportação', prefixes: ['ConsultantImport.', 'EquipmentImport.', 'CurrentInventoryImport.', 'ConsultantExport.', 'EquipmentExport.'] },
]

export function auditActionsIn(prefixes: readonly string[]): string[] {
  return Object.keys(auditActionLabels).filter((code) => prefixes.some((prefix) => code.startsWith(prefix)))
}

export const auditEntityTypeLabels: Readonly<Record<string, string>> = {
  User: 'Utilizador',
  Company: 'Empresa',
  Consultant: 'Consultor',
  ConsultantAllocation: 'Alocação',
  EquipmentType: 'Tipo de equipamento',
  Equipment: 'Equipamento',
  EquipmentHandover: 'Entrega',
  EquipmentReturn: 'Devolução',
  EquipmentReturnGroup: 'Grupo de devoluções',
  ResponsibilityTerm: 'Termo de responsabilidade',
  ImportSession: 'Sessão de importação',
}

export function auditEntityTypeLabel(entityType: string): string {
  return auditEntityTypeLabels[entityType] ?? entityType
}

/**
 * A link to the record's own page, only where the structured EntityType +
 * EntityId pair maps to an existing route — never derived from message text.
 */
const entityRoutes: Readonly<Record<string, (id: string) => string>> = {
  Equipment: (id) => `/equipamentos/${id}`,
  Consultant: (id) => `/consultores/${id}`,
  EquipmentHandover: (id) => `/entregas/${id}`,
  EquipmentReturn: (id) => `/devolucoes/${id}`,
  EquipmentReturnGroup: (id) => `/devolucoes/grupos/${id}`,
}

/** Events whose record no longer exists afterwards — linking to it would only open a "not found" page. */
const deletedEntityActions: ReadonlySet<string> = new Set(['EquipmentHandover.DraftDeleted'])

export function auditEntityHref(entityType: string, entityId?: string | null, action?: string): string | undefined {
  if (action && deletedEntityActions.has(action)) return undefined
  const route = entityRoutes[entityType]
  return route && entityId ? route(encodeURIComponent(entityId)) : undefined
}
