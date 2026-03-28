import { get, post } from './client'
import type { SchemaAttribute, SchemaClass } from './types'

export const fetchAttributes = () => get<SchemaAttribute[]>('/schema/attributes')
export const fetchClasses = () => get<SchemaClass[]>('/schema/classes')

// Schema replication
export interface SchemaReplicationStatus {
  currentSchemaVersion: number
  schemaUsn: number
  lastSyncTime: string
  originServer: string
  pendingChanges: number
  attributeCount: number
  classCount: number
  health: string
}

export interface SchemaChangeEntry {
  id: string
  changeType: string
  objectName: string
  schemaVersion: number
  timestamp: string
  originServer: string
  changes: Record<string, unknown>
}

export const fetchSchemaReplicationStatus = () => get<SchemaReplicationStatus>('/schema/replication/status')
export const fetchSchemaReplicationHistory = (count?: number) =>
  get<SchemaChangeEntry[]>(`/schema/replication/history${count ? `?count=${count}` : ''}`)
export const forceSchemaSync = () => post<{ message: string; timestamp: string }>('/schema/replication/sync')
