/**
 * Fetch layer for the cookie-authenticated API. Every mutating request carries
 * the double-submit CSRF header (docs/SECURITY.md, "Request integrity";
 * ADR-004) fetched once from `GET /api/antiforgery/token` and cached in memory
 * — never in localStorage, matching the backend's own no-token-storage stance.
 */
const API_BASE_URL: string = import.meta.env.VITE_API_BASE_URL ?? ''

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(status: number, message: string, problem: ProblemDetails | null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
  errors?: Record<string, string[]>
}

let csrfTokenPromise: Promise<string> | null = null

async function fetchCsrfToken(): Promise<string> {
  const response = await fetch(`${API_BASE_URL}/api/antiforgery/token`, { credentials: 'include' })
  const body = (await response.json()) as { token: string }
  return body.token
}

/** Called once at app bootstrap; also transparently re-fetched if a mutation is rejected for a stale token. */
export function getCsrfToken(): Promise<string> {
  csrfTokenPromise ??= fetchCsrfToken()
  return csrfTokenPromise
}

function resetCsrfToken() {
  csrfTokenPromise = null
}

/**
 * ASP.NET Core's antiforgery token generator embeds the current user's
 * identity into the token/cookie pair (`DefaultAntiforgeryTokenGenerator`) —
 * so a token fetched while anonymous is bound to "no user" and becomes
 * invalid the instant login succeeds (and again on logout, back the other
 * way). The auth flow must call this right after both transitions so the
 * next mutation lazily fetches a token bound to the new auth state, instead
 * of surfacing a confusing "CSRF token missing or invalid" error.
 */
export function invalidateCsrfToken() {
  resetCsrfToken()
}

const MUTATING_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

async function withCsrfHeader(method: string, headers: HeadersInit): Promise<HeadersInit> {
  if (!MUTATING_METHODS.has(method.toUpperCase())) return headers
  const token = await getCsrfToken()
  return { ...headers, 'X-CSRF-TOKEN': token }
}

async function parseProblem(response: Response): Promise<ProblemDetails | null> {
  try {
    return (await response.clone().json()) as ProblemDetails
  } catch {
    return null
  }
}

function problemMessage(problem: ProblemDetails | null, fallback: string): string {
  if (problem?.errors) {
    const first = Object.values(problem.errors)[0]?.[0]
    if (first) return first
  }
  return problem?.detail ?? problem?.title ?? fallback
}

async function request(path: string, init: RequestInit = {}): Promise<Response> {
  const method = init.method ?? 'GET'
  const headers = await withCsrfHeader(method, {
    Accept: 'application/json',
    ...(init.body && !(init.body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}),
    ...init.headers,
  })

  let response = await fetch(`${API_BASE_URL}${path}`, { ...init, method, credentials: 'include', headers })

  // A 400 from the antiforgery filter itself (Api.Filters.ValidateCsrfAttribute
  // — see its exact "CSRF token missing or invalid." title) means our cached
  // token/cookie pair went stale (e.g. a long-idle tab, or an auth-state
  // transition invalidateCsrfToken() missed) — refresh once and retry
  // transparently rather than surfacing a confusing error.
  if (response.status === 400 && MUTATING_METHODS.has(method.toUpperCase())) {
    const problem = await parseProblem(response)
    if (problem?.title?.toLowerCase().includes('csrf')) {
      resetCsrfToken()
      const retryHeaders = await withCsrfHeader(method, headers)
      response = await fetch(`${API_BASE_URL}${path}`, { ...init, method, credentials: 'include', headers: retryHeaders })
    }
  }

  return response
}

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await request(path, init)

  if (!response.ok) {
    const problem = await parseProblem(response)
    throw new ApiError(response.status, problemMessage(problem, response.statusText), problem)
  }

  // Not just `status === 204`: at least one endpoint (auth/change-password)
  // returns a bare `Ok()` — 200 with an empty body, not 204 — and any future
  // one might too. `response.json()` throws a SyntaxError on an empty body,
  // which every caller's `onError` then reports as a generic failure even
  // though the request fully succeeded (Phase 10 hardening — this exact
  // mismatch was reproduced live: a 200 change-password response rendered as
  // "Não foi possível alterar a palavra-passe."). Treat any empty body as
  // success with no payload, regardless of status code.
  const text = await response.text()
  if (!text) {
    return undefined as T
  }

  return JSON.parse(text) as T
}

export async function apiFetchBlob(path: string, init?: RequestInit): Promise<Blob> {
  const response = await request(path, init)

  if (!response.ok) {
    const problem = await parseProblem(response)
    throw new ApiError(response.status, problemMessage(problem, response.statusText), problem)
  }

  return response.blob()
}

/**
 * Builds a query string, skipping undefined/null/empty values. Generic over
 * `object` (rather than `Record<string, ...>`) so callers can pass their own
 * named `*ListParams`/`*Filters` interfaces directly, without needing an
 * explicit index signature on every one of them.
 */
export function buildQuery<T extends object>(params: T): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params) as [string, string | number | boolean | undefined | null][]) {
    if (value === undefined || value === null || value === '') continue
    search.set(key, String(value))
  }
  const query = search.toString()
  return query ? `?${query}` : ''
}

/** Triggers a browser save for a downloaded Blob without a public URL (Phase 9 scope). */
export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  URL.revokeObjectURL(url)
}
