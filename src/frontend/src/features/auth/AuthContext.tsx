import { createContext, useContext, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, invalidateCsrfToken } from '../../shared/api/httpClient'
import * as authApi from './api'
import type { CurrentUser } from './types'

const CURRENT_USER_QUERY_KEY = ['auth', 'me']

interface AuthContextValue {
  user: CurrentUser | null
  isLoading: boolean
  login: (email: string, password: string) => Promise<CurrentUser>
  loginError: string | null
  isLoggingIn: boolean
  logout: () => Promise<void>
  hasPermission: (permission: string) => boolean
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()

  const meQuery = useQuery({
    queryKey: CURRENT_USER_QUERY_KEY,
    queryFn: authApi.fetchCurrentUser,
    retry: false,
    staleTime: 5 * 60 * 1000,
  })

  const loginMutation = useMutation({
    mutationFn: ({ email, password }: { email: string; password: string }) =>
      authApi.login(email, password),
    onSuccess: (user) => {
      queryClient.setQueryData(CURRENT_USER_QUERY_KEY, user)
      // The token used for this very login request was fetched anonymously and
      // is now bound to "no user" — invalidate it so the next mutation fetches
      // one bound to the freshly authenticated session (see invalidateCsrfToken's doc).
      invalidateCsrfToken()
    },
  })

  const logoutMutation = useMutation({
    mutationFn: authApi.logout,
    onSettled: () => {
      queryClient.setQueryData(CURRENT_USER_QUERY_KEY, null)
      queryClient.clear()
      invalidateCsrfToken()
    },
  })

  const user = meQuery.data ?? null

  const value: AuthContextValue = {
    user,
    isLoading: meQuery.isLoading,
    login: (email, password) => loginMutation.mutateAsync({ email, password }),
    loginError:
      loginMutation.error instanceof ApiError ? loginMutation.error.message : loginMutation.error ? 'Ocorreu um erro inesperado.' : null,
    isLoggingIn: loginMutation.isPending,
    logout: () => logoutMutation.mutateAsync(),
    hasPermission: (permission: string) => user?.permissions.includes(permission) ?? false,
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used within an AuthProvider')
  return context
}
