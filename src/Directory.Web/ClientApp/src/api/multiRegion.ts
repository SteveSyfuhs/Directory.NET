import { get, post, put, del } from './client'
import type { RegionConfiguration, RegionHealthStatus } from '../types/multiRegion'

export function fetchRegions() {
  return get<RegionConfiguration[]>('/regions')
}

export function createRegion(region: Partial<RegionConfiguration>) {
  return post<RegionConfiguration>('/regions', region)
}

export function updateRegion(id: string, region: Partial<RegionConfiguration>) {
  return put<RegionConfiguration>(`/regions/${id}`, region)
}

export function deleteRegion(id: string) {
  return del(`/regions/${id}`)
}

export function setPrimaryRegion(id: string) {
  return post<RegionConfiguration>(`/regions/${id}/set-primary`)
}

export function checkRegionHealth() {
  return post<Record<string, RegionHealthStatus>>('/regions/health-check')
}
