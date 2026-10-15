import { useState } from 'react'
import { Check, Copy } from 'lucide-react'
import { Dialog } from '../../shared/components/Dialog'
import { Button } from '../../shared/components/Button'

interface TemporaryPasswordDialogProps {
  open: boolean
  onClose: () => void
  temporaryPassword: string
}

/**
 * Shown exactly once, right after `POST /api/users` or `.../reset-password` —
 * the API never returns a temporary password again after this response.
 */
export function TemporaryPasswordDialog({ open, onClose, temporaryPassword }: TemporaryPasswordDialogProps) {
  const [copied, setCopied] = useState(false)

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(temporaryPassword)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard permission denied — the password is still visible to select/copy manually.
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => !next && onClose()}
      title="Palavra-passe temporária gerada"
      size="sm"
      footer={
        <Button variant="primary" onClick={onClose}>
          Concluído
        </Button>
      }
    >
      <div className="flex flex-col gap-3">
        <p className="text-sm text-fg-muted">
          Transmita esta palavra-passe ao utilizador por um canal seguro fora da aplicação. Não voltará a
          ser apresentada.
        </p>
        <div className="flex items-center justify-between gap-2 rounded-md border border-border bg-surface-sunken px-3 py-2">
          <code className="text-sm font-medium text-fg select-all">{temporaryPassword}</code>
          <Button variant="ghost" size="sm" icon={copied ? <Check className="size-3.5" /> : <Copy className="size-3.5" />} onClick={() => void handleCopy()}>
            {copied ? 'Copiado' : 'Copiar'}
          </Button>
        </div>
        <p className="text-[12.5px] text-fg-subtle">
          O utilizador terá de a alterar no primeiro início de sessão.
        </p>
      </div>
    </Dialog>
  )
}
