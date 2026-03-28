import { get, post, put, del } from './client'
import type {
  SmartCardMapping,
  SmartCardSettings,
  SmartCardAuthResult,
  MappingType,
} from '../types/smartCard'

export const authenticateWithCertificate = (certificateData: string) =>
  post<SmartCardAuthResult>('/smartcard/authenticate', { certificateData })

export const getMappings = (dn: string) =>
  get<SmartCardMapping[]>(`/smartcard/mappings/${encodeURIComponent(dn)}`)

export const createMapping = (userDn: string, certificateData: string, mappingType?: MappingType) =>
  post<SmartCardMapping>('/smartcard/mappings', { userDn, certificateData, mappingType })

export const deleteMapping = (dn: string, id: string) =>
  del(`/smartcard/mappings/${encodeURIComponent(dn)}/${encodeURIComponent(id)}`)

export const getSettings = () =>
  get<SmartCardSettings>('/smartcard/settings')

export const updateSettings = (settings: SmartCardSettings) =>
  put<SmartCardSettings>('/smartcard/settings', settings)
