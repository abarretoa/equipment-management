import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertCircle } from 'lucide-react'
import { Dialog } from '../../shared/components/Dialog'
import { Button } from '../../shared/components/Button'
import { FormField } from '../../shared/components/FormField'
import { DateField } from '../../shared/components/DateField'
import { Textarea } from '../../shared/components/Textarea'
import { Checkbox } from '../../shared/components/Checkbox'
import { Combobox, type ComboboxOption } from '../../shared/components/Combobox'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { Skeleton } from '../../shared/components/Skeleton'
import { ApiError } from '../../shared/api/httpClient'
import { equipmentStatusMeta } from '../../shared/status/statusMaps'
import { listConsultants } from '../consultants/api'
import { listAssignments } from '../assignments/api'
import type { EquipmentAssignment } from '../assignments/types'
import * as api from './api'

interface NewReturnDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
}

/** Mounted fresh each time it opens, so every opening starts from an empty form. */
export function NewReturnDialog({ open, onOpenChange }: NewReturnDialogProps) {
  return open ? <NewReturnDialogContent onOpenChange={onOpenChange} /> : null
}

/** Why an item in the consultant's custody cannot be selected, or null when it can. The server enforces the same rule. */
function unavailableReason(item: EquipmentAssignment): string | null {
  if (item.equipmentStatus === 'Assigned') return null
  if (item.equipmentStatus === 'ReturnPending') return 'Devolução já iniciada'
  return equipmentStatusMeta[item.equipmentStatus]?.label ?? 'Indisponível'
}

/**
 * Starts a manual return for one or more items a consultant currently holds.
 * Who can be chosen is decided by custody alone (an active assignment), never
 * by the allocation, and nothing here ends or changes an allocation.
 */
