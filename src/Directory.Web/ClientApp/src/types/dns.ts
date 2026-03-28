export interface DnsZone {
  name: string
  type: string
  isReverse: boolean
  status: string
  dynamicUpdate: string
}

export interface DnsRecord {
  id: string
  name: string
  type: string
  data: string
  ttl: number
}

export interface DnsZoneProperties {
  name: string
  type: string
  status: string
  dynamicUpdate: string
  soa: DnsSoa
  nameServers: string[]
  zoneTransfers: DnsZoneTransfers
  aging: DnsAging
}

export interface DnsSoa {
  primaryServer: string
  responsiblePerson: string
  serial: number
  refresh: number
  retry: number
  expire: number
  minimumTtl: number
}

export interface DnsZoneTransfers {
  allowTransfer: string
  notifyServers: string[]
}

export interface DnsAging {
  agingEnabled: boolean
  noRefreshInterval: number
  refreshInterval: number
}

export interface DnsForwarder {
  domain: string
  servers: string[]
}

export interface DnsStatistics {
  serverHostname: string
  port: number
  zoneCount: number
  forwarderCount: number
  recordCount: number
  uptime: string
  serverIpAddresses: string[]
}

export interface ScavengingSettings {
  enabled: boolean
  noRefreshIntervalHours: number
  refreshIntervalHours: number
}

export interface CreateZoneRequest {
  name: string
  type: string
  reverseZone: boolean
  dynamicUpdate?: string
}

export interface CreateRecordRequest {
  name: string
  type: string
  data: string
  ttl?: number
}

export interface UpdateRecordRequest {
  name: string
  type: string
  data: string
  ttl?: number
}

export interface CreateForwarderRequest {
  domain: string
  servers: string[]
}

export interface UpdateZonePropertiesRequest {
  dynamicUpdate?: string
  soa?: Partial<DnsSoa>
  nameServers?: string[]
  zoneTransfers?: Partial<DnsZoneTransfers>
  aging?: Partial<DnsAging>
}

export type DnsRecordType = 'A' | 'AAAA' | 'CNAME' | 'MX' | 'SRV' | 'PTR' | 'NS' | 'SOA' | 'TXT'

export const DNS_RECORD_TYPES: DnsRecordType[] = ['A', 'AAAA', 'CNAME', 'MX', 'SRV', 'PTR', 'NS', 'SOA', 'TXT']

export const RECORD_TYPE_COLORS: Record<string, string> = {
  A: '#3b82f6',
  AAAA: '#8b5cf6',
  CNAME: '#f97316',
  MX: '#22c55e',
  SRV: '#14b8a6',
  PTR: '#eab308',
  NS: '#6b7280',
  SOA: '#ef4444',
  TXT: '#6366f1',
}
