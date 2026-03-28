export interface GmsaAccount {
  id: string
  name: string
  distinguishedName: string
  dnsHostName: string
  servicePrincipalNames: string[]
  principalsAllowedToRetrievePassword: string[]
  managedPasswordIntervalInDays: number
  passwordLastSet: string
  nextPasswordChange: string
  isEnabled: boolean
  createdAt: string
}

export interface KdsRootKey {
  id: string
  effectiveTime: string
  createdAt: string
  kdfAlgorithm: string
}

export interface CreateGmsaRequest {
  name: string
  dnsHostName?: string
  servicePrincipalNames?: string[]
  principalsAllowedToRetrievePassword?: string[]
  managedPasswordIntervalInDays?: number
}
