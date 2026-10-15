import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, FileText, Send, Upload } from 'lucide-react'
import { DetailSection } from '../../shared/components/DetailSection'
import { Button } from '../../shared/components/Button'
import { ConfirmDialog } from '../../shared/components/ConfirmDialog'
import { Dialog } from '../../shared/components/Dialog'
import { Input } from '../../shared/components/Input'
import { DateField } from '../../shared/components/DateField'
import { FormField } from '../../shared/components/FormField'
import { FileUploadArea } from '../../shared/components/FileUploadArea'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { Skeleton } from '../../shared/components/Skeleton'
import { ApiError, saveBlob } from '../../shared/api/httpClient'
import { responsibilityTermStatusMeta, executionStatusMeta, deliveryStatusMeta } from '../../shared/status/statusMaps'
import { formatDate, formatDateTime, formatBytes } from '../../shared/utils/format'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import { listResponsibilityTerms, generateResponsibilityTerm } from '../handovers/api'
import * as api from './api'
import type { ResponsibilityTerm } from './types'

const DOWNLOAD_ERROR = 'Não foi possível descarregar o documento.'

function DialogError({ message }: { message: string | null }) {
  if (!message) return null
  return (
    <p
      className="rounded-md border border-danger-border bg-danger-bg px-3 py-2 text-[13px] leading-[19px] whitespace-pre-line text-danger-fg"
      role="alert"
    >
      {message}
    </p>
  )
}

/**
 * The handover's Responsibility Term: the current ("Atual") version with its
 * send/signature workflow, and every superseded version kept read-only for
 * download. A signed document belongs to the version it was uploaded against —
 * an older signed version never stands in for the current one.
 *
 * A carrier term can be prepared once the dispatch is registered, before
 * physical receipt (`deliveryConfirmed` false): it can be generated and
 * downloaded, but sending it and registering the signed copy only become
 * available after "Confirmar entrega" — the server enforces the same rule.
 */
