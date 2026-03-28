export type MappingType =
  | 'ExplicitMapping'
  | 'SubjectMapping'
  | 'IssuerAndSubjectMapping'
  | 'UpnMapping'
  | 'SubjectAlternativeNameMapping'

export interface SmartCardMapping {
  id: string
  userDn: string
  certificateSubject: string
  certificateIssuer: string
  certificateThumbprint: string
  upn?: string
  type: MappingType
  mappedAt: string
  isEnabled: boolean
}

export interface SmartCardSettings {
  enabled: boolean
  defaultMappingType: MappingType
  requireSmartCardLogon: boolean
  validateCertificateChain: boolean
  checkRevocation: boolean
  trustedCAs: string[]
  modifiedAt: string
}

export interface SmartCardAuthResult {
  success: boolean
  userDn?: string
  error?: string
  mappingType?: string
}
