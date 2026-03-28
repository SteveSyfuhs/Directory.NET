import { get, put, post, del, fetchBlob, putRaw } from './client'
import type { SysvolItem, SysvolReplicationStatus, SysvolConflict, SysvolConfig, SysvolConfigValidation } from '../types/sysvol'

// SYSVOL share configuration
export const fetchSysvolConfig = () => get<SysvolConfig>('/sysvol/config')

export const updateSysvolConfig = (config: SysvolConfig) => put<SysvolConfig>('/sysvol/config', config)

export const validateSysvolConfig = (config: SysvolConfig) => post<SysvolConfigValidation>('/sysvol/config/validate', config)

// SYSVOL file browser
export const fetchSysvolRoot = () => get<SysvolItem[]>('/sysvol')

export const browseSysvol = (path: string) => get<SysvolItem[]>(`/sysvol/browse/${encodeURIComponent(path)}`)

export const downloadSysvolFile = (path: string) =>
  fetchBlob(`/sysvol/file/${encodeURIComponent(path)}`)

export const uploadSysvolFile = (path: string, file: File) =>
  putRaw<SysvolItem>(`/sysvol/file/${encodeURIComponent(path)}`, file, file.type || 'application/octet-stream')

export const deleteSysvolFile = (path: string) => del(`/sysvol/file/${encodeURIComponent(path)}`)

export const fetchSysvolReplicationStatus = () => get<SysvolReplicationStatus>('/sysvol/replication/status')

export const fetchSysvolConflicts = () => get<SysvolConflict[]>('/sysvol/replication/conflicts')
