import { get, del, postFormData } from './client'

export interface TlsCertificateInfo {
  configured: boolean
  subject?: string
  issuer?: string
  notBefore?: string
  notAfter?: string
  thumbprint?: string
  serialNumber?: string
  keyAlgorithm?: string
  keySize?: number
  uploadedAt?: string
}

export interface TlsStatus {
  enabled: boolean
  port: number
  certificateConfigured: boolean
  certificateValid: boolean
  daysUntilExpiry: number | null
  subject: string | null
  thumbprint: string | null
}

export interface TlsUploadResult {
  message: string
  subject: string
  issuer: string
  notBefore: string
  notAfter: string
  thumbprint: string
}

export const getTlsCertificateInfo = () =>
  get<TlsCertificateInfo>('/certificates/tls')

export const getTlsStatus = () =>
  get<TlsStatus>('/certificates/tls/status')

export async function uploadTlsCertificate(file: File, password?: string): Promise<TlsUploadResult> {
  const formData = new FormData()
  formData.append('certificate', file)
  if (password) {
    formData.append('password', password)
  }
  return postFormData<TlsUploadResult>('/certificates/tls', formData)
}

export const removeTlsCertificate = () =>
  del('/certificates/tls')
