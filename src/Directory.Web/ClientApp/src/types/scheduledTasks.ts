export type ScheduledTaskType =
  | 'DnsScavenging'
  | 'BackupExport'
  | 'PasswordExpiryReport'
  | 'StaleAccountCleanup'
  | 'GroupMembershipReport'
  | 'RecycleBinPurge'
  | 'CertificateExpiryCheck'
  | 'Custom'

export interface ScheduledTask {
  id: string
  name: string
  description: string
  taskType: ScheduledTaskType
  cronExpression: string
  isEnabled: boolean
  parameters: Record<string, string>
  lastRunAt: string | null
  lastRunStatus: string
  lastRunMessage: string
  nextRunAt: string | null
  createdAt: string
}

export interface TaskExecutionRecord {
  id: string
  taskId: string
  startedAt: string
  completedAt: string | null
  status: string
  message: string
}
