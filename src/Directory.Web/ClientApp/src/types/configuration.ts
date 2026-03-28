export interface ConfigSection {
  name: string
  hasClusterConfig: boolean
  hasNodeOverrides: string[]
  scopes: string[]
}

export interface ConfigFieldMeta {
  name: string
  type: string
  description: string
  defaultValue: any
  hotReloadable: boolean
  minValue?: any
  maxValue?: any
}

export interface ConfigValues {
  section: string
  scope: string
  version: number
  modifiedBy: string
  modifiedAt: string
  values: Record<string, any>
  etag?: string
}

export interface ConfigNode {
  hostname: string
  site: string
  configVersion: number
  clusterVersion: number
  lastSeen: string
}
