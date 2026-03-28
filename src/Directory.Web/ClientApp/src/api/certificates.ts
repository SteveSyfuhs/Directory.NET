import { get, post, put, del } from './client'

export interface EnrollmentPermission {
  principalDn: string
  principalName?: string
  canEnroll: boolean
  canAutoEnroll: boolean
  canManage: boolean
}

export interface CertificateTemplate {
  name: string
  displayName: string
  dn: string
  validityPeriodDays: number
  renewalPeriodDays: number
  keyUsage: number
  enhancedKeyUsage: string[]
  minimumKeySize: number
  autoEnroll: boolean
  requireApproval: boolean
  publishToDs: boolean
  enrollmentPermissions: EnrollmentPermission[]
  whenCreated: string
  whenChanged: string
}

export interface CreateTemplatePayload {
  name: string
  displayName?: string
  validityPeriodDays?: number
  renewalPeriodDays?: number
  keyUsage?: number
  enhancedKeyUsage?: string[]
  minimumKeySize?: number
  autoEnroll?: boolean
  requireApproval?: boolean
  publishToDs?: boolean
  enrollmentPermissions?: EnrollmentPermission[]
}

export interface UpdateTemplatePayload {
  displayName?: string
  validityPeriodDays?: number
  renewalPeriodDays?: number
  keyUsage?: number
  enhancedKeyUsage?: string[]
  minimumKeySize?: number
  autoEnroll?: boolean
  requireApproval?: boolean
  publishToDs?: boolean
  enrollmentPermissions?: EnrollmentPermission[]
}

export interface EnrolledCertificate {
  serialNumber: string
  subject: string
  templateName: string
  issuedDate: string
  expiryDate: string
  status: string
  issuer: string
  sanEntries: string[]
  thumbprint: string
  keyUsage: string
  enhancedKeyUsage: string[]
  requestedBy: string
  revokedAt?: string
  revocationReason?: string
}

export interface IssuedCertificateDetail {
  serialNumber: string
  subject: string
  issuer: string
  templateName: string
  notBefore: string
  notAfter: string
  thumbprint: string
  status: string
  revocationReason?: string
  revokedAt?: string
  requestedBy: string
  certificatePem: string
  privateKeyPem?: string
  subjectAlternativeNames: string[]
  keyUsage: string
  enhancedKeyUsage: string[]
}

export interface CaInfo {
  commonName: string
  subject: string
  serialNumber: string
  thumbprint: string
  notBefore: string
  notAfter: string
  publicKeyAlgorithm: string
  keySize: number
  isInitialized: boolean
  certificatePem: string
}

export interface CaInitializePayload {
  commonName?: string
  organization?: string
  country?: string
  validityYears?: number
  keySizeInBits?: number
  hashAlgorithm?: string
}

// Templates
export const listTemplates = () => get<CertificateTemplate[]>('/certificates/templates')

export const createTemplate = (payload: CreateTemplatePayload) =>
  post<CertificateTemplate>('/certificates/templates', payload)

export const updateTemplate = (name: string, payload: UpdateTemplatePayload) =>
  put<CertificateTemplate>(`/certificates/templates/${encodeURIComponent(name)}`, payload)

export const deleteTemplate = (name: string) =>
  del(`/certificates/templates/${encodeURIComponent(name)}`)

export const getTemplateSecurity = (name: string) =>
  get<EnrollmentPermission[]>(`/certificates/templates/${encodeURIComponent(name)}/security`)

export const updateTemplateSecurity = (name: string, permissions: EnrollmentPermission[]) =>
  put<EnrollmentPermission[]>(`/certificates/templates/${encodeURIComponent(name)}/security`, permissions)

// CA
export const getCaInfo = () => get<CaInfo>('/certificates/ca')

export const initializeCa = (payload: CaInitializePayload) =>
  post<CaInfo>('/certificates/ca/initialize', payload)

export const downloadCaCertificate = () =>
  get<string>('/certificates/ca/certificate', { responseType: 'text' } as any)

export const getCrlUrl = () => '/api/v1/certificates/ca/crl'

// Enrolled Certificates
export const listEnrolledCertificates = () => get<EnrolledCertificate[]>('/certificates/enrolled')

export const getEnrolledCertificate = (serialNumber: string) =>
  get<IssuedCertificateDetail>(`/certificates/enrolled/${encodeURIComponent(serialNumber)}`)

export const downloadCertificatePem = (serialNumber: string) =>
  get<string>(`/certificates/enrolled/${encodeURIComponent(serialNumber)}/download`, { responseType: 'text' } as any)

export const enrollCertificate = (
  templateName: string,
  subjectDn: string,
  sanEntries?: string[],
  csr?: string
) =>
  post<IssuedCertificateDetail>('/certificates/enroll', { templateName, subjectDn, sanEntries, csr })

export const revokeCertificate = (serialNumber: string, reason?: number) =>
  post<{ message: string }>(`/certificates/revoke/${encodeURIComponent(serialNumber)}`, { reason: reason ?? 0 })

export const renewCertificate = (serialNumber: string) =>
  post<IssuedCertificateDetail>(`/certificates/renew/${encodeURIComponent(serialNumber)}`)
