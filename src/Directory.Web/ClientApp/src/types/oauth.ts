export interface OAuthClient {
  clientId: string
  clientName: string
  clientSecret?: string // Only present on creation
  redirectUris: string[]
  allowedScopes: string[]
  allowedGrantTypes: string[]
  logoUri: string | null
  accessTokenLifetimeMinutes: number
  refreshTokenLifetimeDays: number
  requirePkce: boolean
  isEnabled: boolean
  createdAt: string
}

export interface OAuthClientCreateResponse extends OAuthClient {
  clientSecret: string
}

export interface RegenerateSecretResponse {
  clientSecret: string
}
