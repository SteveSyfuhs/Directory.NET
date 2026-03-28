export interface SysvolItem {
  name: string
  path: string
  isDirectory: boolean
  size?: number
  lastModified?: string
  version?: number
  contentType?: string
  modifiedBy?: string
}

export interface SysvolReplicationStatus {
  totalFiles: number
  totalSizeBytes: number
  lastReplicationTime: string
  pendingChanges: number
  replicationHealth: string
}

export interface SysvolConflict {
  path: string
  localVersion: number
  remoteVersion: number
  detectedAt: string
  localModifiedBy: string
  remoteModifiedBy: string
}

export interface SysvolConfig {
  sysvolSharePath: string
  netlogonSharePath: string
  dfsNamespace: string
  useDfsReplication: boolean
  smbServerHostname: string
}

export interface SysvolConfigValidation {
  isValid: boolean
  errors: string[]
  message: string
}
