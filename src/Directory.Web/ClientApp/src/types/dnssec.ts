export interface DnssecSettings {
  zoneName: string
  dnssecEnabled: boolean
  signatureValidityDays: number
  keyRolloverIntervalDays: number
  lastSignedAt: string | null
}

export interface DnssecKey {
  id: string
  keyType: string
  algorithm: number
  algorithmName: string
  keyTag: number
  createdAt: string
  expiresAt: string | null
  isActive: boolean
  publicKeyBase64: string
}

export interface DnssecDsRecord {
  zoneName: string
  keyTag: number
  algorithm: number
  digestType: number
  digest: string
  dsRecord: string
}

export interface UpdateDnssecSettingsRequest {
  dnssecEnabled: boolean
  signatureValidityDays?: number
  keyRolloverIntervalDays?: number
}

export interface GenerateKeyRequest {
  keyType: 'KSK' | 'ZSK'
  algorithm?: number
}

export interface SignZoneResult {
  zone: string
  signedRRsets: number
  signedAt: string
}
