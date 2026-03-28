const BASE_URL = '/api/v1/auth'

export interface UserInfo {
  username: string
  displayName: string
  upn: string
  dn: string
  groups: string[]
  roles: string[]
  permissions: string[]
}

async function handleAuthError(res: Response): Promise<never> {
  try {
    const body = await res.json()
    if (body && typeof body.detail === 'string') throw new Error(body.detail)
    if (body && typeof body.title === 'string') throw new Error(body.title)
  } catch (e) {
    if (e instanceof Error && !e.message.startsWith('API error:')) throw e
  }
  throw new Error(`API error: ${res.status} ${res.statusText}`)
}

export async function login(username: string, password: string): Promise<UserInfo> {
  const res = await fetch(`${BASE_URL}/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ username, password }),
  })
  if (!res.ok) await handleAuthError(res)
  return res.json()
}

export async function logout(): Promise<void> {
  const res = await fetch(`${BASE_URL}/logout`, {
    method: 'POST',
    credentials: 'include',
  })
  if (!res.ok && res.status !== 401) await handleAuthError(res)
}

export async function getMe(): Promise<UserInfo | null> {
  const res = await fetch(`${BASE_URL}/me`, {
    credentials: 'include',
  })
  if (res.status === 401) return null
  if (!res.ok) await handleAuthError(res)
  return res.json()
}
