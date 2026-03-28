import { ref, type Ref } from 'vue'

export interface PaginatedResponse<T> {
  items: T[]
  continuationToken: string | null
  totalCount: number
  pageSize: number
  hasMore: boolean
}

export interface PaginationParams {
  pageSize?: number
  continuationToken?: string
}

export interface UsePaginationReturn<T> {
  items: Ref<T[]>
  continuationToken: Ref<string | null>
  totalCount: Ref<number>
  loading: Ref<boolean>
  hasMore: Ref<boolean>
  loadMore: () => Promise<void>
  reset: () => void
  reload: () => Promise<void>
}

/**
 * Vue composable for cursor-based pagination.
 *
 * Works with any API function that accepts pagination params
 * and returns a PaginatedResponse.
 *
 * @param fetchFn - API function that accepts PaginationParams and returns PaginatedResponse<T>
 * @param pageSize - number of items per page (default: 50)
 */
export function usePagination<T>(
  fetchFn: (params: PaginationParams) => Promise<PaginatedResponse<T>>,
  pageSize = 50
): UsePaginationReturn<T> {
  const items = ref<T[]>([]) as Ref<T[]>
  const continuationToken = ref<string | null>(null)
  const totalCount = ref(-1)
  const loading = ref(false)
  const hasMore = ref(true)

  async function loadMore() {
    if (loading.value || !hasMore.value) return

    loading.value = true
    try {
      const response = await fetchFn({
        pageSize,
        continuationToken: continuationToken.value ?? undefined,
      })

      items.value = [...items.value, ...response.items]
      continuationToken.value = response.continuationToken
      totalCount.value = response.totalCount
      hasMore.value = response.hasMore
    } finally {
      loading.value = false
    }
  }

  function reset() {
    items.value = []
    continuationToken.value = null
    totalCount.value = -1
    hasMore.value = true
  }

  async function reload() {
    reset()
    await loadMore()
  }

  return {
    items,
    continuationToken,
    totalCount,
    loading,
    hasMore,
    loadMore,
    reset,
    reload,
  }
}
