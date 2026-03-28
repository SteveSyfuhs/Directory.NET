export type ProxyMode = 'PassThrough' | 'ReadOnly' | 'WriteThrough' | 'Cache'

export interface AttributeMapping {
  localName: string
  remoteName: string
  transformExpression?: string
}

export interface LdapProxyBackend {
  id: string
  name: string
  host: string
  port: number
  useSsl: boolean
  bindDn?: string
  bindPassword?: string
  baseDn: string
  attributeMappings: AttributeMapping[]
  isEnabled: boolean
  priority: number
  timeoutMs: number
}

export interface ProxyRoute {
  id: string
  baseDn: string
  backendId: string
  mode: ProxyMode
}

export interface BackendTestResult {
  success: boolean
  message: string
  latencyMs: number
}
