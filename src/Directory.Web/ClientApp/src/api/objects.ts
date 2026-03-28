import { get, put, del } from './client'
import type { ObjectDetail, SearchResult, ResolveResult } from './types'

export function searchObjects(baseDn: string, filter?: string, pageSize?: number) {
  const params = new URLSearchParams({ baseDn })
  if (filter) params.set('filter', filter)
  if (pageSize) params.set('pageSize', String(pageSize))
  return get<SearchResult>(`/objects/search?${params.toString()}`)
}

export const getObject = (guid: string) => get<ObjectDetail>(`/objects/${guid}`)

export function updateObject(guid: string, body: Record<string, unknown>) {
  return put<ObjectDetail>(`/objects/${guid}`, body)
}

export const deleteObject = (guid: string) => del(`/objects/${guid}`)

export function resolveObject(dn: string) {
  return get<ResolveResult>(`/objects/resolve?dn=${encodeURIComponent(dn)}`)
}
