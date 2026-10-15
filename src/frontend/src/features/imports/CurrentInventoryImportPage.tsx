import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Download } from 'lucide-react'
import { PageHeader } from '../../shared/components/PageHeader'
import { Button } from '../../shared/components/Button'
import { FileUploadArea } from '../../shared/components/FileUploadArea'
import { DataTable, type DataTableColumn } from '../../shared/components/DataTable'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { ApiError, saveBlob } from '../../shared/api/httpClient'
import { workforceTypeLabels } from '../../shared/status/statusMaps'
import { currentInventory } from './api'
import type {
  CurrentInventoryImportConfirmResponse,
  CurrentInventoryImportPreviewResponse,
  CurrentInventoryImportRow,
  CurrentInventoryImportSummary,
} from './types'
import { importRowStatusMeta } from './rowStatusMeta'
import { ImportIssuesCell } from './ImportIssuesCell'
import { ImportSummaryStats } from './ImportSummaryStats'
import { ImportAlert, ImportSection, ImportSteps } from './ImportWorkflow'
import { currentInventoryImportSuccessMessage, importFileNames, importErrorMessage, importPreviewSummary } from './importMessages'
import { brand } from '../../branding/brand.config'

/**
 * "Importar inventário atual": Consultants, their current allocation, Equipment
 * and who currently holds it, from one workbook. Same Preview → Confirm flow as
 * the Consultant and Equipment imports, which stay available on their own pages.
 * The full rules live in the workbook's "Instruções" sheet.
 */
export function CurrentInventoryImportPage() {
  return (
    <div className="flex flex-col">
      <PageHeader
        title="Importar inventário atual"
        description="Importe consultores, equipamentos e as respetivas atribuições atuais através de um único ficheiro."
        breadcrumbs={[{ label: 'Importação e exportação' }, { label: 'Inventário atual' }]}
      />
      <div className="flex flex-col px-4 pb-6 sm:px-6">
        <CurrentInventoryImportSection />
      </div>
    </div>
  )
}

