import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { Drawer } from '../../shared/components/Drawer'
import { KeyValueList } from '../../shared/components/KeyValueList'
import { formatDateTime } from '../../shared/utils/format'
import type { AuditLog } from './types'
import { auditActionLabel, auditActionLabels, auditEntityHref, auditEntityTypeLabel } from './auditPresentation'

interface AuditDetailDrawerProps {
  entry: AuditLog | null
  onClose: () => void
}

function prettyJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2)
  } catch {
    return raw
  }
}

function JsonBlock({ raw }: { raw: string }) {
  return (
    <pre className="max-h-64 overflow-auto rounded-md bg-surface-sunken p-3 font-mono text-[12px] leading-relaxed text-fg">
      {prettyJson(raw)}
    </pre>
  )
}

function Section({ id, title, children }: { id: string; title: string; children: ReactNode }) {
  return (
    <section aria-labelledby={id} className="flex flex-col gap-3 border-b border-border py-4 first:pt-0 last:border-b-0">
      <h3 id={id} className="text-[14px] leading-5 font-semibold text-fg">
        {title}
      </h3>
      {children}
    </section>
  )
}

const mono = (value: string) => <span className="font-mono text-[12px] break-all">{value}</span>

export function AuditDetailDrawer({ entry, onClose }: AuditDetailDrawerProps) {
  const entityHref = entry ? auditEntityHref(entry.entityType, entry.entityId, entry.action) : undefined

  return (
    <Drawer open={Boolean(entry)} onOpenChange={(open) => !open && onClose()} title="Detalhe do registo de auditoria">
      {entry && (
        <div className="flex flex-col">
          <Section id="audit-summary" title="Resumo">
            <KeyValueList
              columns={1}
              items={[
                { label: 'Ação', value: <span className="font-medium">{auditActionLabel(entry.action)}</span> },
                // The stored code, unchanged; shown as context when it has a label of its own.
                ...(auditActionLabels[entry.action] ? [{ label: 'Código técnico', value: mono(entry.action) }] : []),
                { label: 'Autor', value: entry.actorDisplayNameSnapshot ?? 'Sistema' },
                { label: 'Data/hora', value: formatDateTime(entry.occurredAtUtc) },
              ]}
            />
          </Section>

          <Section id="audit-entity" title="Registo afetado">
            <KeyValueList
              columns={1}
              items={[
                { label: 'Entidade', value: `${auditEntityTypeLabel(entry.entityType)} (${entry.entityType})` },
                {
                  label: 'Identificador',
                  value: entry.entityId
                    ? entityHref
                      ? (
                          <Link
                            to={entityHref}
                            className="underline decoration-border-strong underline-offset-2 hover:decoration-fg"
                          >
                            {mono(entry.entityId)}
                          </Link>
                        )
                      : mono(entry.entityId)
                    : '—',
                },
              ]}
            />
          </Section>

          <Section id="audit-origin" title="Origem do pedido">
            <KeyValueList
              columns={1}
              items={[
                { label: 'Endereço IP', value: entry.ipAddress ? mono(entry.ipAddress) : '—' },
                { label: 'ID de correlação', value: entry.correlationId ? mono(entry.correlationId) : '—' },
              ]}
            />
          </Section>

          {entry.oldValuesJson && (
            <Section id="audit-old" title="Valores anteriores">
              <JsonBlock raw={entry.oldValuesJson} />
            </Section>
          )}

          {entry.newValuesJson && (
            <Section id="audit-new" title="Valores novos">
              <JsonBlock raw={entry.newValuesJson} />
            </Section>
          )}

          {entry.metadataJson && (
            <Section id="audit-metadata" title="Metadados">
              <JsonBlock raw={entry.metadataJson} />
            </Section>
          )}
        </div>
      )}
    </Drawer>
  )
}
