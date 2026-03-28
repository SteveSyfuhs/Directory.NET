export type HrSyncSourceType = 'GenericApi' | 'CsvUpload' | 'Workday' | 'BambooHR' | 'SapSuccessFactors'

export interface HrSyncConfiguration {
  id: string
  name: string
  sourceType: HrSyncSourceType
  endpointUrl: string
  apiKey: string
  attributeMapping: Record<string, string>
  targetOu: string
  autoCreateUsers: boolean
  autoDisableOnTermination: boolean
  autoMoveOnDepartmentChange: boolean
  cronSchedule: string
  isEnabled: boolean
  lastSyncAt: string | null
  lastSyncStatus: string | null
}

export interface HrSyncHistoryEntry {
  id: string
  configurationId: string
  startedAt: string
  completedAt: string | null
  status: string
  usersCreated: number
  usersUpdated: number
  usersDisabled: number
  usersMoved: number
  errors: number
  errorDetails: string[]
}

export interface HrSyncPreviewResult {
  actions: HrSyncPreviewAction[]
  totalRecords: number
  newUsers: number
  updates: number
  terminations: number
  moves: number
  noChange: number
}

export interface HrSyncPreviewAction {
  action: string
  employeeId: string
  displayName: string
  currentDn: string | null
  changes: Record<string, string>
}

export interface HrSyncStatus {
  configurationId: string
  isRunning: boolean
  lastSyncAt: string | null
  lastSyncStatus: string | null
  currentRun: HrSyncHistoryEntry | null
}
