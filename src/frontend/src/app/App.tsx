import { lazy, Suspense } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import { QueryClientProvider } from '@tanstack/react-query'
import { Loader2 } from 'lucide-react'
import { queryClient } from './queryClient'
import { AuthProvider, useAuth } from '../features/auth/AuthContext'
import { AuthGate, RequireAuth, RequirePermission } from '../features/auth/RouteGuards'
import { LoginPage } from '../features/auth/LoginPage'
import { ChangePasswordPage } from '../features/auth/ChangePasswordPage'
import { AppLayout } from './AppLayout'
import { NotFoundPage } from './NotFoundPage'
import { DashboardPage } from '../features/dashboard/DashboardPage'
import { CompaniesPage } from '../features/companies/CompaniesPage'
import { EquipmentTypesPage } from '../features/equipment-types/EquipmentTypesPage'
import { EquipmentListPage } from '../features/equipment/EquipmentListPage'
import { EquipmentCreatePage, EquipmentEditPage } from '../features/equipment/EquipmentForm'
import { EquipmentDetailPage } from '../features/equipment/EquipmentDetailPage'
import { ConsultantsListPage } from '../features/consultants/ConsultantsListPage'
import { ConsultantCreatePage, ConsultantEditPage } from '../features/consultants/ConsultantForm'
import { ConsultantDetailPage } from '../features/consultants/ConsultantDetailPage'
import { ReturnsListPage } from '../features/returns/ReturnsListPage'
import { MaintenancePage } from '../features/maintenance/MaintenancePage'
import { HandoversListPage } from '../features/handovers/HandoversListPage'
import { HandoverCreatePage } from '../features/handovers/HandoverCreatePage'
import { TooltipProvider } from '../shared/components/Tooltip'
import { CurrentInventoryImportPermissions, Permissions } from '../shared/permissions'

// Code-split (Phase 10): less-frequently-visited or Administration-only
// screens don't need to be in the initial bundle everyone downloads on login.
// List/dashboard/form pages stay eager — they're the common path and are
// already small.
const UsersPage = lazy(() => import('../features/users/UsersPage').then((m) => ({ default: m.UsersPage })))
const AuditPage = lazy(() => import('../features/audit/AuditPage').then((m) => ({ default: m.AuditPage })))
const SolicitacoesAbatePage = lazy(() =>
  import('../features/equipment-retirement-requests/SolicitacoesAbatePage').then((m) => ({ default: m.SolicitacoesAbatePage })),
)
const ConsultantImportExportPage = lazy(() =>
  import('../features/imports/ConsultantImportExportPage').then((m) => ({ default: m.ConsultantImportExportPage })),
)
const EquipmentImportExportPage = lazy(() =>
  import('../features/imports/EquipmentImportExportPage').then((m) => ({ default: m.EquipmentImportExportPage })),
)
const CurrentInventoryImportPage = lazy(() =>
  import('../features/imports/CurrentInventoryImportPage').then((m) => ({ default: m.CurrentInventoryImportPage })),
)
const HandoverDetailPage = lazy(() =>
  import('../features/handovers/HandoverDetailPage').then((m) => ({ default: m.HandoverDetailPage })),
)
const ReturnGroupDetailPage = lazy(() =>
  import('../features/returns/ReturnGroupDetailPage').then((m) => ({ default: m.ReturnGroupDetailPage })),
)
const ProfilePage = lazy(() => import('../features/profile/ProfilePage').then((m) => ({ default: m.ProfilePage })))
const ReturnDetailPage = lazy(() =>
  import('../features/returns/ReturnDetailPage').then((m) => ({ default: m.ReturnDetailPage })),
)

function RouteLoadingFallback() {
  return (
    <div className="flex h-64 items-center justify-center">
      <Loader2 className="size-6 animate-spin text-fg-subtle" aria-hidden />
    </div>
  )
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <TooltipProvider>
          <BrowserRouter>
            <AuthGate>
              <Suspense fallback={<RouteLoadingFallback />}>
                <AppRoutes />
              </Suspense>
            </AuthGate>
          </BrowserRouter>
        </TooltipProvider>
      </AuthProvider>
    </QueryClientProvider>
  )
}

