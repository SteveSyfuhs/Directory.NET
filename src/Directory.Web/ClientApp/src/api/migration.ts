import { get, post, put, del } from './client'
import type {
  MigrationSource,
  MigrationPlan,
  MigrationPreview,
  MigrationResult,
  MigrationHistoryEntry,
  ConnectionTestResult,
  SchemaDiscoveryResult,
} from '../types/migration'

export function fetchMigrationSources() {
  return get<MigrationSource[]>('/migration/sources')
}

export function createMigrationSource(source: Partial<MigrationSource>) {
  return post<MigrationSource>('/migration/sources', source)
}

export function updateMigrationSource(id: string, source: Partial<MigrationSource>) {
  return put<MigrationSource>(`/migration/sources/${id}`, source)
}

export function deleteMigrationSource(id: string) {
  return del(`/migration/sources/${id}`)
}

export function testMigrationSource(source: Partial<MigrationSource>) {
  return post<ConnectionTestResult>('/migration/sources/test', source)
}

export function discoverMigrationSchema(source: Partial<MigrationSource>) {
  return post<SchemaDiscoveryResult>('/migration/sources/discover', source)
}

export function previewMigration(plan: Partial<MigrationPlan>) {
  return post<MigrationPreview>('/migration/preview', plan)
}

export function executeMigration(plan: Partial<MigrationPlan>) {
  return post<MigrationResult>('/migration/execute', plan)
}

export function fetchMigrationStatus(id: string) {
  return get<MigrationResult>(`/migration/status/${id}`)
}

export function fetchMigrationHistory() {
  return get<MigrationHistoryEntry[]>('/migration/history')
}
