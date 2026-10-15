import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Download, FileSpreadsheet } from 'lucide-react'
import { PageHeader } from '../../shared/components/PageHeader'
import { Button } from '../../shared/components/Button'
import { Input } from '../../shared/components/Input'
import { Select } from '../../shared/components/Select'
import { FormField } from '../../shared/components/FormField'
import { FileUploadArea } from '../../shared/components/FileUploadArea'
import { DataTable, type DataTableColumn } from '../../shared/components/DataTable'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { ApiError, saveBlob } from '../../shared/api/httpClient'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import { equipmentStatusMeta } from '../../shared/status/statusMaps'
import { listEquipmentTypes } from '../equipment-types/api'
import * as importsApi from './api'
import * as exportsApi from '../exports/api'
import type { EquipmentImportRow, ImportConfirmResponse, ImportPreviewResponse } from './types'
import { importRowStatusMeta } from './rowStatusMeta'
import { ImportIssuesCell } from './ImportIssuesCell'
import { ImportSummaryStats } from './ImportSummaryStats'
import { ImportAlert, ImportSection, ImportSteps } from './ImportWorkflow'
import { equipmentImportSuccessMessage, importFileNames, importErrorMessage, importPreviewSummary } from './importMessages'

const EQUIPMENT_STATUS_OPTIONS = [
  'Available',
  'InTransit',
  'Assigned',
  'ReturnPending',
  'UnderInspection',
  'Damaged',
  'UnderMaintenance',
  'Retired',
] as const

export function EquipmentImportExportPage() {
  const { hasPermission } = useAuth()
  const canImport = hasPermission(Permissions.Equipment.Import)
  const canExport = hasPermission(Permissions.Equipment.Export)

  return (
    <div className="flex flex-col">
      <PageHeader
        title="Equipamentos: Importação e exportação"
        description="Importe equipamentos em lote a partir de Excel ou exporte a lista atual."
        breadcrumbs={[{ label: 'Importação e exportação' }, { label: 'Equipamentos' }]}
      />
      <div className="flex flex-col px-4 pb-6 sm:px-6">
        {canImport && <EquipmentImportSection />}
        {canExport && <EquipmentExportSection />}
        {!canImport && !canExport && (
          <p className="py-5 text-[13px] text-fg-muted">Sem permissões de importação ou exportação de equipamentos.</p>
        )}
      </div>
    </div>
  )
}

