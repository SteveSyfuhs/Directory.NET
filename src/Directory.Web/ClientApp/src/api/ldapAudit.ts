import { get } from './client'

export interface LdapAuditEntry {
  id: string
  timestamp: string
  operation: string
  clientIp: string
  clientPort: number
  boundDn: string
  targetDn: string
  resultCode: string
  durationMs: number
  details: Record<string, string>
}

export interface LdapAuditResult {
  items: LdapAuditEntry[]
}

export interface LdapAuditStatistics {
  operationsPerSecond: number
  totalEntries: number
  topOperations: Record<string, number>
  topClients: Record<string, number>
  topResultCodes: Record<string, number>
  averageDurationMs: number
}

export interface LdapActiveConnection {
  clientIp: string
  clientPort: number
  boundDn: string
  connectedSince: string
  lastActivity: string
  requestCount: number
}

export interface LdapAuditParams {
  operation?: string
  clientIp?: string
  boundDn?: string
  targetDn?: string
  from?: string
  to?: string
  limit?: number
}

export function fetchLdapAudit(params: LdapAuditParams = {}) {
  const qs = new URLSearchParams()
  if (params.operation) qs.set('operation', params.operation)
  if (params.clientIp) qs.set('clientIp', params.clientIp)
  if (params.boundDn) qs.set('boundDn', params.boundDn)
  if (params.targetDn) qs.set('targetDn', params.targetDn)
  if (params.from) qs.set('from', params.from)
  if (params.to) qs.set('to', params.to)
  if (params.limit) qs.set('limit', String(params.limit))
  return get<LdapAuditResult>(`/ldap-audit?${qs.toString()}`)
}

export function fetchLdapAuditStatistics() {
  return get<LdapAuditStatistics>('/ldap-audit/statistics')
}

export function fetchLdapActiveConnections() {
  return get<LdapActiveConnection[]>('/ldap-audit/active-connections')
}
