import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

const STORAGE_KEY = 'app-preferences'

interface StoredPreferences {
  sidebarCollapsed: boolean
  tablePageSize: number
  dateFormat: string
}

function loadFromStorage(): StoredPreferences {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) {
      const parsed = JSON.parse(raw) as Partial<StoredPreferences>
      return {
        sidebarCollapsed: parsed.sidebarCollapsed ?? false,
        tablePageSize: parsed.tablePageSize ?? 25,
        dateFormat: parsed.dateFormat ?? 'relative',
      }
    }
  } catch {
    // ignore parse errors
  }
  return {
    sidebarCollapsed: false,
    tablePageSize: 25,
    dateFormat: 'relative',
  }
}

export const usePreferencesStore = defineStore('preferences', () => {
  const stored = loadFromStorage()

  const sidebarCollapsed = ref<boolean>(stored.sidebarCollapsed)
  const tablePageSize = ref<number>(stored.tablePageSize)
  const dateFormat = ref<string>(stored.dateFormat)

  function persist() {
    const prefs: StoredPreferences = {
      sidebarCollapsed: sidebarCollapsed.value,
      tablePageSize: tablePageSize.value,
      dateFormat: dateFormat.value,
    }
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs))
    } catch {
      // ignore storage errors (e.g. private browsing quota exceeded)
    }
  }

  // Persist whenever any preference changes
  watch([sidebarCollapsed, tablePageSize, dateFormat], persist)

  function setSidebarCollapsed(value: boolean) {
    sidebarCollapsed.value = value
  }

  function setTablePageSize(value: number) {
    tablePageSize.value = value
  }

  function setDateFormat(value: string) {
    dateFormat.value = value
  }

  return {
    sidebarCollapsed,
    tablePageSize,
    dateFormat,
    setSidebarCollapsed,
    setTablePageSize,
    setDateFormat,
  }
})
