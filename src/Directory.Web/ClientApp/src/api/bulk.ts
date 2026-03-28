import { post } from './client'
import type { BulkResponse } from './types'

export interface BulkModification {
  attribute: string
  operation: 'set' | 'add' | 'remove' | 'clear'
  values?: unknown[]
}

export function bulkModify(dns: string[], modifications: BulkModification[]) {
  return post<BulkResponse>('/bulk/modify', { dns, modifications })
}

export function bulkMove(dns: string[], targetDn: string) {
  return post<BulkResponse>('/bulk/move', { dns, targetDn })
}

export function bulkEnable(dns: string[]) {
  return post<BulkResponse>('/bulk/enable', { dns })
}

export function bulkDisable(dns: string[]) {
  return post<BulkResponse>('/bulk/disable', { dns })
}

export function bulkDelete(dns: string[]) {
  return post<BulkResponse>('/bulk/delete', { dns })
}

export function bulkResetPassword(dns: string[], password: string, mustChangeAtNextLogon: boolean) {
  return post<BulkResponse>('/bulk/reset-password', { dns, password, mustChangeAtNextLogon })
}
