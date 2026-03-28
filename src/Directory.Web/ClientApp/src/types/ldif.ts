export interface LdifExportRequest {
  baseDn?: string
  filter?: string
  scope?: 'base' | 'oneLevel' | 'subtree'
  attributes?: string[]
  includeOperationalAttributes: boolean
}

export interface LdifImportResult {
  totalRecords: number
  imported: number
  skipped: number
  failed: number
  errors: string[]
}
