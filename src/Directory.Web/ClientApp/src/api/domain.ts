import { get, put, post, del } from './client'
import type { DomainConfig, PasswordPolicy } from './types'

export const fetchDomainConfig = () => get<DomainConfig>('/domain/config')
export const fetchPasswordPolicy = () => get<PasswordPolicy>('/domain/password-policy')
export const updatePasswordPolicy = (policy: PasswordPolicy) => put<PasswordPolicy>('/domain/password-policy', policy)

// Functional Level
export const fetchFunctionalLevel = () => get<{
  domainLevel: number
  forestLevel: number
  possibleDomainLevels: { level: number; name: string }[]
  possibleForestLevels: { level: number; name: string }[]
}>('/domain/functional-level')

export const raiseDomainLevel = (targetLevel: number) =>
  post<{ message: string; newLevel: number }>('/domain/raise-domain-level', { targetLevel })

export const raiseForestLevel = (targetLevel: number) =>
  post<{ message: string; newLevel: number }>('/domain/raise-forest-level', { targetLevel })

// UPN Suffixes
export const fetchUpnSuffixes = () => get<{
  defaultSuffix: string
  alternativeSuffixes: string[]
}>('/domain/upn-suffixes')

export const addUpnSuffix = (suffix: string) =>
  post<{ suffix: string }>('/domain/upn-suffixes', { suffix })

export const deleteUpnSuffix = (suffix: string) =>
  del(`/domain/upn-suffixes/${encodeURIComponent(suffix)}`)
