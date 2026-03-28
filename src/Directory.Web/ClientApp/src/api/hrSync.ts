import { get, post, put, del } from './client'
import type {
  HrSyncConfiguration,
  HrSyncHistoryEntry,
  HrSyncPreviewResult,
  HrSyncStatus,
} from '../types/hrSync'

export function fetchHrSyncConfigurations() {
  return get<HrSyncConfiguration[]>('/hr-sync')
}

export function fetchHrSyncConfiguration(id: string) {
  return get<HrSyncConfiguration>(`/hr-sync/${id}`)
}

export function createHrSyncConfiguration(config: Partial<HrSyncConfiguration>) {
  return post<HrSyncConfiguration>('/hr-sync', config)
}

export function updateHrSyncConfiguration(id: string, config: Partial<HrSyncConfiguration>) {
  return put<HrSyncConfiguration>(`/hr-sync/${id}`, config)
}

export function deleteHrSyncConfiguration(id: string) {
  return del(`/hr-sync/${id}`)
}

export function triggerHrSync(id: string) {
  return post<HrSyncHistoryEntry>(`/hr-sync/${id}/sync`)
}

export function fetchHrSyncStatus(id: string) {
  return get<HrSyncStatus>(`/hr-sync/${id}/status`)
}

export function fetchHrSyncHistory(id: string) {
  return get<HrSyncHistoryEntry[]>(`/hr-sync/${id}/history`)
}

export function previewHrSync(id: string) {
  return post<HrSyncPreviewResult>(`/hr-sync/${id}/preview`)
}

export function fetchHrSyncSourceTypes() {
  return get<string[]>('/hr-sync/source-types')
}

export function fetchDefaultHrAttributeMapping() {
  return get<Record<string, string>>('/hr-sync/attribute-mapping/defaults')
}