export function ResponsibilityTermSection({ handoverId, deliveryConfirmed }: { handoverId: string; deliveryConfirmed: boolean }) {
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()

  const termsQuery = useQuery({
    queryKey: ['responsibility-terms', handoverId],
    queryFn: () => listResponsibilityTerms(handoverId),
  })

  const [generateConfirmOpen, setGenerateConfirmOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [downloadError, setDownloadError] = useState<string | null>(null)

  const generateMutation = useMutation({
    mutationFn: () => generateResponsibilityTerm(handoverId),
    onMutate: () => setError(null),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['responsibility-terms', handoverId] })
      // A term prepared before delivery freezes the handover's snapshot.
      void queryClient.invalidateQueries({ queryKey: ['handover', handoverId] })
      setGenerateConfirmOpen(false)
    },
    onError: (e) => setError(e instanceof ApiError ? e.message : 'Não foi possível gerar o termo.'),
  })

  const terms = termsQuery.data ?? []
  const current = terms.find((t) => t.status === 'Generated')
  const previous = terms.filter((t) => t.status !== 'Generated')
  const canGenerate = hasPermission(Permissions.ResponsibilityTerm.Generate)

  return (
    <DetailSection
      variant="ruled"
      title="Termo de Responsabilidade"
      description={
        deliveryConfirmed
          ? 'Gerado a partir dos dados da entrega. Versões anteriores permanecem sempre disponíveis.'
          : 'A composição da entrega está bloqueada. O termo já pode ser preparado.'
      }
      actions={
        canGenerate && (
          <Button
            size="sm"
            variant="secondary"
            className="max-lg:h-11"
            icon={<FileText className="size-4" aria-hidden />}
            onClick={() => setGenerateConfirmOpen(true)}
          >
            {current ? 'Regenerar termo' : 'Gerar termo'}
          </Button>
        )
      }
    >
      {termsQuery.isLoading ? (
        <Skeleton className="h-24 w-full" />
      ) : termsQuery.error ? (
        <p className="text-[13px] text-danger" role="alert">
          Não foi possível carregar os termos desta entrega.
        </p>
      ) : terms.length === 0 ? (
        <p className="text-[13px] text-fg-muted">Ainda não foi gerado nenhum termo para esta entrega.</p>
      ) : (
        <div className="flex flex-col gap-4">
          {current && <CurrentTermCard term={current} handoverId={handoverId} deliveryConfirmed={deliveryConfirmed} />}
          {previous.length > 0 && (
            <div>
              <h3 className="mb-1.5 text-[12px] leading-4 font-medium text-fg-muted">Versões anteriores</h3>
              <ul aria-label="Versões anteriores do termo" className="flex flex-col">
                {previous.map((term) => {
                  const meta = responsibilityTermStatusMeta[term.status]
                  return (
                    <li
                      key={term.id}
                      className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 border-b border-border-row py-2 last:border-b-0"
                    >
                      <span className="text-[13px] text-fg-muted tabular-nums">
                        <span className="font-mono text-[12.5px] text-fg">{term.documentNumber}</span> · versão {term.version} ·{' '}
                        {formatDate(term.generatedAtUtc)}
                      </span>
                      <span className="flex items-center gap-2">
                        <StatusBadge label={meta?.label ?? term.status} tone={meta?.tone ?? 'neutral'} icon={meta?.icon} />
                        <Button
                          size="sm"
                          variant="ghost"
                          className="max-lg:h-11"
                          aria-label={`Descarregar PDF ${term.documentNumber} versão ${term.version}`}
                          icon={<Download className="size-3.5" aria-hidden />}
                          onClick={() => {
                            setDownloadError(null)
                            api
                              .getResponsibilityTermPdf(term.id)
                              .then((blob) => saveBlob(blob, `${term.documentNumber}.pdf`))
                              .catch(() => setDownloadError(DOWNLOAD_ERROR))
                          }}
                        >
                          PDF
                        </Button>
                      </span>
                    </li>
                  )
                })}
              </ul>
              {downloadError && (
                <p className="mt-2 text-[12.5px] text-danger" role="alert">
                  {downloadError}
                </p>
              )}
            </div>
          )}
        </div>
      )}

      <ConfirmDialog
        open={generateConfirmOpen}
        onOpenChange={(open) => {
          setGenerateConfirmOpen(open)
          if (!open) setError(null)
        }}
        title={current ? 'Regenerar termo' : 'Gerar termo'}
        description={
          current
            ? 'A versão atual passa a "Substituído" mas continua disponível para consulta e descarregamento. É criada uma nova versão, com um novo processo de envio/assinatura.'
            : deliveryConfirmed
              ? 'É gerado o Termo de Responsabilidade a partir dos dados desta entrega.'
              : 'É gerado o Termo de Responsabilidade a partir dos dados desta entrega. O termo poderá ser enviado após a confirmação da entrega.'
        }
        confirmLabel={current ? 'Regenerar' : 'Gerar'}
        loading={generateMutation.isPending}
        error={error}
        onConfirm={() => generateMutation.mutate()}
      />
    </DetailSection>
  )
}

