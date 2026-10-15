import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Search } from 'lucide-react'
import { Dialog } from '../../shared/components/Dialog'
import { Input } from '../../shared/components/Input'
import { Button } from '../../shared/components/Button'
import { ApiError } from '../../shared/api/httpClient'
import { listEquipment } from '../equipment/api'
import * as api from './api'

interface AddItemDialogProps {
  handoverId: string
  open: boolean
  onOpenChange: (open: boolean) => void
}

/**
 * Lists Available equipment only. Adding an item reserves nothing: the
 * equipment stays Available while the handover is a Draft, so it may sit in
 * more than one Draft — only the first confirmed delivery assigns it, and the
 * backend rejects any later one.
 */
export function AddItemDialog({ handoverId, open, onOpenChange }: AddItemDialogProps) {
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [addingId, setAddingId] = useState<string | null>(null)

  const query = useQuery({
    queryKey: ['equipment', 'available-picker', search],
    queryFn: () => listEquipment({ status: 'Available', search: search || undefined, page: 1, pageSize: 20 }),
    enabled: open,
  })

  const mutation = useMutation({
    mutationFn: (equipmentId: string) => {
      setAddingId(equipmentId)
      return api.addHandoverItem(handoverId, equipmentId)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['handover', handoverId] })
      void queryClient.invalidateQueries({ queryKey: ['equipment', 'available-picker'] })
    },
    onSettled: () => setAddingId(null),
  })

  const addError = mutation.error
    ? mutation.error instanceof ApiError && mutation.error.status < 500
      ? mutation.error.message
      : 'Não foi possível adicionar o equipamento.'
    : null

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) mutation.reset()
        onOpenChange(next)
      }}
      title="Adicionar equipamento"
      description="Apenas equipamento disponível. Fica atribuído ao consultor só quando a entrega for confirmada."
      size="md"
    >
      <div className="flex flex-col gap-3">
        <div className="relative">
          <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-muted" aria-hidden />
          <Input
            aria-label="Pesquisar equipamento disponível"
            placeholder="Pesquisar por identificador, fabricante ou modelo…"
            className="pl-8"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            autoFocus
          />
        </div>
        {addError && (
          <p
            className="rounded-md border border-danger-border bg-danger-bg px-3 py-2 text-[13px] leading-[19px] whitespace-pre-line text-danger-fg"
            role="alert"
          >
            {addError}
          </p>
        )}
        <div className="max-h-80 overflow-y-auto rounded-md border border-border">
          {query.isLoading ? (
            <p className="px-3 py-3 text-[13px] text-fg-muted">A carregar…</p>
          ) : query.error ? (
            <p className="px-3 py-3 text-[13px] text-danger" role="alert">
              Não foi possível carregar o equipamento disponível.
            </p>
          ) : (query.data?.items.length ?? 0) === 0 ? (
            <p className="px-3 py-3 text-[13px] text-fg-muted">Nenhum equipamento disponível encontrado.</p>
          ) : (
            <ul className="divide-y divide-border-row">
              {query.data?.items.map((equipment) => (
                <li key={equipment.id} className="flex items-center justify-between gap-3 px-3 py-2">
                  <div className="min-w-0">
                    <p className="text-[13px] leading-5 text-fg">
                      <span className="mr-2 font-mono text-[12px] font-medium">{equipment.assetTag}</span>
                      <span className="text-fg-muted">{equipment.equipmentTypeName}</span>
                    </p>
                    <p className="truncate text-[12px] text-fg-muted">
                      {[equipment.manufacturer, equipment.model].filter(Boolean).join(' ') || 'Sem fabricante/modelo'}
                    </p>
                  </div>
                  <Button
                    size="sm"
                    variant="secondary"
                    className="max-lg:h-11"
                    aria-label={`Adicionar ${equipment.assetTag}`}
                    loading={addingId === equipment.id}
                    onClick={() => mutation.mutate(equipment.id)}
                  >
                    Adicionar
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </Dialog>
  )
}
