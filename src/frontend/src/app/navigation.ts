import {
  LayoutDashboard,
  Users,
  Building2,
  UserCog,
  Laptop,
  Tags,
  PackageCheck,
  Undo2,
  Wrench,
  FileSpreadsheet,
  ClipboardList,
  ScrollText,
  Archive,
  type LucideIcon,
} from 'lucide-react'
import { CurrentInventoryImportPermissions, Permissions } from '../shared/permissions'

export interface NavLink {
  to: string
  label: string
  icon: LucideIcon
  /** One permission, or several that are ALL required. */
  permission?: string | readonly string[]
}

export interface NavSection {
  label: string
  items: NavLink[]
}

/** Sidebar structure (Phase 9 scope, adapted: no standalone Responsibility Term list — the API only exposes terms per-handover). */
export const navSections: NavSection[] = [
  {
    label: '',
    items: [{ to: '/', label: 'Dashboard', icon: LayoutDashboard }],
  },
  {
    label: 'Pessoas',
    items: [
      { to: '/consultores', label: 'Consultores', icon: Users, permission: Permissions.Consultant.Read },
      { to: '/empresas', label: 'Empresas', icon: Building2, permission: Permissions.Company.Read },
      { to: '/utilizadores', label: 'Utilizadores', icon: UserCog, permission: Permissions.User.Read },
    ],
  },
  {
    label: 'Equipamentos',
    items: [
      { to: '/equipamentos', label: 'Equipamentos', icon: Laptop, permission: Permissions.Equipment.Read },
      { to: '/tipos-equipamento', label: 'Tipos de equipamento', icon: Tags, permission: Permissions.EquipmentType.Read },
    ],
  },
  {
    label: 'Operações',
    items: [
      { to: '/entregas', label: 'Entregas', icon: PackageCheck, permission: Permissions.EquipmentHandover.Read },
      { to: '/devolucoes', label: 'Devoluções', icon: Undo2, permission: Permissions.EquipmentReturn.Read },
      { to: '/manutencao', label: 'Manutenção', icon: Wrench, permission: Permissions.EquipmentMaintenance.Read },
    ],
  },
  {
    label: 'Importação e exportação',
    items: [
      { to: '/importacao-exportacao/consultores', label: 'Consultores', icon: FileSpreadsheet, permission: Permissions.Consultant.Import },
      { to: '/importacao-exportacao/equipamentos', label: 'Equipamentos', icon: FileSpreadsheet, permission: Permissions.Equipment.Import },
      { to: '/importacao-exportacao/inventario-atual', label: 'Inventário atual', icon: ClipboardList, permission: CurrentInventoryImportPermissions },
    ],
  },
  {
    label: 'Administração',
    items: [
      { to: '/solicitacoes-abate', label: 'Solicitações de abate', icon: Archive, permission: Permissions.EquipmentRetirementRequest.Read },
      { to: '/auditoria', label: 'Auditoria', icon: ScrollText, permission: Permissions.Audit.Read },
    ],
  },
]