function NewReturnDialogContent({ onOpenChange }: Pick<NewReturnDialogProps, 'onOpenChange'>) {
  const queryClient = useQueryClient()
  const [consultantSearch, setConsultantSearch] = useState('')
  const [consultantId, setConsultantId] = useState('')
  const [selected, setSelected] = useState<string[]>([])
  const [expectedReturnDate, setExpectedReturnDate] = useState('')
  const [reason, setReason] = useState('')

  const consultantsQuery = useQuery({
    queryKey: ['consultants', 'return-picker', consultantSearch],
    queryFn: () => listConsultants({ hasActiveCustody: true, search: consultantSearch || undefined, page: 1, pageSize: 20 }),
  })

  const assignmentsQuery = useQuery({
    queryKey: ['assignments', 'active', consultantId],
    queryFn: () => listAssignments({ consultantId, status: 'Active', page: 1, pageSize: 100 }),
    enabled: Boolean(consultantId),
  })

  const options: ComboboxOption[] = (consultantsQuery.data?.items ?? []).map((consultant) => ({
    value: consultant.id,
    label: consultant.fullName,
    secondary: consultant.currentRoleOrFunction ?? undefined,
  }))

  const items = assignmentsQuery.data?.items ?? []

  const mutation = useMutation({
    mutationFn: () =>
      api.startReturnGroup({
        consultantId,
        equipmentAssignmentIds: selected,
        expectedReturnDate: expectedReturnDate || undefined,
        reason: reason.trim() || undefined,
      }),
    onSuccess: () => {
      // Starting a return changes equipment status and creates returns; custody and allocation are untouched.
      for (const queryKey of [['returns'], ['equipment'], ['consultants'], ['assignments'], ['dashboard']]) {
        void queryClient.invalidateQueries({ queryKey })
      }
      onOpenChange(false)
    },
    onError: () => {
      // The picker may be stale (e.g. a return was just started elsewhere): show the current state next to the error.
      void queryClient.invalidateQueries({ queryKey: ['assignments', 'active', consultantId] })
    },
  })

  const serverError = mutation.error
    ? mutation.error instanceof ApiError && mutation.error.status < 500
      ? mutation.error.message
      : 'Não foi possível iniciar a devolução.'
    : null

  function chooseConsultant(id: string) {
    setConsultantId(id)
    setSelected([])
    mutation.reset()
  }

  function toggle(assignmentId: string, checked: boolean) {
    setSelected((current) => (checked ? [...current, assignmentId] : current.filter((id) => id !== assignmentId)))
  }

  return (
    <Dialog
      open
      onOpenChange={onOpenChange}
      title="Nova devolução"
      description="Inicia a devolução de equipamento que um consultor tem atualmente consigo. A alocação do consultor não é alterada."
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={mutation.isPending}>
            Cancelar
          </Button>
          <Button
            variant="primary"
            loading={mutation.isPending}
            disabled={selected.length === 0}
            onClick={() => {
              if (!mutation.isPending) mutation.mutate()
            }}
          >
            Iniciar devolução
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <FormField
          id="return-consultant"
          label="Consultor"
          required
          helperText="São apresentados os consultores com equipamento atualmente atribuído."
        >
          <Combobox
            id="return-consultant"
            value={consultantId || null}
            onChange={chooseConsultant}
            options={options}
            search={consultantSearch}
            onSearchChange={setConsultantSearch}
            placeholder="Pesquisar consultor…"
            searchPlaceholder="Pesquisar por nome…"
            isLoading={consultantsQuery.isLoading}
            emptyLabel="Nenhum consultor com equipamento atribuído encontrado."
          />
        </FormField>

        {consultantId && (
          <div className="flex flex-col gap-1.5" role="group" aria-labelledby="return-items-label">
            <p id="return-items-label" className="text-[13px] font-medium text-fg">
              Equipamentos atualmente atribuídos
              <span className="text-danger" aria-hidden>
                {' '}
                *
              </span>
            </p>
            {assignmentsQuery.isLoading ? (
              <Skeleton className="h-24 w-full" />
            ) : assignmentsQuery.error ? (
              <p className="text-[13px] text-danger" role="alert">
                Não foi possível carregar os equipamentos deste consultor.
              </p>
            ) : items.length === 0 ? (
              <p className="text-[13px] text-fg-muted">Este consultor não tem equipamento atribuído atualmente.</p>
            ) : (
              <ul className="divide-y divide-border rounded-md border border-border">
                {items.map((item) => {
                  const unavailable = unavailableReason(item)
                  const descriptor = [item.manufacturer, item.model].filter(Boolean).join(' ') || item.equipmentTypeName
                  return (
                    <li key={item.id} className="flex items-center justify-between gap-3 px-3 py-2">
                      <Checkbox
                        id={`return-item-${item.id}`}
                        label={`${item.assetTag} — ${descriptor}`}
                        checked={selected.includes(item.id)}
                        disabled={unavailable !== null}
                        onChange={(e) => toggle(item.id, e.target.checked)}
                        className={unavailable ? 'cursor-not-allowed text-fg-subtle' : undefined}
                      />
                      {unavailable && <StatusBadge label={unavailable} tone="warning" />}
                    </li>
                  )
                })}
              </ul>
            )}
          </div>
        )}

        <FormField id="new-return-expected-date" label="Data prevista de devolução" optional>
          <DateField id="new-return-expected-date" value={expectedReturnDate} onChange={(e) => setExpectedReturnDate(e.target.value)} />
        </FormField>
        <FormField id="new-return-reason" label="Motivo" optional>
          <Textarea id="new-return-reason" value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} />
        </FormField>

        {serverError && (
          <div
            className="flex items-start gap-2 rounded-md border border-danger-border bg-danger-bg px-3 py-2 text-[13px] text-danger"
            role="alert"
          >
            <AlertCircle className="mt-0.5 size-4 shrink-0" />
            <p className="whitespace-pre-line">{serverError}</p>
          </div>
        )}
      </div>
    </Dialog>
  )
}
