import { get, post } from './client'

export interface SetupStatus {
  isDatabaseConfigured: boolean
  isProvisioned: boolean
  isProvisioning: boolean
  provisioningProgress: number
  provisioningPhase: string | null
  provisioningError: string | null
}

export interface ValidateConnectionResult { success: boolean; error?: string }
export interface ValidateDomainResult { valid: boolean; domainDn?: string; suggestedNetBios?: string; error?: string }
export interface ValidatePasswordResult { valid: boolean; reason?: string }
export interface ProvisionResult { started: boolean; error?: string }

export interface ProvisionRequest {
  domainName: string
  netBiosName: string
  adminPassword: string
  adminUsername: string
  siteName: string
  cosmosConnectionString?: string
  cosmosDatabaseName?: string
}

export const fetchSetupStatus = () => get<SetupStatus>('/setup/status')
export const fetchProgress = () => get<SetupStatus>('/setup/progress')
export const validateConnection = (connectionString: string, databaseName: string) =>
  post<ValidateConnectionResult>('/setup/validate-connection', { connectionString, databaseName })
export const configureDatabase = (connectionString: string, databaseName: string) =>
  post<ValidateConnectionResult>('/setup/configure-database', { connectionString, databaseName })
export const validateDomain = (domainName: string) =>
  post<ValidateDomainResult>('/setup/validate-domain', { domainName })
export const validatePassword = (password: string) =>
  post<ValidatePasswordResult>('/setup/validate-password', { password })
export interface ValidateSourceDcResult {
  success: boolean
  error?: string
  domainName?: string
  domainDn?: string
  forestName?: string
  dcHostname?: string
  dsaGuid?: string
  functionalLevel?: number
}

export interface ProvisionReplicaRequest {
  sourceDcUrl?: string
  domainName?: string
  adminUpn: string
  adminPassword: string
  siteName: string
  hostname?: string
  transport?: string
}

export interface DiscoverDomainResult {
  success: boolean
  error?: string
  dcHostname?: string
  dcIpAddress?: string
  dcRpcPort?: number
  domainDn?: string
  forestName?: string
  dsaGuid?: string
  functionalLevel?: number
}

export interface ReplicationProgress {
  phase: string
  namingContext: string
  objectsProcessed: number
  objectsTotal: number | null
  bytesTransferred: number
}

export const startProvisioning = (request: ProvisionRequest) =>
  post<ProvisionResult>('/setup/provision', request)

export const validateSourceDc = (sourceDcUrl: string) =>
  post<ValidateSourceDcResult>('/setup/validate-source-dc', { sourceDcUrl })

export const startReplicaProvisioning = (request: ProvisionReplicaRequest) =>
  post<ProvisionResult>('/setup/provision-replica', request)

export const discoverDomain = (domainName: string) =>
  post<DiscoverDomainResult>('/setup/discover-domain', { domainName })

export const fetchReplicationProgress = () =>
  get<SetupStatus & { replicationProgress?: ReplicationProgress }>('/setup/replication-progress')
