export type RegionHealthStatus = 'Unknown' | 'Healthy' | 'Degraded' | 'Offline'

export interface RegionConfiguration {
  id: string
  name: string
  cosmosDbEndpoint: string
  preferredRegion: string
  dcEndpoints: string[]
  isPrimary: boolean
  isEnabled: boolean
  createdAt: string
  health: RegionHealthStatus
  lastHealthCheck?: string
}