function EquipmentImportSection() {
  const [preview, setPreview] = useState<ImportPreviewResponse<EquipmentImportRow> | null>(null)
  const [confirmResult, setConfirmResult] = useState<ImportConfirmResponse | null>(null)
  const [staleNotice, setStaleNotice] = useState<string | null>(null)

  const templateMutation = useMutation({
    mutationFn: () => importsApi.getImportTemplate('equipment'),
    onSuccess: (blob) => saveBlob(blob, importFileNames.equipmentTemplate),
  })

  const previewMutation = useMutation({
    mutationFn: (file: File) => importsApi.previewImport('equipment', file),
    onSuccess: (result) => {
      setPreview(result)
      setConfirmResult(null)
      setStaleNotice(null)
    },
  })

  const confirmMutation = useMutation({
    mutationFn: () => importsApi.confirmImport('equipment', preview!.importSessionId),
    onSuccess: (result) => {
      setConfirmResult(result)
      setPreview(null)
    },
    onError: async (error) => {
      if (error instanceof ApiError && error.status === 409 && preview) {
        const refreshed = await importsApi.getImportSession('equipment', preview.importSessionId)
        setPreview(refreshed)
        setStaleNotice(
          'Alguns dados mudaram desde a pré-visualização (por exemplo, um número de série deixou de ser único). Reveja as linhas assinaladas antes de confirmar novamente.',
        )
      }
    },
  })

  const cancelMutation = useMutation({
    mutationFn: () => importsApi.cancelImport('equipment', preview!.importSessionId),
    onSuccess: () => {
      setPreview(null)
      setStaleNotice(null)
    },
  })

  const columns: DataTableColumn<EquipmentImportRow>[] = [
    { key: 'rowNumber', header: 'Linha', width: '64px', render: (row) => <span className="tabular-nums">{row.rowNumber}</span> },
    {
      key: 'status',
      header: 'Estado',
      width: '104px',
      render: (row) => {
        const meta = importRowStatusMeta[row.status]
        return <StatusBadge label={meta.label} tone={meta.tone} />
      },
    },
    { key: 'equipmentType', header: 'Tipo de equipamento', render: (row) => row.equipmentType },
    { key: 'manufacturer', header: 'Fabricante', render: (row) => row.manufacturer ?? '—' },
    { key: 'model', header: 'Modelo', render: (row) => row.model ?? '—' },
    {
      key: 'serialNumber',
      header: 'Número de série',
      render: (row) => (row.serialNumber ? <span className="font-mono text-[12px]">{row.serialNumber}</span> : '—'),
    },
    { key: 'issues', header: 'Problemas', width: '260px', render: (row) => <ImportIssuesCell issues={row.issues} /> },
  ]

  // A confirm 409 is handled above by refreshing the preview; any other failure is shown as is.
  const confirmError =
    confirmMutation.error && !(confirmMutation.error instanceof ApiError && confirmMutation.error.status === 409)
      ? importErrorMessage(confirmMutation.error, 'Não foi possível confirmar a importação.')
      : null

  return (
    <ImportSection
      id="equipment-import"
      title="Importar equipamentos"
      description="Carregue o ficheiro Excel preenchido a partir do modelo. Nada é importado antes de confirmar a pré-visualização."
      actions={
        <Button
          variant="secondary"
          size="sm"
          className="max-lg:h-11"
          icon={<Download className="size-3.5" aria-hidden />}
          loading={templateMutation.isPending}
          onClick={() => templateMutation.mutate()}
        >
          Descarregar modelo
        </Button>
      }
    >
      <ImportSteps stage={confirmResult ? 'done' : preview ? 'preview' : 'upload'} />

      {templateMutation.isError && (
        <ImportAlert tone="danger">{importErrorMessage(templateMutation.error, 'Não foi possível descarregar o modelo.')}</ImportAlert>
      )}

      {confirmResult && (
        <ImportAlert tone="success" title="Importação concluída">
          <p className="text-fg-muted">{equipmentImportSuccessMessage(confirmResult)}</p>
          <Button size="sm" variant="secondary" className="mt-1.5 w-fit max-lg:h-11" onClick={() => setConfirmResult(null)}>
            Nova importação
          </Button>
        </ImportAlert>
      )}

      {!preview && !confirmResult && (
        <FileUploadArea
          accept=".xlsx"
          hint="Máximo 5 MB, formato .xlsx"
          disabled={previewMutation.isPending}
          onFileSelected={(file) => previewMutation.mutate(file)}
        />
      )}

      {previewMutation.isPending && (
        <p className="text-[13px] text-fg-muted" role="status">
          A analisar o ficheiro…
        </p>
      )}

      {previewMutation.isError && (
        <ImportAlert tone="danger">
          {previewMutation.error instanceof ApiError ? previewMutation.error.message : 'Não foi possível processar o ficheiro.'}
        </ImportAlert>
      )}

      {preview && (
        <div className="flex flex-col gap-4">
          <ImportSummaryStats
            totalRows={preview.totalRows}
            validRows={preview.validRows}
            warningRows={preview.warningRows}
            errorRows={preview.errorRows}
          />

          {staleNotice && <ImportAlert tone="warning">{staleNotice}</ImportAlert>}

          <div className="rounded-md border border-border">
            <DataTable
              columns={columns}
              rows={preview.rows}
              rowKey={(row) => String(row.rowNumber)}
              emptyTitle="Sem linhas no ficheiro"
              minTableWidth="920px"
              scrollLabel="Linhas do ficheiro"
            />
          </div>

          <ImportAlert tone={preview.errorRows > 0 ? 'danger' : preview.warningRows > 0 ? 'warning' : 'success'}>
            {importPreviewSummary(preview)}
          </ImportAlert>

          {confirmError && <ImportAlert tone="danger">{confirmError}</ImportAlert>}
          {cancelMutation.isError && (
            <ImportAlert tone="danger">{importErrorMessage(cancelMutation.error, 'Não foi possível cancelar a importação.')}</ImportAlert>
          )}

          <div className="flex flex-col-reverse gap-2 border-t border-border pt-4 sm:flex-row sm:justify-end">
            <Button variant="secondary" className="max-sm:h-11" loading={cancelMutation.isPending} onClick={() => cancelMutation.mutate()}>
              Cancelar
            </Button>
            <Button
              variant="primary"
              className="max-sm:h-11"
              disabled={preview.errorRows > 0}
              loading={confirmMutation.isPending}
              onClick={() => confirmMutation.mutate()}
            >
              Confirmar importação
            </Button>
          </div>
        </div>
      )}
    </ImportSection>
  )
}

