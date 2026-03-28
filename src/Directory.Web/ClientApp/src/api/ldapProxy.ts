import { get, post, put, del } from './client'
import type { LdapProxyBackend, ProxyRoute, BackendTestResult } from '../types/ldapProxy'

export function fetchProxyBackends() {
  return get<LdapProxyBackend[]>('/ldap-proxy/backends')
}

export function createProxyBackend(backend: Partial<LdapProxyBackend>) {
  return post<LdapProxyBackend>('/ldap-proxy/backends', backend)
}

export function updateProxyBackend(id: string, backend: Partial<LdapProxyBackend>) {
  return put<LdapProxyBackend>(`/ldap-proxy/backends/${id}`, backend)
}

export function deleteProxyBackend(id: string) {
  return del(`/ldap-proxy/backends/${id}`)
}

export function testProxyBackend(id: string) {
  return post<BackendTestResult>(`/ldap-proxy/backends/${id}/test`)
}

export function fetchProxyRoutes() {
  return get<ProxyRoute[]>('/ldap-proxy/routes')
}

export function createProxyRoute(route: Partial<ProxyRoute>) {
  return post<ProxyRoute>('/ldap-proxy/routes', route)
}

export function updateProxyRoute(id: string, route: Partial<ProxyRoute>) {
  return put<ProxyRoute>(`/ldap-proxy/routes/${id}`, route)
}

export function deleteProxyRoute(id: string) {
  return del(`/ldap-proxy/routes/${id}`)
}
