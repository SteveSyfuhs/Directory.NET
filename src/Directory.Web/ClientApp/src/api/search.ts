import { get, post, del } from './client'
import type { AdvancedSearchResult, SavedSearch } from './types'

export interface AdvancedSearchRequest {
  baseDn?: string
  scope?: string
  filter: string
  attributes?: string[]
  maxResults?: number
}

export function advancedSearch(request: AdvancedSearchRequest) {
  return post<AdvancedSearchResult>('/search', request)
}

export function exportSearchCsv(request: AdvancedSearchRequest): Promise<string> {
  return post<string>('/search/export', request)
}

export function getSavedSearches() {
  return get<SavedSearch[]>('/search/saved')
}

export function saveSearch(search: Omit<SavedSearch, 'id' | 'createdAt'>) {
  return post<SavedSearch>('/search/saved', search)
}

export function deleteSavedSearch(id: string) {
  return del(`/search/saved/${id}`)
}
