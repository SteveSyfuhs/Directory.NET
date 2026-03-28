import { get, post, put, del } from './client'
import type { SamlServiceProvider } from '../types/saml'

export function fetchSamlProviders() {
  return get<SamlServiceProvider[]>('/saml/service-providers')
}

export function fetchSamlProvider(id: string) {
  return get<SamlServiceProvider>(`/saml/service-providers/${id}`)
}

export function createSamlProvider(sp: Partial<SamlServiceProvider>) {
  return post<SamlServiceProvider>('/saml/service-providers', sp)
}

export function updateSamlProvider(id: string, sp: Partial<SamlServiceProvider>) {
  return put<SamlServiceProvider>(`/saml/service-providers/${id}`, sp)
}

export function deleteSamlProvider(id: string) {
  return del(`/saml/service-providers/${id}`)
}

export function getSamlMetadataUrl(): string {
  return '/api/v1/saml/metadata'
}
