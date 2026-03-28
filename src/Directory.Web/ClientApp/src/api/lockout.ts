import { get, put, post } from './client'
import type { LockoutPolicy, LockoutInfo } from '../types/lockout'

export function getLockoutPolicy() {
  return get<LockoutPolicy>('/lockout/policy')
}

export function updateLockoutPolicy(policy: Partial<LockoutPolicy>) {
  return put<LockoutPolicy>('/lockout/policy', policy)
}

export function getLockoutStatus(dn: string) {
  return get<LockoutInfo>(`/lockout/status/${encodeURIComponent(dn)}`)
}

export function unlockAccount(dn: string) {
  return post<{ message: string }>(`/lockout/unlock/${encodeURIComponent(dn)}`)
}

export function getLockedAccounts() {
  return get<LockoutInfo[]>('/lockout/locked-accounts')
}
