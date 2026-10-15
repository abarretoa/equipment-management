import { ApiError } from '../../shared/api/httpClient'
import type { CurrentInventoryImportConfirmResponse, ImportConfirmResponse, ImportPreviewResponse } from './types'

/** Download names: pt-PT, ASCII-only so every browser and file system keeps them intact. */
export const importFileNames = {
  consultantsTemplate: 'Modelo_Importacao_Consultores.xlsx',
  equipmentTemplate: 'Modelo_Importacao_Equipamentos.xlsx',
  currentInventoryTemplate: 'Modelo_Importacao_Inventario_Atual.xlsx',
  consultantsExport: 'Consultores.xlsx',
  equipmentExport: 'Equipamentos.xlsx',
} as const

const count = (value: number | undefined) => value ?? 0

export function consultantImportSuccessMessage(result: ImportConfirmResponse): string {
  const consultants = count(result.createdConsultants)
  const allocations = count(result.createdAllocations)
  const imported = consultants === 1 ? 'Foi importado 1 consultor' : `Foram importados ${consultants} consultores`
  if (allocations === 0) return `${imported}.`
  return allocations === 1 ? `${imported} e criada 1 alocação.` : `${imported} e criadas ${allocations} alocações.`
}

export function equipmentImportSuccessMessage(result: ImportConfirmResponse): string {
  const equipment = count(result.createdEquipment)
  return equipment === 1 ? 'Foi importado 1 equipamento.' : `Foram importados ${equipment} equipamentos.`
}

/** What the preview means for the person importing: blocked by errors, importable with warnings, or clean. */
export function importPreviewSummary(preview: Pick<ImportPreviewResponse<unknown>, 'errorRows' | 'warningRows'>): string {
  if (preview.errorRows > 0) {
    const rows = preview.errorRows === 1 ? '1 linha apresenta erros' : `${preview.errorRows} linhas apresentam erros`
    return `${rows}. Corrija o ficheiro e carregue-o novamente: nada é importado enquanto existirem erros.`
  }
  if (preview.warningRows > 0) {
    return preview.warningRows === 1
      ? '1 linha tem avisos. Será importada mesmo assim.'
      : `${preview.warningRows} linhas têm avisos. Serão importadas mesmo assim.`
  }
  return 'Todas as linhas são válidas.'
}

const plural = (value: number, one: string, many: string) => (value === 1 ? `1 ${one}` : `${value} ${many}`)

export function currentInventoryImportSuccessMessage(result: CurrentInventoryImportConfirmResponse): string {
  const parts = [
    plural(result.createdEquipment, 'equipamento registado', 'equipamentos registados'),
    plural(result.createdAssignments, 'atribuição atual', 'atribuições atuais'),
    plural(result.createdConsultants, 'consultor novo', 'consultores novos'),
    plural(result.createdAllocations, 'alocação nova', 'alocações novas'),
  ]
  if (result.reusedConsultants > 0) parts.push(plural(result.reusedConsultants, 'consultor existente reutilizado', 'consultores existentes reutilizados'))
  return `${parts.join(', ')}. Não foi enviado nenhum email.`
}

/** The server's own message for an expected (4xx) failure; a generic one otherwise — never raw internals. */
export function importErrorMessage(error: unknown, fallback: string): string {
  return error instanceof ApiError && error.status < 500 ? error.message : fallback
}
