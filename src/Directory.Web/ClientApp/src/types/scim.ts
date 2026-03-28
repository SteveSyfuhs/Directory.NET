export interface ScimIntegration {
  id: string
  name: string
  description: string | null
  bearerToken: string
  isEnabled: boolean
  createdAt: string
  lastSyncAt: string | null
  lastSyncStatus: string | null
  operationCount: number
  attributeMapping: Record<string, string>
}

export interface ScimOperationLog {
  id: string
  integrationId: string
  operation: string
  resourceType: string
  resourceId: string | null
  status: string
  detail: string | null
  timestamp: string
}
