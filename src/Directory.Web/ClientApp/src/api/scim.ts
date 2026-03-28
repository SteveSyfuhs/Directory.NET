import { get, post, put, del } from './client'
import type { ScimIntegration, ScimOperationLog } from '../types/scim'

export function fetchScimIntegrations() {
  return get<ScimIntegration[]>('/scim-integrations')
}

export function fetchScimIntegration(id: string) {
  return get<ScimIntegration>(`/scim-integrations/${id}`)
}

export function createScimIntegration(integration: Partial<ScimIntegration>) {
  return post<ScimIntegration>('/scim-integrations', integration)
}

export function updateScimIntegration(id: string, integration: Partial<ScimIntegration>) {
  return put<ScimIntegration>(`/scim-integrations/${id}`, integration)
}

export function deleteScimIntegration(id: string) {
  return del(`/scim-integrations/${id}`)
}

export function fetchScimOperationLogs(id: string) {
  return get<ScimOperationLog[]>(`/scim-integrations/${id}/logs`)
}

export function fetchDefaultScimAttributeMapping() {
  return get<Record<string, string>>('/scim-integrations/attribute-mapping/defaults')
}
