import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { MoreHorizontal, Plus, UserCog } from 'lucide-react'
import { PageHeader } from '../../shared/components/PageHeader'
import { FilterBar } from '../../shared/components/FilterBar'
import { Select } from '../../shared/components/Select'
import { DataTable, type DataTableColumn } from '../../shared/components/DataTable'
import { StatusBadge } from '../../shared/components/StatusBadge'
import { Tag } from '../../shared/components/Tag'
import { Button } from '../../shared/components/Button'
import { DropdownMenu } from '../../shared/components/DropdownMenu'
import { ConfirmDialog } from '../../shared/components/ConfirmDialog'
import { IconButton } from '../../shared/components/IconButton'
import { AdminMobileRow } from '../../shared/components/AdminMobileRow'
import { ApiError } from '../../shared/api/httpClient'
import { activeStatusMeta, roleLabels } from '../../shared/status/statusMaps'
import { useAuth } from '../auth/AuthContext'
import { Permissions } from '../../shared/permissions'
import * as api from './api'
import type { UserRole, UserSummary } from './types'
import { CreateUserDialog } from './CreateUserDialog'
import { EditUserDialog } from './EditUserDialog'
import { ChangeRoleDialog } from './ChangeRoleDialog'
import { ResetPasswordConfirmDialog } from './ResetPasswordConfirmDialog'
import { TemporaryPasswordDialog } from './TemporaryPasswordDialog'

const PAGE_SIZE = 20

const fullName = (user: UserSummary) => `${user.firstName} ${user.lastName}`

/**
 * Application accounts (login identities) — not Consultants. Nothing here
 * reads or links consultant, allocation or custody data.
 */
