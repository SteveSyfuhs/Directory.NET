export interface SshPublicKey {
  id: string
  userDn: string
  keyType: string
  publicKeyData: string
  comment?: string
  fingerprint?: string
  addedAt: string
  lastUsedAt?: string
  isEnabled: boolean
}
