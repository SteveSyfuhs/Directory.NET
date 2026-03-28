import { get, put, del, post } from './client'
import type { FormattedAttribute, SchemaAttributeInfo } from '../types/attributes'

/**
 * Fetch attributes for a directory object by DN.
 * @param dn - Distinguished name of the object
 * @param filter - Optional filter: 'all' | 'set' | 'writable' | 'backlink'
 * @param showAll - If true, includes unset schema attributes with defaults
 */
export function fetchAttributes(dn: string, filter?: string, showAll?: boolean): Promise<FormattedAttribute[]> {
  const params = new URLSearchParams()
  params.set('dn', dn)
  if (filter) params.set('filter', filter)
  if (showAll) params.set('showAll', 'true')
  return get<FormattedAttribute[]>(`/objects/by-dn/attributes?${params.toString()}`)
}

/**
 * Fetch all schema attributes for a given object class (must-contain + may-contain, inherited).
 */
export function fetchSchemaAttributes(className: string): Promise<SchemaAttributeInfo[]> {
  return get<SchemaAttributeInfo[]>(`/schema/classes/${encodeURIComponent(className)}/attributes`)
}

/**
 * Set (replace) the values of an attribute.
 */
export function setAttribute(dn: string, attrName: string, values: any[]): Promise<void> {
  return put<void>(`/objects/by-dn/attributes/${encodeURIComponent(attrName)}?dn=${encodeURIComponent(dn)}`, { values })
}

/**
 * Clear (remove all values of) an attribute.
 */
export function clearAttribute(dn: string, attrName: string): Promise<void> {
  return del(`/objects/by-dn/attributes/${encodeURIComponent(attrName)}?dn=${encodeURIComponent(dn)}`)
}

/**
 * Add values to a multi-valued attribute.
 */
export function addAttributeValues(dn: string, attrName: string, values: any[]): Promise<void> {
  return post<void>(`/objects/by-dn/attributes/${encodeURIComponent(attrName)}/values?dn=${encodeURIComponent(dn)}`, { values })
}

/**
 * Remove specific values from a multi-valued attribute.
 */
export function removeAttributeValues(dn: string, attrName: string, values: any[]): Promise<void> {
  return del(`/objects/by-dn/attributes/${encodeURIComponent(attrName)}/values?dn=${encodeURIComponent(dn)}`, { values })
}
