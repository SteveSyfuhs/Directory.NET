export interface Fido2CredentialSummary {
  id: string
  credentialId: string
  deviceName: string
  attestationType: string
  transports: string[]
  registeredAt: string
  lastUsedAt: string | null
  signCount: number
  isEnabled: boolean
}

export interface PublicKeyCredentialCreationOptions {
  rp: { id: string; name: string }
  user: { id: string; name: string; displayName: string }
  challenge: string
  pubKeyCredParams: { type: string; alg: number }[]
  timeout: number
  attestation: string
  excludeCredentials: PublicKeyCredentialDescriptor[]
  authenticatorSelection: {
    authenticatorAttachment: string
    requireResidentKey: boolean
    residentKey: string
    userVerification: string
  }
}

export interface PublicKeyCredentialRequestOptions {
  challenge: string
  timeout: number
  rpId: string
  allowCredentials: PublicKeyCredentialDescriptor[]
  userVerification: string
}

export interface PublicKeyCredentialDescriptor {
  type: string
  id: string
  transports?: string[]
}

export interface Fido2RegistrationResult {
  success: boolean
  credentialId?: string
  error?: string
}

export interface Fido2AuthenticationResult {
  success: boolean
  userDn?: string
  error?: string
}
