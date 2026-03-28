import { get, post, put, del } from './client'

export interface RetentionPolicy {
  id: string
  name: string
  description: string
  target: string
  retentionDays: number
  action: string
  isEnabled: boolean
  lastAppliedAt: string | null
  lastPurgedCount: number | null
  createdAt: string
}

export interface RetentionPreview {
  policyId: string
  policyName: string
  target: string
  affectedCount: number
  sampleItems: string[]
  previewedAt: string
}

export interface RetentionRunResult {
  policyId: string
  processedCount: number
  purgedCount: number
  errorCount: number
  status: string
  completedAt: string
}

export function fetchRetentionPolicies() {
  return get<RetentionPolicy[]>('/retention/policies')
}

export function fetchRetentionPolicy(id: string) {
  return get<RetentionPolicy>(`/retention/policies/${id}`)
}

export function createRetentionPolicy(policy: Partial<RetentionPolicy>) {
  return post<RetentionPolicy>('/retention/policies', policy)
}

export function updateRetentionPolicy(id: string, policy: Partial<RetentionPolicy>) {
  return put<RetentionPolicy>(`/retention/policies/${id}`, policy)
}

export function deleteRetentionPolicy(id: string) {
  return del(`/retention/policies/${id}`)
}

export function runRetentionPolicy(id: string) {
  return post<RetentionRunResult>(`/retention/policies/${id}/run`)
}

export function previewRetentionPolicy(id: string) {
  return get<RetentionPreview>(`/retention/preview/${id}`)
}
