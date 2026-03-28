import { postFormData, postForBlob } from './client'
import type { LdifExportRequest, LdifImportResult } from '../types/ldif'

export const exportLdif = (options: LdifExportRequest) =>
  postForBlob('/ldif/export', options)

export const importLdif = async (file: File) => {
  const formData = new FormData()
  formData.append('file', file)
  return postFormData<LdifImportResult>('/ldif/import', formData)
}

export const validateLdif = async (file: File) => {
  const formData = new FormData()
  formData.append('file', file)
  return postFormData<LdifImportResult>('/ldif/validate', formData)
}
