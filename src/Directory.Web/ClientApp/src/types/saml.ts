export interface SamlAttributeMapping {
  samlAttributeName: string
  directoryAttribute: string
}

export interface SamlServiceProvider {
  id: string
  entityId: string
  name: string
  assertionConsumerServiceUrl: string
  singleLogoutServiceUrl: string | null
  certificate: string | null
  nameIdFormat: string
  attributeMappings: SamlAttributeMapping[]
  isEnabled: boolean
  createdAt: string
}
