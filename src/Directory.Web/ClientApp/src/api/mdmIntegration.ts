import { get, post, put, del } from './client'
import type { MdmIntegration, DeviceComplianceStatus, MdmSyncResult } from '../types/mdm'

export function fetchMdmIntegrations() {
  return get<MdmIntegration[]>('/mdm/integrations')
}

export function createMdmIntegration(integration: Partial<MdmIntegration>) {
  return post<MdmIntegration>('/mdm/integrations', integration)
}

export function updateMdmIntegration(id: string, integration: Partial<MdmIntegration>) {
  return put<MdmIntegration>(`/mdm/integrations/${id}`, integration)
}

export function deleteMdmIntegration(id: string) {
  return del(`/mdm/integrations/${id}`)
}

export function fetchMdmDevices(integrationId?: string) {
  const query = integrationId ? `?integrationId=${integrationId}` : ''
  return get<DeviceComplianceStatus[]>(`/mdm/devices${query}`)
}

export function syncMdmDevices(integrationId?: string) {
  const query = integrationId ? `?integrationId=${integrationId}` : ''
  return post<MdmSyncResult>(`/mdm/devices/sync${query}`)
}
