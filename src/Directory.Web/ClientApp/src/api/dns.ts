import { get, post, put, del } from './client'
import type {
  DnsZone,
  DnsRecord,
  DnsZoneProperties,
  DnsForwarder,
  DnsStatistics,
  ScavengingSettings,
  CreateZoneRequest,
  CreateRecordRequest,
  UpdateRecordRequest,
  CreateForwarderRequest,
  UpdateZonePropertiesRequest,
} from '../types/dns'

// Zones
export function listZones() {
  return get<DnsZone[]>('/dns/zones')
}

export function createZone(request: CreateZoneRequest) {
  return post<DnsZone>('/dns/zones', request)
}

export function deleteZone(zoneName: string) {
  return del(`/dns/zones/${encodeURIComponent(zoneName)}`)
}

export function getZoneProperties(zoneName: string) {
  return get<DnsZoneProperties>(`/dns/zones/${encodeURIComponent(zoneName)}/properties`)
}

export function updateZoneProperties(zoneName: string, request: UpdateZonePropertiesRequest) {
  return put<DnsZoneProperties>(`/dns/zones/${encodeURIComponent(zoneName)}/properties`, request)
}

// Records
export function listRecords(zoneName: string, type?: string) {
  const params = type ? `?type=${encodeURIComponent(type)}` : ''
  return get<DnsRecord[]>(`/dns/zones/${encodeURIComponent(zoneName)}/records${params}`)
}

export function createRecord(zoneName: string, request: CreateRecordRequest) {
  return post<DnsRecord>(`/dns/zones/${encodeURIComponent(zoneName)}/records`, request)
}

export function updateRecord(zoneName: string, recordId: string, request: UpdateRecordRequest) {
  return put<DnsRecord>(`/dns/zones/${encodeURIComponent(zoneName)}/records/${encodeURIComponent(recordId)}`, request)
}

export function deleteRecord(zoneName: string, recordId: string) {
  return del(`/dns/zones/${encodeURIComponent(zoneName)}/records/${encodeURIComponent(recordId)}`)
}

// Forwarders
export function listForwarders() {
  return get<DnsForwarder[]>('/dns/forwarders')
}

export function createForwarder(request: CreateForwarderRequest) {
  return post<DnsForwarder>('/dns/forwarders', request)
}

export function deleteForwarder(domain: string) {
  return del(`/dns/forwarders/${encodeURIComponent(domain)}`)
}

// Scavenging
export function triggerScavenging() {
  return post<{ status: string; startedAt: string }>('/dns/scavenging')
}

export function getScavengingSettings() {
  return get<ScavengingSettings>('/dns/scavenging/settings')
}

export function updateScavengingSettings(settings: ScavengingSettings) {
  return put<ScavengingSettings>('/dns/scavenging/settings', settings)
}

// Statistics
export function getDnsStatistics() {
  return get<DnsStatistics>('/dns/statistics')
}

// SRV Registration
export function registerSrvRecords() {
  return post<{ registered: number; total: number; errors: string[] }>('/dns/register-srv')
}
