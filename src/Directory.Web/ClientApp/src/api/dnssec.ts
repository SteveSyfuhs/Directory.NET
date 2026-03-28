import { get, post, put, del } from './client'
import type {
  DnssecSettings,
  DnssecKey,
  DnssecDsRecord,
  UpdateDnssecSettingsRequest,
  GenerateKeyRequest,
  SignZoneResult,
} from '../types/dnssec'

export function getDnssecSettings(zone: string) {
  return get<DnssecSettings>(`/dns/dnssec/${encodeURIComponent(zone)}`)
}

export function updateDnssecSettings(zone: string, request: UpdateDnssecSettingsRequest) {
  return put<DnssecSettings>(`/dns/dnssec/${encodeURIComponent(zone)}`, request)
}

export function signZone(zone: string) {
  return post<SignZoneResult>(`/dns/dnssec/${encodeURIComponent(zone)}/sign`)
}

export function listDnssecKeys(zone: string) {
  return get<DnssecKey[]>(`/dns/dnssec/${encodeURIComponent(zone)}/keys`)
}

export function generateDnssecKey(zone: string, request: GenerateKeyRequest) {
  return post<DnssecKey>(`/dns/dnssec/${encodeURIComponent(zone)}/keys`, request)
}

export function deleteDnssecKey(zone: string, keyId: string) {
  return del(`/dns/dnssec/${encodeURIComponent(zone)}/keys/${encodeURIComponent(keyId)}`)
}

export function getDsRecord(zone: string) {
  return get<DnssecDsRecord>(`/dns/dnssec/${encodeURIComponent(zone)}/ds`)
}