function AppRoutes() {
  const { user } = useAuth()

  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        element={
          <RequireAuth>
            <AppLayout />
          </RequireAuth>
        }
      >
        <Route path="/alterar-password" element={<ChangePasswordPage />} />
        {/* Every signed-in user's own account — no permission gate, no id in the path (ADR-073). */}
        <Route path="/perfil" element={<ProfilePage />} />
        <Route index element={<DashboardPage />} />
        <Route
          path="/empresas"
          element={
            <RequirePermission permission={Permissions.Company.Read}>
              <CompaniesPage />
            </RequirePermission>
          }
        />
        <Route
          path="/tipos-equipamento"
          element={
            <RequirePermission permission={Permissions.EquipmentType.Read}>
              <EquipmentTypesPage />
            </RequirePermission>
          }
        />
        <Route
          path="/equipamentos"
          element={
            <RequirePermission permission={Permissions.Equipment.Read}>
              <EquipmentListPage />
            </RequirePermission>
          }
        />
        <Route
          path="/equipamentos/novo"
          element={
            <RequirePermission permission={Permissions.Equipment.Create}>
              <EquipmentCreatePage />
            </RequirePermission>
          }
        />
        <Route
          path="/equipamentos/:id"
          element={
            <RequirePermission permission={Permissions.Equipment.Read}>
              <EquipmentDetailPage />
            </RequirePermission>
          }
        />
        <Route
          path="/equipamentos/:id/editar"
          element={
            <RequirePermission permission={Permissions.Equipment.Update}>
              <EquipmentEditPage />
            </RequirePermission>
          }
        />
        <Route
          path="/consultores"
          element={
            <RequirePermission permission={Permissions.Consultant.Read}>
              <ConsultantsListPage />
            </RequirePermission>
          }
        />
        <Route
          path="/consultores/novo"
          element={
            <RequirePermission permission={Permissions.Consultant.Create}>
              <ConsultantCreatePage />
            </RequirePermission>
          }
        />
        <Route
          path="/consultores/:id"
          element={
            <RequirePermission permission={Permissions.Consultant.Read}>
              <ConsultantDetailPage />
            </RequirePermission>
          }
        />
        <Route
          path="/consultores/:id/editar"
          element={
            <RequirePermission permission={Permissions.Consultant.Update}>
              <ConsultantEditPage />
            </RequirePermission>
          }
        />
        <Route
          path="/utilizadores"
          element={
            <RequirePermission permission={Permissions.User.Read}>
              <UsersPage />
            </RequirePermission>
          }
        />
        <Route
          path="/auditoria"
          element={
            <RequirePermission permission={Permissions.Audit.Read}>
              <AuditPage />
            </RequirePermission>
          }
        />
        <Route
          path="/solicitacoes-abate"
          element={
            <RequirePermission permission={Permissions.EquipmentRetirementRequest.Read}>
              <SolicitacoesAbatePage />
            </RequirePermission>
          }
        />
        <Route
          path="/importacao-exportacao/consultores"
          element={
            <RequirePermission permission={Permissions.Consultant.Import}>
              <ConsultantImportExportPage />
            </RequirePermission>
          }
        />
        <Route
          path="/importacao-exportacao/equipamentos"
          element={
            <RequirePermission permission={Permissions.Equipment.Import}>
              <EquipmentImportExportPage />
            </RequirePermission>
          }
        />
        <Route
          path="/importacao-exportacao/inventario-atual"
          element={
            <RequirePermission permission={CurrentInventoryImportPermissions}>
              <CurrentInventoryImportPage />
            </RequirePermission>
          }
        />
        <Route
          path="/devolucoes"
          element={
            <RequirePermission permission={Permissions.EquipmentReturn.Read}>
              <ReturnsListPage />
            </RequirePermission>
          }
        />
        <Route
          path="/devolucoes/grupos/:groupId"
          element={
            <RequirePermission permission={Permissions.EquipmentReturn.Read}>
              <ReturnGroupDetailPage />
            </RequirePermission>
          }
        />
        <Route
          path="/devolucoes/:id"
          element={
            <RequirePermission permission={Permissions.EquipmentReturn.Read}>
              <ReturnDetailPage />
            </RequirePermission>
          }
        />
        <Route
          path="/manutencao"
          element={
            <RequirePermission permission={Permissions.EquipmentMaintenance.Read}>
              <MaintenancePage />
            </RequirePermission>
          }
        />
        <Route
          path="/entregas"
          element={
            <RequirePermission permission={Permissions.EquipmentHandover.Read}>
              <HandoversListPage />
            </RequirePermission>
          }
        />
        <Route
          path="/entregas/nova"
          element={
            <RequirePermission permission={Permissions.EquipmentHandover.Create}>
              <HandoverCreatePage />
            </RequirePermission>
          }
        />
        <Route
          path="/entregas/:id"
          element={
            <RequirePermission permission={Permissions.EquipmentHandover.Read}>
              <HandoverDetailPage />
            </RequirePermission>
          }
        />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
      <Route path="*" element={<Navigate to={user ? '/' : '/login'} replace />} />
    </Routes>
  )
}