function CurrentTermCard({
  term,
  handoverId,
  deliveryConfirmed,
}: {
  term: ResponsibilityTerm
  handoverId: string
  deliveryConfirmed: boolean
}) {
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const [sendOpen, setSendOpen] = useState(false)
  const [uploadOpen, setUploadOpen] = useState(false)
  const [recipientEmail, setRecipientEmail] = useState('')
  const [signedOn, setSignedOn] = useState('')
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [sendError, setSendError] = useState<string | null>(null)
  const [uploadError, setUploadError] = useState<string | null>(null)
  const [downloadError, setDownloadError] = useState<string | null>(null)

  const deliveriesQuery = useQuery({
    queryKey: ['responsibility-term-deliveries', term.id],
    queryFn: () => api.listDeliveries(term.id),
    enabled: term.executionStatus !== 'NotSent',
  })

  const sendMutation = useMutation({
    mutationFn: () => api.sendResponsibilityTerm(term.id, recipientEmail || undefined),
    onMutate: () => setSendError(null),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['responsibility-terms'] })
      void queryClient.invalidateQueries({ queryKey: ['responsibility-term-deliveries', term.id] })
      setSendOpen(false)
      setRecipientEmail('')
    },
    onError: (e) => setSendError(e instanceof ApiError ? e.message : 'Não foi possível enviar o termo.'),
  })

  const uploadMutation = useMutation({
    mutationFn: () => {
      if (!selectedFile) throw new Error('Selecione um ficheiro PDF.')
      return api.uploadSignedDocument(term.id, selectedFile, signedOn || undefined)
    },
    onMutate: () => setUploadError(null),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['responsibility-terms'] })
      // The Handover's derived operational status (Iteration 1) depends on this signed artifact — refetch both the detail and
      // list views so "Finalizada" shows up without a separate action.
      void queryClient.invalidateQueries({ queryKey: ['handover', handoverId] })
      void queryClient.invalidateQueries({ queryKey: ['handovers'] })
      setUploadOpen(false)
      setSelectedFile(null)
      setSignedOn('')
    },
    onError: (e) => setUploadError(e instanceof ApiError ? e.message : 'Não foi possível carregar o documento assinado.'),
  })

  function download(load: () => Promise<Blob>, fileName: string) {
    setDownloadError(null)
    load()
      .then((blob) => saveBlob(blob, fileName))
      .catch(() => setDownloadError(DOWNLOAD_ERROR))
  }

  // Sending and the signed copy are post-delivery only.
  const canSend = deliveryConfirmed && hasPermission(Permissions.ResponsibilityTerm.Send)
  const canManageSignature = deliveryConfirmed && hasPermission(Permissions.ResponsibilityTerm.SignatureManage)
  const currentMeta = responsibilityTermStatusMeta[term.status]
  const executionMeta = executionStatusMeta[term.executionStatus]

  return (
    <div className="rounded-lg border border-border bg-surface">
      <div className="flex flex-wrap items-start justify-between gap-x-3 gap-y-2 px-4 pt-3.5">
        <div className="min-w-0">
          <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-[14px] leading-5 font-semibold text-fg">
            <span className="font-mono text-[13px]">{term.documentNumber}</span>
            <span className="font-normal text-fg-muted">· versão {term.version}</span>
            {currentMeta && <StatusBadge label={currentMeta.label} tone={currentMeta.tone} icon={currentMeta.icon} />}
          </p>
          <p className="mt-0.5 text-[12.5px] leading-[18px] text-fg-muted">
            Gerado por {term.generatedByDisplayNameSnapshot} em {formatDateTime(term.generatedAtUtc)} · {formatBytes(term.sizeBytes)}
          </p>
        </div>
        <StatusBadge
          label={executionMeta?.label ?? term.executionStatus}
          tone={executionMeta?.tone ?? 'neutral'}
          icon={executionMeta?.icon}
        />
      </div>

      <div className="flex flex-wrap gap-2 px-4 pt-3 pb-3.5">
        <Button
          size="sm"
          variant="secondary"
          className="max-lg:h-11"
          icon={<Download className="size-3.5" aria-hidden />}
          onClick={() => download(() => api.getResponsibilityTermPdf(term.id), `${term.documentNumber}.pdf`)}
        >
          Descarregar PDF original
        </Button>
        {canSend && (
          <Button
            size="sm"
            variant="secondary"
            className="max-lg:h-11"
            icon={<Send className="size-3.5" aria-hidden />}
            onClick={() => setSendOpen(true)}
          >
            Enviar
          </Button>
        )}
        {canManageSignature && !term.signature && (
          <Button
            size="sm"
            variant="secondary"
            className="max-lg:h-11"
            icon={<Upload className="size-3.5" aria-hidden />}
            onClick={() => setUploadOpen(true)}
          >
            Carregar documento assinado
          </Button>
        )}
      </div>

      {downloadError && (
        <p className="px-4 pb-3 text-[12.5px] text-danger" role="alert">
          {downloadError}
        </p>
      )}

      {term.signature ? (
        <div className="flex flex-col items-start gap-1 border-t border-border px-4 py-3">
          <StatusBadge label="Documento assinado registado" tone="success" />
          <p className="text-[12.5px] leading-[18px] text-fg-muted">
            {term.signature.signedOn && `Assinado em ${formatDate(term.signature.signedOn)} · `}
            Carregado por {term.signature.uploadedByDisplayNameSnapshot} em {formatDateTime(term.signature.uploadedAtUtc)}
          </p>
          <Button
            size="sm"
            variant="secondary"
            className="mt-1.5 max-lg:h-11"
            icon={<Download className="size-3.5" aria-hidden />}
            onClick={() => download(() => api.getSignedDocumentPdf(term.id), `${term.documentNumber}-assinado.pdf`)}
          >
            Descarregar documento assinado
          </Button>
        </div>
      ) : (
        <p className="border-t border-border px-4 py-3 text-[12.5px] leading-[18px] text-fg-muted">
          {deliveryConfirmed
            ? 'Documento assinado desta versão ainda não registado.'
            : 'O termo poderá ser enviado após a confirmação da entrega.'}
        </p>
      )}

      {deliveriesQuery.data && deliveriesQuery.data.length > 0 && (
        <div className="border-t border-border px-4 py-3">
          <h4 className="mb-1.5 text-[12px] leading-4 font-medium text-fg-muted">Histórico de envio</h4>
          <ul className="flex flex-col">
            {deliveriesQuery.data.map((delivery) => {
              const meta = deliveryStatusMeta[delivery.status]
              return (
                <li
                  key={delivery.id}
                  className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 border-b border-border-row py-1.5 text-[12.5px] last:border-b-0"
                >
                  <span className="min-w-0 break-words text-fg-muted">
                    <span className="tabular-nums">{formatDateTime(delivery.attemptedAtUtc)}</span> · {delivery.recipientEmailSnapshot}
                    {delivery.failureCode && ` · ${delivery.failureCode}`}
                  </span>
                  <StatusBadge label={meta?.label ?? delivery.status} tone={meta?.tone ?? 'neutral'} icon={meta?.icon} />
                </li>
              )
            })}
          </ul>
        </div>
      )}

      <Dialog
        open={sendOpen}
        onOpenChange={(open) => {
          setSendOpen(open)
          if (!open) setSendError(null)
        }}
        title="Enviar termo por e-mail"
        description="Por omissão, é usado o e-mail do consultor registado na entrega."
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setSendOpen(false)}>
              Cancelar
            </Button>
            <Button variant="primary" loading={sendMutation.isPending} onClick={() => sendMutation.mutate()}>
              Enviar
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <FormField id="recipient-email" label="E-mail do destinatário" optional helperText="Deixe em branco para usar o e-mail registado.">
            <Input id="recipient-email" type="email" value={recipientEmail} onChange={(e) => setRecipientEmail(e.target.value)} />
          </FormField>
          <DialogError message={sendError} />
        </div>
      </Dialog>

      <Dialog
        open={uploadOpen}
        onOpenChange={(open) => {
          setUploadOpen(open)
          if (!open) setUploadError(null)
        }}
        title="Carregar documento assinado"
        description={`Documento assinado da versão ${term.version} (${term.documentNumber}).`}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setUploadOpen(false)}>
              Cancelar
            </Button>
            <Button
              variant="primary"
              loading={uploadMutation.isPending}
              disabled={!selectedFile}
              onClick={() => uploadMutation.mutate()}
            >
              Carregar
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <FormField id="signed-on" label="Data de assinatura" optional helperText="Conforme indicado no documento assinado.">
            <DateField id="signed-on" value={signedOn} onChange={(e) => setSignedOn(e.target.value)} />
          </FormField>
          <FormField id="signed-file" label="Documento PDF assinado" required>
            {selectedFile ? (
              <p className="truncate rounded-md border border-border bg-surface-sunken px-3 py-2 text-[13px] text-fg">
                {selectedFile.name}
              </p>
            ) : (
              <FileUploadArea accept="application/pdf" hint="PDF" onFileSelected={setSelectedFile} />
            )}
          </FormField>
          <DialogError message={uploadError} />
        </div>
      </Dialog>
    </div>
  )
}
