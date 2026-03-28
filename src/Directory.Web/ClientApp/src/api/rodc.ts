import { get, post, put } from './client'
import type {
  RodcSettings,
  UpdateRodcSettingsRequest,
  PasswordCacheResponse,
  PasswordCachePrincipalRequest,
  PasswordCacheUpdateResponse,
  RodcReplicationResult,
} from '../types/rodc'

export function getRodcSettings() {
  return get<RodcSettings>('/rodc/settings')
}

export function updateRodcSettings(request: UpdateRodcSettingsRequest) {
  return put<RodcSettings>('/rodc/settings', request)
}

export function getPasswordCache() {
  return get<PasswordCacheResponse>('/rodc/password-cache')
}

export function addPasswordCachePrincipal(request: PasswordCachePrincipalRequest) {
  return post<PasswordCacheUpdateResponse>('/rodc/password-cache/add', request)
}

export function removePasswordCachePrincipal(request: PasswordCachePrincipalRequest) {
  return post<PasswordCacheUpdateResponse>('/rodc/password-cache/remove', request)
}

export function triggerReplication() {
  return post<RodcReplicationResult>('/rodc/replicate')
}
