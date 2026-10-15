import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { useMutation, useQuery } from '@tanstack/react-query'
import { UserX } from 'lucide-react'
import { PageHeader } from '../../shared/components/PageHeader'
import { ContentWidth } from '../../shared/components/ContentWidth'
import { FormField } from '../../shared/components/FormField'
import { Combobox, type ComboboxOption } from '../../shared/components/Combobox'
import { Textarea } from '../../shared/components/Textarea'
import { Button } from '../../shared/components/Button'
import { EmptyState } from '../../shared/components/EmptyState'
import { ApiError } from '../../shared/api/httpClient'
import { workforceTypeLabels } from '../../shared/status/statusMaps'
import { listConsultants } from '../consultants/api'
import { DeliveryMethodPicker } from './DeliveryMethodPicker'
import type { HandoverDeliveryMethod } from './types'
import * as api from './api'

/**
 * Compact task width (Phase 9 refinement) — a focused single-decision form,
 * not a full entity page. It only creates the Draft: equipment is added on the
 * handover itself, and custody starts only when that delivery is confirmed.
 * There is deliberately no photo upload here — current condition photos belong
 * to the equipment, not to the delivery.
 */
export function HandoverCreatePage() {
  const navigate = useNavigate()
  const [consultantSearch, setConsultantSearch] = useState('')
  const [consultantId, setConsultantId] = useState('')
  const [deliveryMethod, setDeliveryMethod] = useState<HandoverDeliveryMethod | null>(null)
  const [notes, setNotes] = useState('')
  const [formError, setFormError] = useState<string | null>(null)

  // Only Consultants eligible for a new delivery (an Active allocation,
  // Iteration 3). The backend re-checks on submit regardless.
  const consultantsQuery = useQuery({
    queryKey: ['consultants', 'handover-picker', consultantSearch],
    queryFn: () =>
      listConsultants({ isActive: true, hasActiveAllocation: true, search: consultantSearch || undefined, page: 1, pageSize: 20 }),
  })

  // De-duplicated by id purely defensively: the one-Active-allocation
  // invariant means the list endpoint returns each Consultant once.
  const consultants = [...new Map((consultantsQuery.data?.items ?? []).map((c) => [c.id, c])).values()]
  const options: ComboboxOption[] = consultants.map((consultant) => {
    const regime = consultant.currentWorkforceType ? workforceTypeLabels[consultant.currentWorkforceType] : null
    const secondaryParts = [regime, consultant.currentCompanyName ?? undefined, consultant.currentRoleOrFunction ?? undefined].filter(Boolean)
    return {
      value: consultant.id,
      label: consultant.fullName,
      secondary: secondaryParts.length > 0 ? secondaryParts.join(' · ') : undefined,
    }
  })

  const noEligibleConsultants = !consultantsQuery.isLoading && !consultantsQuery.error && !consultantSearch && options.length === 0

  const mutation = useMutation({
    mutationFn: () => api.createHandover(consultantId, deliveryMethod!, notes || undefined),
    onSuccess: (handover) => navigate(`/entregas/${handover.id}`),
    onError: (error) =>
      setFormError(error instanceof ApiError && error.status < 500 ? error.message : 'Não foi possível criar a entrega.'),
  })

  return (
    <div className="flex flex-col">
      <PageHeader
        title="Nova entrega"
        description="Crie uma entrega em rascunho e adicione os equipamentos de seguida."
        breadcrumbs={[{ label: 'Entregas', to: '/entregas' }, { label: 'Nova entrega' }]}
      />
      <ContentWidth variant="compact" className="px-4 py-6 sm:px-6">
        {noEligibleConsultants ? (
          <EmptyState
            icon={<UserX className="size-5" aria-hidden />}
            title="Não existem consultores com uma alocação válida para receber equipamento."
            description="Registe uma alocação no consultor antes de criar uma entrega."
            action={
              <Link to="/consultores" className="text-[13px] font-medium text-fg underline underline-offset-2">
                Ir para Consultores
              </Link>
            }
          />
        ) : (
          <form
            className="flex flex-col"
            onSubmit={(e) => {
              e.preventDefault()
              setFormError(null)
              mutation.mutate()
            }}
          >
            <section aria-labelledby="handover-form-recipient" className="flex flex-col gap-4 border-b border-border pb-6">
              <h2 id="handover-form-recipient" className="text-[14px] leading-5 font-semibold text-fg">
                Consultor e método de entrega
              </h2>
              <FormField
                id="consultant"
                label="Consultor"
                required
                helperText="Só são apresentados consultores com uma alocação válida."
              >
                <Combobox
                  id="consultant"
                  value={consultantId || null}
                  onChange={setConsultantId}
                  options={options}
                  search={consultantSearch}
                  onSearchChange={setConsultantSearch}
                  placeholder="Pesquisar consultor…"
                  searchPlaceholder="Pesquisar por nome…"
                  isLoading={consultantsQuery.isLoading}
                  emptyLabel="Nenhum consultor com alocação válida encontrado."
                />
              </FormField>

              <FormField id="delivery-method" label="Método de entrega" required>
                <DeliveryMethodPicker id="delivery-method" value={deliveryMethod} onChange={setDeliveryMethod} arrowKeySelection />
              </FormField>
            </section>

            <section aria-labelledby="handover-form-notes" className="flex flex-col gap-4 pt-6">
              <h2 id="handover-form-notes" className="text-[14px] leading-5 font-semibold text-fg">
                Observações
              </h2>
              <FormField id="notes" label="Notas" optional>
                <Textarea id="notes" value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="Observações internas sobre esta entrega…" />
              </FormField>
            </section>

            {formError && (
              <p
                className="mt-5 rounded-md border border-danger-border bg-danger-bg px-3 py-2.5 text-[13px] leading-[19px] whitespace-pre-line text-danger-fg"
                role="alert"
              >
                {formError}
              </p>
            )}

            <div className="mt-6 flex flex-col-reverse gap-2 border-t border-border pt-4 sm:flex-row sm:justify-end">
              <Button type="button" variant="secondary" className="max-sm:h-11" onClick={() => void navigate('/entregas')}>
                Cancelar
              </Button>
              <Button
                type="submit"
                variant="primary"
                className="max-sm:h-11"
                loading={mutation.isPending}
                disabled={!consultantId || !deliveryMethod}
              >
                Criar entrega
              </Button>
            </div>
          </form>
        )}
      </ContentWidth>
    </div>
  )
}
