export interface RodcSettings {
  isRodc: boolean
  fullDcEndpoint: string
  lastReplicationTime: string | null
  passwordReplicationAllowed: string[]
  passwordReplicationDenied: string[]
}

export interface UpdateRodcSettingsRequest {
  isRodc?: boolean
  fullDcEndpoint?: string
  passwordReplicationAllowed?: string[]
  passwordReplicationDenied?: string[]
}

export interface PasswordCacheResponse {
  cachedPrincipals: string[]
  allowedPrincipals: string[]
  deniedPrincipals: string[]
}

export interface PasswordCachePrincipalRequest {
  principal: string
  list?: 'allowed' | 'denied'
}

export interface PasswordCacheUpdateResponse {
  allowedPrincipals: string[]
  deniedPrincipals: string[]
}

export interface RodcReplicationResult {
  success: boolean
  sourceDc: string
  replicationTime: string
  objectsReplicated: number
  message: string
}
