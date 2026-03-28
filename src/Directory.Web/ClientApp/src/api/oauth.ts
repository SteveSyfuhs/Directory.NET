import { get, post, put, del } from './client'
import type { OAuthClient, OAuthClientCreateResponse, RegenerateSecretResponse } from '../types/oauth'

export function fetchOAuthClients() {
  return get<OAuthClient[]>('/oauth/clients')
}

export function fetchOAuthClient(id: string) {
  return get<OAuthClient>(`/oauth/clients/${id}`)
}

export function createOAuthClient(client: Partial<OAuthClient>) {
  return post<OAuthClientCreateResponse>('/oauth/clients', client)
}

export function updateOAuthClient(id: string, client: Partial<OAuthClient>) {
  return put<OAuthClient>(`/oauth/clients/${id}`, client)
}

export function deleteOAuthClient(id: string) {
  return del(`/oauth/clients/${id}`)
}

export function regenerateClientSecret(id: string) {
  return post<RegenerateSecretResponse>(`/oauth/clients/${id}/secret`)
}
