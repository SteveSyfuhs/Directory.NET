export interface RadiusSettings {
  enabled: boolean
  port: number
  accountingPort: number
  clients: RadiusClient[]
}

export interface RadiusClient {
  id: string
  name: string
  ipAddress: string
  sharedSecret: string
  description?: string
  isEnabled: boolean
}

export interface RadiusLogEntry {
  id: string
  timestamp: string
  clientIp: string
  clientName?: string
  username: string
  success: boolean
  reason?: string
}
