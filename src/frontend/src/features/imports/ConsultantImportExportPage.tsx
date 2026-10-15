import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
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
import * as importsApi from './api'
import * as exportsApi from '../exports/api'
import type { ConsultantImportRow, ImportConfirmResponse, ImportPreviewResponse } from './types'
import { importRowStatusMeta } from './rowStatusMeta'
import { ImportIssuesCell } from './ImportIssuesCell'
import { ImportSummaryStats } from './ImportSummaryStats'
import { ImportAlert, ImportSection, ImportSteps } from './ImportWorkflow'
import { consultantImportSuccessMessage, importFileNames, importErrorMessage, importPreviewSummary } from './importMessages'

export function ConsultantImportExportPage() {
  const { hasPermission } = useAuth()
  const canImport = hasPermission(Permissions.Consultant.Import)
  const canExport = hasPermission(Permissions.Consultant.Export)

  return (
    <div className="flex flex-col">
      <PageHeader
        title="Consultores: Importação e exportação"
        description="Importe consultores em lote a partir de Excel ou exporte a lista atual."
        breadcrumbs={[{ label: 'Importação e exportação' }, { label: 'Consultores' }]}
      />
      <div className="flex flex-col px-4 pb-6 sm:px-6">
        {canImport && <ConsultantImportSection />}
        {canExport && <ConsultantExportSection />}
        {!canImport && !canExport && (
          <p className="py-5 text-[13px] text-fg-muted">Sem permissões de importação ou exportação de consultores.</p>
        )}
      </div>
    </div>
  )
}

function ConsultantImportSection() {
  const [preview, setPreview] = useState<ImportPreviewResponse<ConsultantImportRow> | null>(null)
  const [confirmResult, setConfirmResult] = useState<ImportConfirmResponse | null>(null)
  const [staleNotice, setStaleNotice] = useState<string | null>(null)

  const templateMutation = useMutation({
    mutationFn: () => importsApi.getImportTemplate('consultants'),
    onSuccess: (blob) => saveBlob(blob, importFileNames.consultantsTemplate),
  })

  const previewMutation = useMutation({
    mutationFn: (file: File) => importsApi.previewImport('consultants', file),
    onSuccess: (result) => {
      setPreview(result)
      setConfirmResult(null)
      setStaleNotice(null)
    },
  })

  const confirmMutation = useMutation({
    mutationFn: () => importsApi.confirmImport('consultants', preview!.importSessionId),
    onSuccess: (result) => {
      setConfirmResult(result)
      setPreview(null)
    },
    onError: async (error) => {
      if (error instanceof ApiError && error.status === 409 && preview) {
        const refreshed = await importsApi.getImportSession('consultants', preview.importSessionId)
        setPreview(refreshed)
        setStaleNotice(
          'Alguns dados mudaram desde a pré-visualização (por exemplo, um NIF deixou de ser único). Reveja as linhas assinaladas antes de confirmar novamente.',
        )
      }
    },
  })

  const cancelMutation = useMutation({
    mutationFn: () => importsApi.cancelImport('consultants', preview!.importSessionId),
    onSuccess: () => {
      setPreview(null)
      setStaleNotice(null)
    },
  })

  const columns: DataTableColumn<ConsultantImportRow>[] = [
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
    { key: 'fullName', header: 'Nome completo', render: (row) => row.fullName },
    { key: 'nif', header: 'NIF', width: '104px', render: (row) => <span className="font-mono text-[12px]">{row.nif}</span> },
    { key: 'email', header: 'Email', render: (row) => row.email },
    {
      key: 'allocation',
      header: 'Alocação',
      render: (row) =>
        row.hasAllocation ? (
          <span className="text-fg-muted">
            {row.roleOrFunction ?? '—'}
            {row.company ? ` · ${row.company}` : ''}
          </span>
        ) : (
          <span className="text-fg-subtle">Sem alocação</span>
        ),
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
      id="consultant-import"
      title="Importar consultores"
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
          <p className="text-fg-muted">{consultantImportSuccessMessage(confirmResult)}</p>
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

function ConsultantExportSection() {
  const [search, setSearch] = useState('')
  const [isActive, setIsActive] = useState('')
  const [workforceType, setWorkforceType] = useState('')

  const exportMutation = useMutation({
    mutationFn: () =>
      exportsApi.exportConsultants({
        search: search || undefined,
        isActive: isActive ? isActive === 'true' : undefined,
        workforceType: workforceType || undefined,
      }),
    onSuccess: (blob) => saveBlob(blob, importFileNames.consultantsExport),
  })

  return (
    <ImportSection
      id="consultant-export"
      title="Exportar consultores"
      description="Gera um ficheiro Excel com os consultores que correspondem aos filtros."
    >
      <div className="grid grid-cols-1 items-end gap-3 sm:grid-cols-[minmax(0,1fr)_160px_160px_auto]">
        <FormField id="export-consultant-search" label="Pesquisar">
          <Input id="export-consultant-search" placeholder="Nome…" value={search} onChange={(e) => setSearch(e.target.value)} />
        </FormField>
        <FormField id="export-consultant-active" label="Estado">
          <Select id="export-consultant-active" value={isActive} onChange={(e) => setIsActive(e.target.value)}>
            <option value="">Todos</option>
            <option value="true">Ativos</option>
            <option value="false">Inativos</option>
          </Select>
        </FormField>
        <FormField id="export-consultant-workforce" label="Regime">
          <Select id="export-consultant-workforce" value={workforceType} onChange={(e) => setWorkforceType(e.target.value)}>
            <option value="">Todos</option>
            <option value="Internal">Interno</option>
            <option value="External">Externo</option>
          </Select>
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