function CurrentInventoryImportSection() {
  const [preview, setPreview] = useState<CurrentInventoryImportPreviewResponse | null>(null)
  const [confirmResult, setConfirmResult] = useState<CurrentInventoryImportConfirmResponse | null>(null)
  const [staleNotice, setStaleNotice] = useState<string | null>(null)

  const templateMutation = useMutation({
    mutationFn: () => currentInventory.getTemplate(),
    onSuccess: (blob) => saveBlob(blob, importFileNames.currentInventoryTemplate),
  })

  const previewMutation = useMutation({
    mutationFn: (file: File) => currentInventory.preview(file),
    onSuccess: (result) => {
      setPreview(result)
      setConfirmResult(null)
      setStaleNotice(null)
    },
  })

  const confirmMutation = useMutation({
    mutationFn: () => currentInventory.confirm(preview!.importSessionId),
    onSuccess: (result) => {
      setConfirmResult(result)
      setPreview(null)
    },
    onError: (error) => {
      if (!(error instanceof ApiError) || error.status !== 409 || !preview) return
      // A stale preview comes back with every row re-validated against the
      // current data: show those, so the rows that changed are visible.
      const rows = (error.problem as { rows?: CurrentInventoryImportRow[] } | null)?.rows
      if (rows) {
        setPreview({
          ...preview,
          rows,
          validRows: rows.filter((r) => r.status === 'Valid').length,
          warningRows: rows.filter((r) => r.status === 'Warning').length,
          errorRows: rows.filter((r) => r.status === 'Error').length,
        })
      }
      setStaleNotice(
        `${error.message} Reveja as linhas assinaladas, corrija o ficheiro ou os dados da plataforma e carregue o ficheiro novamente.`,
      )
    },
  })

  const cancelMutation = useMutation({
    mutationFn: () => currentInventory.cancel(preview!.importSessionId),
    onSuccess: () => {
      setPreview(null)
      setStaleNotice(null)
    },
  })

  const columns: DataTableColumn<CurrentInventoryImportRow>[] = [
    { key: 'rowNumber', header: 'Linha', width: '64px', render: (row) => <span className="tabular-nums">{row.rowNumber}</span> },
    {
      key: 'status',
      header: 'Estado',
      width: '96px',
      render: (row) => {
        const meta = importRowStatusMeta[row.status]
        return <StatusBadge label={meta.label} tone={meta.tone} />
      },
    },
    { key: 'equipment', header: 'Equipamento', render: (row) => <EquipmentCell row={row} /> },
    { key: 'person', header: 'Pessoa', render: (row) => <PersonCell row={row} /> },
    { key: 'plan', header: 'O que vai acontecer', render: (row) => <PlanCell row={row} /> },
    { key: 'issues', header: 'Problemas', width: '260px', render: (row) => <ImportIssuesCell issues={row.issues} /> },
  ]

  // A confirm 409 is handled above (re-validated rows + notice); any other failure is shown as is.
  const confirmError =
    confirmMutation.error && !(confirmMutation.error instanceof ApiError && confirmMutation.error.status === 409)
      ? importErrorMessage(confirmMutation.error, 'Não foi possível confirmar a importação.')
      : null

  return (
    <ImportSection
      id="current-inventory-import"
      title="Importar inventário atual"
      description={`Inclui funcionários internos da ${brand.organizationName} e consultores externos, distinguidos pela coluna Regime. Nada é importado antes de confirmar a pré-visualização.`}
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
          <p className="text-fg-muted">{currentInventoryImportSuccessMessage(confirmResult)}</p>
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
          <PlanSummary summary={preview.summary} hasErrors={preview.errorRows > 0} />

          {staleNotice && <ImportAlert tone="warning">{staleNotice}</ImportAlert>}

          <div className="rounded-md border border-border">
            <DataTable
              columns={columns}
              rows={preview.rows}
              rowKey={(row) => String(row.rowNumber)}
              emptyTitle="Sem linhas no ficheiro"
              minTableWidth="980px"
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
              disabled={preview.errorRows > 0 || preview.totalRows === 0}
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

/** The planned outcome, per person for consultants/allocations — what HR is about to confirm. */
function PlanSummary({ summary, hasErrors }: { summary: CurrentInventoryImportSummary; hasErrors: boolean }) {
  const items: { label: string; value: number | string }[] = [
    { label: 'Consultores novos', value: summary.newConsultants },
    { label: 'Consultores existentes reutilizados', value: summary.reusedConsultants },
    { label: 'Alocações novas', value: summary.newAllocations },
    { label: 'Alocações existentes reutilizadas', value: summary.reusedAllocations },
    { label: 'Equipamentos novos', value: summary.equipmentToCreate },
    { label: 'Serão atribuídos', value: summary.equipmentToAssign },
    { label: 'Ficarão disponíveis', value: summary.equipmentToStayAvailable },
    { label: 'Internos / Externos', value: `${summary.internalConsultants} / ${summary.externalConsultants}` },
  ]

  return (
    <section aria-label="Resultado previsto" className="flex flex-col gap-2">
      <p className="text-[12px] font-medium text-fg-muted">
        {hasErrors ? 'Resultado previsto das linhas sem erros' : 'Resultado previsto da confirmação'}
      </p>
      <dl className="grid grid-cols-1 gap-x-6 gap-y-1.5 rounded-md border border-border px-3.5 py-3 sm:grid-cols-2 lg:grid-cols-4">
        {items.map((item) => (
          <div key={item.label} className="flex items-baseline justify-between gap-3">
            <dt className="text-[12.5px] text-fg-muted">{item.label}</dt>
            <dd className="text-[13px] font-semibold tabular-nums text-fg">{item.value}</dd>
          </div>
        ))}
      </dl>
    </section>
  )
}

function EquipmentCell({ row }: { row: CurrentInventoryImportRow }) {
  const description = [row.manufacturer, row.model].filter(Boolean).join(' ')
  const identifier = row.imei ? `IMEI ${row.imei}` : row.serialNumber ? `N.º série ${row.serialNumber}` : null
  return (
    <div className="flex flex-col">
      <span className="text-[13px] text-fg">{row.equipmentType || '—'}</span>
      {(description || identifier) && (
        <span className="text-[12px] text-fg-subtle">{[description, identifier].filter(Boolean).join(' · ')}</span>
      )}
    </div>
  )
}

function PersonCell({ row }: { row: CurrentInventoryImportRow }) {
  if (!row.willBeAssigned) {
    return <span className="text-[12.5px] text-fg-subtle">Sem pessoa (stock)</span>
  }

  const regime = row.workforceType ? workforceTypeLabels[row.workforceType] ?? row.workforceType : null
  return (
    <div className="flex flex-col">
      <span className="text-[13px] text-fg">{row.fullName || '—'}</span>
      <span className="text-[12px] text-fg-subtle">
        {[row.nif ? `NIF ${row.nif}` : null, regime ? `Regime: ${regime}` : null, row.company].filter(Boolean).join(' · ')}
      </span>
    </div>
  )
}

function PlanCell({ row }: { row: CurrentInventoryImportRow }) {
  if (row.status === 'Error') {
    return <span className="text-[12.5px] text-fg-subtle">Nada, enquanto houver erros</span>
  }

  const badges: { label: string; tone: 'success' | 'info' | 'neutral' | 'primary' }[] = [{ label: 'Equipamento novo', tone: 'success' }]
  if (row.willBeAssigned) {
    if (row.consultantAction === 'Create') badges.push({ label: 'Consultor novo', tone: 'success' })
    if (row.consultantAction === 'Reuse') badges.push({ label: 'Consultor existente', tone: 'neutral' })
    if (row.allocationAction === 'Create') badges.push({ label: 'Alocação nova', tone: 'success' })
    if (row.allocationAction === 'Reuse') badges.push({ label: 'Alocação existente', tone: 'neutral' })
    badges.push({ label: 'Será atribuído', tone: 'primary' })
  } else {
    badges.push({ label: 'Ficará disponível', tone: 'info' })
  }

  return (
    <div className="flex flex-wrap gap-1">
      {badges.map((badge) => (
        <StatusBadge key={badge.label} label={badge.label} tone={badge.tone} />
      ))}
    </div>
  )
}
