import { get, put, del } from './client'
import type { ConfigSection, ConfigFieldMeta, ConfigValues, ConfigNode } from '../types/configuration'

export function fetchSections(): Promise<ConfigSection[]> {
  return get<ConfigSection[]>('/configuration/sections')
}

export function fetchSectionSchema(section: string): Promise<ConfigFieldMeta[]> {
  return get<ConfigFieldMeta[]>(`/configuration/schema/${encodeURIComponent(section)}`)
}

export function fetchClusterConfig(section: string): Promise<ConfigValues> {
  return get<ConfigValues>(`/configuration/${encodeURIComponent(section)}/cluster`)
}

export function fetchNodeConfig(section: string, hostname: string): Promise<ConfigValues> {
  return get<ConfigValues>(`/configuration/${encodeURIComponent(section)}/node/${encodeURIComponent(hostname)}`)
}

export function updateClusterConfig(section: string, values: Record<string, any>, etag?: string): Promise<ConfigValues> {
  return put<ConfigValues>(`/configuration/${encodeURIComponent(section)}/cluster`, { values, etag })
}

export function updateNodeConfig(section: string, hostname: string, values: Record<string, any>, etag?: string): Promise<ConfigValues> {
  return put<ConfigValues>(`/configuration/${encodeURIComponent(section)}/node/${encodeURIComponent(hostname)}`, { values, etag })
}

export async function deleteNodeConfig(section: string, hostname: string): Promise<void> {
  return del(`/configuration/${encodeURIComponent(section)}/node/${encodeURIComponent(hostname)}`)
}

export function fetchNodes(): Promise<ConfigNode[]> {
  return get<ConfigNode[]>('/configuration/nodes')
}
