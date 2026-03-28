export type MdmProvider = 'Intune' | 'JamfPro' | 'WorkspaceOne' | 'MobileIron' | 'Generic'

export interface MdmIntegration {
  id: string
  name: string
  provider: MdmProvider
  apiEndpoint: string
  apiKey: string
  syncDeviceCompliance: boolean
  isEnabled: boolean
  createdAt: string
  lastSyncAt?: string
}

export interface DeviceComplianceStatus {
  deviceId: string
  userDn: string
  deviceName: string
  platform: string
  isCompliant: boolean
  isManaged: boolean
  lastCheckIn: string
  complianceIssues: string[]
  integrationId: string
}

export interface MdmSyncResult {
  devicesSynced: number
  newDevices: number
  updatedDevices: number
  errors: number
  syncedAt: string
}
