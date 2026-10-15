export type EquipmentTypeKind = 'Generic' | 'Laptop' | 'MobilePhone'

export interface EquipmentType {
  id: string
  name: string
  kind: EquipmentTypeKind
  assetTagPrefix: string
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export interface EquipmentTypeListParams {
  search?: string
  isActive?: boolean
  page: number
  pageSize: number
}

export interface CreateEquipmentTypeValues {
  name: string
  kind: EquipmentTypeKind
  assetTagPrefix: string
}

export interface UpdateEquipmentTypeValues {
  name: string
  assetTagPrefix: string
}
