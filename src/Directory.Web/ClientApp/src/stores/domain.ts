import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { DomainConfig, DashboardSummary, PasswordPolicy } from '../api/types'
import { fetchDomainConfig, fetchPasswordPolicy } from '../api/domain'
import { fetchSummary } from '../api/dashboard'

export const useDomainStore = defineStore('domain', () => {
  const config = ref<DomainConfig | null>(null)
  const summary = ref<DashboardSummary | null>(null)
  const passwordPolicy = ref<PasswordPolicy | null>(null)

  // Per-action loading state
  const loadingConfig = ref(false)
  const loadingSummary = ref(false)
  const loadingPasswordPolicy = ref(false)

  // Last error message (any action)
  const error = ref<string | null>(null)

  async function loadConfig() {
    loadingConfig.value = true
    error.value = null
    try {
      config.value = await fetchDomainConfig()
    } catch (e: unknown) {
      const message = e instanceof Error ? e.message : String(e)
      error.value = message
      console.error('[DomainStore] loadConfig failed:', message, e)
    } finally {
      loadingConfig.value = false
    }
  }

  async function loadSummary() {
    loadingSummary.value = true
    error.value = null
    try {
      summary.value = await fetchSummary()
    } catch (e: unknown) {
      const message = e instanceof Error ? e.message : String(e)
      error.value = message
      console.error('[DomainStore] loadSummary failed:', message, e)
    } finally {
      loadingSummary.value = false
    }
  }

  async function loadPasswordPolicy() {
    loadingPasswordPolicy.value = true
    error.value = null
    try {
      passwordPolicy.value = await fetchPasswordPolicy()
    } catch (e: unknown) {
      const message = e instanceof Error ? e.message : String(e)
      error.value = message
      console.error('[DomainStore] loadPasswordPolicy failed:', message, e)
    } finally {
      loadingPasswordPolicy.value = false
    }
  }

  return {
    config,
    summary,
    passwordPolicy,
    loadingConfig,
    loadingSummary,
    loadingPasswordPolicy,
    error,
    loadConfig,
    loadSummary,
    loadPasswordPolicy,
  }
})
