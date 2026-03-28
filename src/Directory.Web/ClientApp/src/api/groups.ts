import { get, post, del } from './client'
import type { ObjectSummary } from './types'

export interface CreateGroupPayload {
  containerDn: string
  cn: string
  samAccountName: string
  description?: string
  groupScope: 'Global' | 'DomainLocal' | 'Universal'
  groupType: 'Security' | 'Distribution'
  extraAttributes?: Record<string, string>
}

export const createGroup = (payload: CreateGroupPayload) => post<{ objectGuid: string }>('/groups', payload)

export const getGroupMembers = (guid: string) => get<ObjectSummary[]>(`/groups/${guid}/members`)

export const addGroupMember = (guid: string, memberDn: string) =>
  post<void>(`/groups/${guid}/members`, { memberDn })

export const removeGroupMember = (guid: string, memberDn: string) =>
  del(`/groups/${guid}/members/${encodeURIComponent(memberDn)}`)
