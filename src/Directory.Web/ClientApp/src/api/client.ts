import router from '../router'
import { useAuthStore } from '../stores/auth'

const BASE_URL = '/api/v1'

/** Paths that should not trigger a 401 redirect (to avoid loops). */
const AUTH_PATHS = ['/auth/login', '/auth/logout', '/auth/me']

function isAuthPath(path: string): boolean {
  return AUTH_PATHS.some((p) => path.endsWith(p))
}

/**
 * Handle a 401 response: clear the auth store, show a toast, and redirect to
 * /login with a returnUrl query param so the user lands back where they were.
 */
function handleUnauthorized(): void {
  const auth = useAuthStore()
  // Only act if the user was previously authenticated (avoids double-redirect)
  if (!auth.isAuthenticated && auth.checked) return

  auth.clearSession()

  const currentPath = router.currentRoute.value.fullPath
  // Don't redirect if already on the login page
  if (currentPath.startsWith('/login')) return

  // Dispatch a custom event that App.vue listens for to show the toast
  // (we can't use useToast outside a component setup context)
  window.dispatchEvent(new CustomEvent('session:expired'))

  router.push({ path: '/login', query: { returnUrl: currentPath, expired: '1' } })
}

async function handleError(res: Response, path: string): Promise<never> {
  if (res.status === 401 && !isAuthPath(path)) {
    handleUnauthorized()
    throw new Error('Session expired')
  }

  try {
    const body = await res.json()
    if (body && typeof body.detail === 'string') {
      throw new Error(body.detail)
    }
    if (body && typeof body.title === 'string') {
      throw new Error(body.title)
    }
  } catch (e) {
    if (e instanceof Error && e.message !== `API error: ${res.status} ${res.statusText}`) {
      throw e
    }
  }
  throw new Error(`API error: ${res.status} ${res.statusText}`)
}

export async function get<T>(path: string): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`)
  if (!res.ok) await handleError(res, path)
  return res.json()
}

export async function post<T>(path: string, body?: unknown): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : undefined,
  })
  if (!res.ok) await handleError(res, path)
  return res.status === 204 ? (undefined as T) : res.json()
}

export async function put<T>(path: string, body?: unknown): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : undefined,
  })
  if (!res.ok) await handleError(res, path)
  return res.status === 204 ? (undefined as T) : res.json()
}

export async function del(path: string, body?: unknown): Promise<void> {
  const options: RequestInit = { method: 'DELETE' }
  if (body !== undefined) {
    options.headers = { 'Content-Type': 'application/json' }
    options.body = JSON.stringify(body)
  }
  const res = await fetch(`${BASE_URL}${path}`, options)
  if (!res.ok) await handleError(res, path)
}

export async function getText(path: string): Promise<string> {
  const res = await fetch(`${BASE_URL}${path}`)
  if (!res.ok) await handleError(res, path)
  return res.text()
}

export async function postFormData<T>(path: string, formData: FormData): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    method: 'POST',
    body: formData,
  })
  if (!res.ok) await handleError(res, path)
  return res.status === 204 ? (undefined as T) : res.json()
}

export async function postForBlob(path: string, body?: unknown): Promise<Blob> {
  const res = await fetch(`${BASE_URL}${path}`, {
    method: 'POST',
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  })
  if (!res.ok) await handleError(res, path)
  return res.blob()
}

export async function fetchBlob(path: string): Promise<Blob> {
  const res = await fetch(`${BASE_URL}${path}`)
  if (!res.ok) await handleError(res, path)
  return res.blob()
}

export async function putRaw<T>(path: string, body: BodyInit, contentType: string): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    method: 'PUT',
    headers: { 'Content-Type': contentType },
    body,
  })
  if (!res.ok) await handleError(res, path)
  return res.status === 204 ? (undefined as T) : res.json()
}
