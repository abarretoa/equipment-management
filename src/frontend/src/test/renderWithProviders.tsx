import type { ReactElement } from 'react'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { vi } from 'vitest'
import { AuthProvider } from '../features/auth/AuthContext'

/** `url` carries the query string, for handlers that answer differently per filter. */
type Handler = (init: RequestInit | undefined, url: URL) => Response | Promise<Response>

/** JSON response helper for stubbed API handlers. */
export function json(status: number, body?: unknown): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/**
 * Stubs `fetch` at the network boundary, keyed by "METHOD /path". The real
 * httpClient, CSRF handling and AuthProvider run unchanged; an unhandled
 * request fails the test loudly instead of silently resolving.
 */
export function stubApi(handlers: Record<string, Handler>) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
    const parsed = new URL(url, 'http://localhost')
    const key = `${(init?.method ?? 'GET').toUpperCase()} ${parsed.pathname}`
    const handler = handlers[key]
    if (!handler) throw new Error(`Unhandled API request in test: ${key}`)
    return handler(init, parsed)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/** Renders inside the app's real providers (React Query + AuthProvider) and an in-memory router. */
export function renderWithProviders(ui: ReactElement, { route = '/' }: { route?: string } = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })

  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  )
}

export interface RecordedRequest {
  method: string
  url: URL
  /** The parsed JSON body, if any. */
  body: unknown
}

/** Every request a `stubApi` mock received, oldest first, with method, parsed URL and JSON body. */
export function recordedRequests(fetchMock: ReturnType<typeof stubApi>): RecordedRequest[] {
  return fetchMock.mock.calls.map(([input, init]) => {
    const href = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
    const body = typeof init?.body === 'string' ? (JSON.parse(init.body) as unknown) : undefined
    return { method: (init?.method ?? 'GET').toUpperCase(), url: new URL(href, 'http://localhost'), body }
  })
}