function EquipmentExportSection() {
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [equipmentTypeId, setEquipmentTypeId] = useState('')
  const [manufacturer, setManufacturer] = useState('')

  const equipmentTypesQuery = useQuery({
    queryKey: ['equipment-types', 'active-options'],
    queryFn: () => listEquipmentTypes({ isActive: true, page: 1, pageSize: 100 }),
  })

  const exportMutation = useMutation({
    mutationFn: () =>
      exportsApi.exportEquipment({
        search: search || undefined,
        status: status || undefined,
        equipmentTypeId: equipmentTypeId || undefined,
        manufacturer: manufacturer || undefined,
      }),
    onSuccess: (blob) => saveBlob(blob, importFileNames.equipmentExport),
  })

  return (
    <ImportSection
      id="equipment-export"
      title="Exportar equipamentos"
      description="Gera um ficheiro Excel com os equipamentos que correspondem aos filtros."
    >
      <div className="grid grid-cols-1 items-end gap-3 sm:grid-cols-2 lg:grid-cols-[minmax(0,1fr)_176px_176px_160px_auto]">
        <FormField id="export-equipment-search" label="Pesquisar">
          <Input
            id="export-equipment-search"
            placeholder="Identificador, modelo…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </FormField>
        <FormField id="export-equipment-status" label="Estado">
          <Select id="export-equipment-status" value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="">Todos</option>
            {EQUIPMENT_STATUS_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {equipmentStatusMeta[option]?.label ?? option}
              </option>
            ))}
          </Select>
        </FormField>
        <FormField id="export-equipment-type" label="Tipo">
          <Select id="export-equipment-type" value={equipmentTypeId} onChange={(e) => setEquipmentTypeId(e.target.value)}>
            <option value="">Todos</option>
            {(equipmentTypesQuery.data?.items ?? []).map((type) => (
              <option key={type.id} value={type.id}>
                {type.name}
              </option>
            ))}
          </Select>
        </FormField>
        <FormField id="export-equipment-manufacturer" label="Fabricante">
          <Input id="export-equipment-manufacturer" value={manufacturer} onChange={(e) => setManufacturer(e.target.value)} />
        </FormField>
        <Button
          variant="primary"
          className="max-sm:h-11"
          icon={<FileSpreadsheet className="size-4" aria-hidden />}
          loading={exportMutation.isPending}
          onClick={() => exportMutation.mutate()}
        >
          Exportar
        </Button>
      </div>
      {exportMutation.isError && (
        <ImportAlert tone="danger">{importErrorMessage(exportMutation.error, 'Não foi possível gerar a exportação.')}</ImportAlert>
      )}
    </ImportSection>
  )
}
