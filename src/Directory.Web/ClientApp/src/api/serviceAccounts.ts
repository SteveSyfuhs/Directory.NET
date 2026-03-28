import { get, post, put, del } from './client'

export interface ServiceAccountSummary {
  objectGuid: string
  name: string
  dn: string
  type: 'MSA' | 'gMSA'
  dnsHostName: string
  passwordInterval: number
  principalCount: number
  enabled: boolean
  whenCreated: string
  whenChanged: string
}

export interface ServiceAccountDetail extends ServiceAccountSummary {
  samAccountName: string
  principals: string[]
  servicePrincipalNames: string[]
  description: string
  objectSid: string
}

export interface CreateServiceAccountPayload {
  name: string
  type: 'msa' | 'gmsa'
  dnsHostName?: string
  principals?: string[]
  passwordInterval?: number
  servicePrincipalNames?: string[]
}

export interface UpdateServiceAccountPayload {
  dnsHostName?: string
  description?: string
  passwordInterval?: number
  servicePrincipalNames?: string[]
}

export const listServiceAccounts = () => get<ServiceAccountSummary[]>('/service-accounts')

export const getServiceAccount = (id: string) => get<ServiceAccountDetail>(`/service-accounts/${id}`)

export const createServiceAccount = (payload: CreateServiceAccountPayload) =>
  post<ServiceAccountDetail>('/service-accounts', payload)

export const updateServiceAccount = (id: string, payload: UpdateServiceAccountPayload) =>
  put<ServiceAccountDetail>(`/service-accounts/${id}`, payload)

export const deleteServiceAccount = (id: string) => del(`/service-accounts/${id}`)

export const addPrincipal = (id: string, principalDn: string) =>
  post<void>(`/service-accounts/${id}/principals`, { principalDn })

export const removePrincipal = (id: string, dn: string) =>
  del(`/service-accounts/${id}/principals/${encodeURIComponent(dn)}`)

export const enableServiceAccount = (id: string) => put<ServiceAccountDetail>(`/service-accounts/${id}/enable`)

export const disableServiceAccount = (id: string) => put<ServiceAccountDetail>(`/service-accounts/${id}/disable`)
