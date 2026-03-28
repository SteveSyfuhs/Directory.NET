import { get } from './client'

export interface AuditLogEntry {
  id: string
  timestamp: string
  action: string
  targetDn: string
  objectClass: string
  actor: string
  sourceIp: string
  success: boolean
  details?: string
}

export interface AuditLogResult {
  items: AuditLogEntry[]
  totalCount: number
}

export interface AuditLogParams {
  startDate?: string
  endDate?: string
  action?: string
  targetDn?: string
  pageSize?: number
  page?: number
}

export function fetchAuditLog(params: AuditLogParams = {}) {
  const qs = new URLSearchParams()
  if (params.startDate) qs.set('startDate', params.startDate)
  if (params.endDate) qs.set('endDate', params.endDate)
  if (params.action) qs.set('action', params.action)
  if (params.targetDn) qs.set('targetDn', params.targetDn)
  if (params.pageSize) qs.set('pageSize', String(params.pageSize))
  if (params.page) qs.set('page', String(params.page))
  return get<AuditLogResult>(`/audit?${qs.toString()}`)
}
