export type MigrationSourceType = 'ActiveDirectory' | 'OpenLDAP' | 'FreeIPA' | 'GenericLDAP' | 'LdifFile'
export type ConflictResolution = 'Skip' | 'Overwrite' | 'Merge' | 'Rename'
export type MigrationStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled'

export interface MigrationSource {
  id: string
  name: string
  type: MigrationSourceType
  host: string
  port: number
  useSsl: boolean
  bindDn: string
  bindPassword: string
  baseDn: string
  filter?: string
}

export interface MigrationMapping {
  sourceAttribute: string
  targetAttribute: string
  transformRule?: string
}

export interface MigrationOptions {
  migrateUsers: boolean
  migrateGroups: boolean
  migrateOUs: boolean
  migrateComputers: boolean
  preserveSidHistory: boolean
  preservePasswords: boolean
  migrateGroupMemberships: boolean
  dryRun: boolean
  onConflict: ConflictResolution
}

export interface MigrationPreview {
  users: number
  groups: number
  ous: number
  computers: number
  conflicts: number
  warnings: string[]
}

export interface MigrationPlan {
  id: string
  sourceId: string
  targetBaseDn: string
  attributeMappings: MigrationMapping[]
  options: MigrationOptions
  preview?: MigrationPreview
}

export interface MigrationResult {
  planId: string
  totalProcessed: number
  created: number
  updated: number
  skipped: number
  failed: number
  errors: MigrationError[]
  duration: string
  status: MigrationStatus
  progressPercent: number
  startedAt: string
  completedAt?: string
}

export interface MigrationError {
  dn: string
  objectClass: string
  message: string
}

export interface MigrationHistoryEntry {
  planId: string
  sourceName: string
  sourceType: MigrationSourceType
  startedAt: string
  completedAt?: string
  status: MigrationStatus
  totalProcessed: number
  created: number
  failed: number
}

export interface ConnectionTestResult {
  success: boolean
  message: string
  serverType?: string
}

export interface SchemaDiscoveryResult {
  objectClasses: string[]
  attributes: string[]
  estimatedObjectCount: number
}