export function UsersPage() {
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [role, setRole] = useState<string>('')
  const [isActive, setIsActive] = useState('')
  const [page, setPage] = useState(1)

  const [createOpen, setCreateOpen] = useState(false)
  const [editingUser, setEditingUser] = useState<UserSummary | null>(null)
  const [roleTarget, setRoleTarget] = useState<UserSummary | null>(null)
  const [resetTarget, setResetTarget] = useState<UserSummary | null>(null)
  const [archiveTarget, setArchiveTarget] = useState<UserSummary | null>(null)
  const [temporaryPassword, setTemporaryPassword] = useState<string | null>(null)

  const query = useQuery({
    queryKey: ['users', { search, role, isActive, page }],
    queryFn: () =>
      api.listUsers({
        search: search || undefined,
        role: (role || undefined) as UserRole | undefined,
        isActive: isActive ? isActive === 'true' : undefined,
        page,
        pageSize: PAGE_SIZE,
      }),
  })

  const archiveMutation = useMutation({
    mutationFn: (user: UserSummary) => api.setUserActive(user.id, !user.isActive, user.concurrencyStamp),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['users'] })
      setArchiveTarget(null)
    },
  })

  const canManage = hasPermission(Permissions.User.Update)
  const canCreate = hasPermission(Permissions.User.Create)
  const canArchive = hasPermission(Permissions.User.Archive)
  const canAssignRole = hasPermission(Permissions.User.AssignRole)
  const hasActiveFilters = Boolean(search || role || isActive)

  function clearFilters() {
    setSearch('')
    setRole('')
    setIsActive('')
    setPage(1)
  }

  /** Only the actions this account may perform are offered; with none, there is no menu. */
  function rowMenu(row: UserSummary, size: 'sm' | 'touch') {
    const items = [
      ...(canManage ? [{ label: 'Editar', onSelect: () => setEditingUser(row) }] : []),
      ...(canAssignRole ? [{ label: 'Alterar função', onSelect: () => setRoleTarget(row) }] : []),
      ...(canManage ? [{ label: 'Repor palavra-passe', onSelect: () => setResetTarget(row) }] : []),
      ...(canArchive
        ? [{ label: row.isActive ? 'Desativar' : 'Ativar', destructive: row.isActive, onSelect: () => setArchiveTarget(row) }]
        : []),
    ]
    if (items.length === 0) return null
    return (
      <DropdownMenu
        trigger={<IconButton icon={<MoreHorizontal className="size-4" aria-hidden />} label={`Mais ações: ${fullName(row)}`} size={size} />}
        items={items}
      />
    )
  }

  function statusCell(row: UserSummary) {
    const meta = activeStatusMeta(row.isActive)
    return (
      <span className="flex flex-wrap items-center gap-1.5">
        <StatusBadge label={meta.label} tone={meta.tone} />
        {row.mustChangePassword && <Tag label="Aguarda 1ª alteração" />}
      </span>
    )
  }

  const columns: DataTableColumn<UserSummary>[] = [
    {
      key: 'name',
      header: 'Nome',
      render: (row) => (
        <span className="flex flex-col">
          <span className="font-medium">{fullName(row)}</span>
          <span className="text-[12px] text-fg-muted">{row.email}</span>
        </span>
      ),
    },
    { key: 'role', header: 'Função', width: '144px', render: (row) => roleLabels[row.role] ?? row.role },
    { key: 'status', header: 'Estado', width: '232px', render: statusCell },
    { key: 'actions', header: '', width: '48px', align: 'right', render: (row) => rowMenu(row, 'sm') },
  ]

  return (
    <div className="flex flex-col">
      <PageHeader
        title="Utilizadores"
        description="Contas internas com acesso à aplicação (Administração e RH). Não são consultores."
        actions={
          canCreate && (
            <Button
              variant="primary"
              size="sm"
              className="max-lg:h-11 max-lg:px-4"
              icon={<Plus className="size-4" aria-hidden />}
              onClick={() => setCreateOpen(true)}
            >
              Novo utilizador
            </Button>
          )
        }
      />
      <div className="flex flex-col pb-6 sm:px-3">
        <FilterBar
          search={search}
          onSearchChange={(v) => { setSearch(v); setPage(1) }}
          searchPlaceholder="Pesquisar por nome ou e-mail…"
          hasActiveFilters={hasActiveFilters}
          onClear={clearFilters}
          summary={
            query.data && (
              <span>
                <b className="font-semibold text-fg tabular-nums">{query.data.totalCount}</b>{' '}
                {query.data.totalCount === 1 ? 'utilizador' : 'utilizadores'}
              </span>
            )
          }
        >
          <Select aria-label="Filtrar por função" className="w-full sm:w-40" value={role} onChange={(e) => { setRole(e.target.value); setPage(1) }}>
            <option value="">Todas as funções</option>
            <option value="Administration">{roleLabels.Administration}</option>
            <option value="HR">{roleLabels.HR}</option>
          </Select>
          <Select aria-label="Filtrar por estado" className="w-full sm:w-40" value={isActive} onChange={(e) => { setIsActive(e.target.value); setPage(1) }}>
            <option value="">Todos os estados</option>
            <option value="true">Ativos</option>
            <option value="false">Inativos</option>
          </Select>
        </FilterBar>
        <DataTable
          columns={columns}
          rows={query.data?.items ?? []}
          rowKey={(row) => row.id}
          isLoading={query.isLoading}
          error={query.error}
          onRetry={() => void query.refetch()}
          renderMobileRow={(row) => (
            <AdminMobileRow
              name={fullName(row)}
              status={statusCell(row)}
              meta={`${roleLabels[row.role] ?? row.role} · ${row.email}`}
              menu={rowMenu(row, 'touch')}
            />
          )}
          emptyIcon={<UserCog aria-hidden />}
          emptyTitle="Nenhum utilizador encontrado"
          emptyDescription={hasActiveFilters ? 'Nenhum utilizador corresponde aos filtros aplicados. Ajuste ou limpe os filtros.' : undefined}
          emptyAction={
            hasActiveFilters ? (
              <Button variant="secondary" size="sm" onClick={clearFilters}>
                Limpar filtros
              </Button>
            ) : undefined
          }
          pagination={query.data ? { page, pageSize: PAGE_SIZE, totalCount: query.data.totalCount, onPageChange: setPage } : undefined}
        />
      </div>

      <CreateUserDialog open={createOpen} onOpenChange={setCreateOpen} onCreated={setTemporaryPassword} />
      <EditUserDialog open={Boolean(editingUser)} onOpenChange={(open) => !open && setEditingUser(null)} user={editingUser} />
      <ChangeRoleDialog
        key={roleTarget?.id ?? 'none'}
        open={Boolean(roleTarget)}
        onOpenChange={(open) => !open && setRoleTarget(null)}
        user={roleTarget}
      />
      <ResetPasswordConfirmDialog
        open={Boolean(resetTarget)}
        onOpenChange={(open) => !open && setResetTarget(null)}
        user={resetTarget}
        onReset={setTemporaryPassword}
      />
      {temporaryPassword && (
        <TemporaryPasswordDialog open temporaryPassword={temporaryPassword} onClose={() => setTemporaryPassword(null)} />
      )}

      <ConfirmDialog
        open={Boolean(archiveTarget)}
        onOpenChange={(open) => {
          if (!open) {
            setArchiveTarget(null)
            archiveMutation.reset()
          }
        }}
        title={archiveTarget?.isActive ? 'Desativar utilizador' : 'Ativar utilizador'}
        description={
          archiveTarget?.isActive
            ? `${archiveTarget?.firstName} ${archiveTarget?.lastName} perde acesso imediato à aplicação.`
            : `${archiveTarget?.firstName} ${archiveTarget?.lastName} volta a ter acesso à aplicação.`
        }
        confirmLabel={archiveTarget?.isActive ? 'Desativar' : 'Ativar'}
        destructive={archiveTarget?.isActive}
        loading={archiveMutation.isPending}
        error={
          archiveMutation.error
            ? archiveMutation.error instanceof ApiError && archiveMutation.error.status < 500
              ? archiveMutation.error.message
              : 'Não foi possível alterar o estado do utilizador.'
            : null
        }
        onConfirm={() => archiveTarget && archiveMutation.mutate(archiveTarget)}
      />
    </div>
  )
}
