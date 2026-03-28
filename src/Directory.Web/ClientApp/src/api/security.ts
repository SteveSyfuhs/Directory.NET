import { get, put, post, del } from './client'
import type { SecurityDescriptorInfo, EffectivePermissions } from './types'
import type { AceDto } from '../types/security'

export function getObjectSecurity(dn: string) {
  return get<SecurityDescriptorInfo>(`/objects/by-dn/security?dn=${encodeURIComponent(dn)}`)
}

export function getEffectivePermissions(dn: string, principalDn: string) {
  return get<EffectivePermissions>(
    `/objects/by-dn/effective-permissions?dn=${encodeURIComponent(dn)}&principalDn=${encodeURIComponent(principalDn)}`
  )
}

export function updateOwner(dn: string, ownerSid: string): Promise<SecurityDescriptorInfo> {
  return put<SecurityDescriptorInfo>('/objects/by-dn/security/owner', { dn, ownerSid })
}

export function updateDacl(dn: string, aces: AceDto[]): Promise<SecurityDescriptorInfo> {
  return put<SecurityDescriptorInfo>('/objects/by-dn/security/dacl', { dn, aces })
}

export function addAce(dn: string, ace: AceDto): Promise<SecurityDescriptorInfo> {
  return post<SecurityDescriptorInfo>('/objects/by-dn/security/dacl/ace', { dn, ace })
}

export function removeAce(dn: string, aceIndex: number): Promise<SecurityDescriptorInfo> {
  return del('/objects/by-dn/security/dacl/ace', { dn, aceIndex }) as unknown as Promise<SecurityDescriptorInfo>
}

export function setInheritance(dn: string, enabled: boolean): Promise<SecurityDescriptorInfo> {
  return put<SecurityDescriptorInfo>('/objects/by-dn/security/inherit', { dn, enabled })
}

export function propagateInheritance(dn: string): Promise<{ propagated: number }> {
  return post<{ propagated: number }>('/objects/by-dn/security/propagate', { dn })
}
