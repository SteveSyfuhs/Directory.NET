import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { fetchSetupStatus, fetchProgress, type SetupStatus } from '../api/setup'

export const useSetupStore = defineStore('setup', () => {
  const status = ref<SetupStatus | null>(null)
  const loading = ref(true)
  const checked = ref(false)

  async function checkStatus() {
    loading.value = true
    try {
      status.value = await fetchSetupStatus()
    } catch {
      // If API fails, assume nothing is configured
      status.value = {
        isDatabaseConfigured: false,
        isProvisioned: false,
        isProvisioning: false,
        provisioningProgress: 0,
        provisioningPhase: null,
        provisioningError: null,
      }
    } finally {
      loading.value = false
      checked.value = true
    }
  }

  async function pollProgress() {
    try {
      status.value = await fetchProgress()
    } catch {
      // Ignore polling errors
    }
  }

  const isDatabaseConfigured = computed(() => status.value?.isDatabaseConfigured ?? false)
  const isProvisioned = computed(() => status.value?.isProvisioned ?? false)
  const isProvisioning = computed(() => status.value?.isProvisioning ?? false)

  function markDatabaseConfigured() {
    if (status.value) {
      status.value = { ...status.value, isDatabaseConfigured: true }
    }
  }

  return { status, loading, checked, checkStatus, pollProgress, isDatabaseConfigured, isProvisioned, isProvisioning, markDatabaseConfigured }
})
