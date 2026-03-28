import { get, post, del } from './client'
import type { GmsaAccount, KdsRootKey, CreateGmsaRequest } from '../types/gmsa'

export const listGmsaAccounts = () => get<GmsaAccount[]>('/gmsa')

export const getGmsaAccount = (name: string) => get<GmsaAccount>(`/gmsa/${encodeURIComponent(name)}`)

export const createGmsaAccount = (payload: CreateGmsaRequest) =>
  post<GmsaAccount>('/gmsa', payload)

export const deleteGmsaAccount = (name: string) => del(`/gmsa/${encodeURIComponent(name)}`)

export const rotateGmsaPassword = (name: string) =>
  post<GmsaAccount>(`/gmsa/${encodeURIComponent(name)}/rotate`)

export const listKdsRootKeys = () => get<KdsRootKey[]>('/gmsa/kds-root-keys')

export const createKdsRootKey = () => post<KdsRootKey>('/gmsa/kds-root-keys')
