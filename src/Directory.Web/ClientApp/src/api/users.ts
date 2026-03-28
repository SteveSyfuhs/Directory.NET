import { get, post, put } from './client'
import type { ObjectSummary } from './types'

export interface CreateUserPayload {
  containerDn: string
  cn: string
  samAccountName: string
  userPrincipalName?: string
  givenName?: string
  sn?: string
  displayName?: string
  password: string
  mustChangePasswordAtNextLogon?: boolean
  accountDisabled?: boolean
  extraAttributes?: Record<string, string>
}

export const createUser = (payload: CreateUserPayload) => post<{ objectGuid: string }>('/users', payload)

export const enableUser = (guid: string) => put<void>(`/users/${guid}/enable`)

export const disableUser = (guid: string) => put<void>(`/users/${guid}/disable`)

export function resetPassword(guid: string, password: string, mustChangeAtNextLogon: boolean) {
  return post<void>(`/users/${guid}/reset-password`, { password, mustChangeAtNextLogon })
}

export const unlockUser = (guid: string) => put<void>(`/users/${guid}/unlock`)

export const getUserGroups = (guid: string) => get<ObjectSummary[]>(`/users/${guid}/groups`)

export const getDirectReports = (guid: string) => get<ObjectSummary[]>(`/users/${guid}/direct-reports`)

export function updateDelegation(guid: string, delegationType: string, allowedServices?: string[]) {
  return put<void>(`/users/${guid}/delegation`, { delegationType, allowedServices })
}

export function updateLogonHours(guid: string, hours: string | null) {
  return put<void>(`/users/${guid}/logon-hours`, { hours })
}

export function updateLogonWorkstations(guid: string, workstations: string[]) {
  return put<void>(`/users/${guid}/logon-workstations`, { workstations })
}
