<script setup lang="ts">
import { ref, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import SelectButton from 'primevue/selectbutton'
import Dialog from 'primevue/dialog'
import ProgressSpinner from 'primevue/progressspinner'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import LdapFilterBuilder from '../components/LdapFilterBuilder.vue'
import PropertySheet from '../components/PropertySheet.vue'
import BulkOperationDialog from '../components/BulkOperationDialog.vue'
import { advancedSearch, getSavedSearches, saveSearch, deleteSavedSearch } from '../api/search'
import type { AdvancedSearchResultItem, SavedSearch } from '../api/types'
import { objectClassIcon } from '../utils/format'

const toast = useToast()

// Search parameters
const baseDn = ref('')
const scope = ref('subtree')
const filterMode = ref<'visual' | 'raw'>('visual')
const visualFilter = ref('')
const rawFilter = ref('(objectClass=*)')
const attributesList = ref('')
const maxResults = ref(1000)

// Results
const loading = ref(false)
const results = ref<AdvancedSearchResultItem[]>([])
const totalCount = ref(0)
const hasSearched = ref(false)

// Selection
const selectedRows = ref<AdvancedSearchResultItem[]>([])

// Property sheet
const propertySheetVisible = ref(false)
const propertySheetGuid = ref('')

// Bulk operations
const bulkDialogVisible = ref(false)

// Saved searches
const savedSearches = ref<SavedSearch[]>([])
const saveDialogVisible = ref(false)
const saveSearchName = ref('')
const savedSearchDropdown = ref<string | null>(null)

// Column config
const columnDialogVisible = ref(false)
const visibleColumns = ref<string[]>(['cn', 'objectClass', 'sAMAccountName', 'description'])
const columnInput = ref('')

const scopeOptions = [
  { label: 'Base Object', value: 'base' },
  { label: 'Single Level', value: 'onelevel' },
  { label: 'Whole Subtree', value: 'subtree' },
]

const filterModeOptions = [
  { label: 'Visual Builder', value: 'visual' },
  { label: 'Raw LDAP Filter', value: 'raw' },
]

const currentFilter = computed(() => {
  return filterMode.value === 'visual' ? visualFilter.value : rawFilter.value
})

const selectedDns = computed(() => selectedRows.value.map(r => r.dn))

// Dynamically build column set from results
const availableColumns = computed(() => {
  const cols = new Set<string>()
  for (const item of results.value) {
    for (const key of Object.keys(item.attributes)) {
      cols.add(key)
    }
  }
  return Array.from(cols).sort()
})

async function executeSearch() {
  if (!currentFilter.value) {
    toast.add({ severity: 'warn', summary: 'Warning', detail: 'Please provide a filter', life: 3000 })
    return
  }

  loading.value = true
  hasSearched.value = true
  selectedRows.value = []

  try {
    const attrs = attributesList.value
      ? attributesList.value.split(',').map(a => a.trim()).filter(Boolean)
      : undefined

    const result = await advancedSearch({
      baseDn: baseDn.value || undefined,
      scope: scope.value,
      filter: currentFilter.value,
      attributes: attrs,
      maxResults: maxResults.value,
    })

    results.value = result.items
    totalCount.value = result.totalCount

    // Auto-detect columns from first result
    if (result.items.length > 0 && visibleColumns.value.length === 0) {
      visibleColumns.value = Object.keys(result.items[0].attributes).slice(0, 6)
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Search Error', detail: e.message, life: 5000 })
    results.value = []
    totalCount.value = 0
  } finally {
    loading.value = false
  }
}

function onRowDoubleClick(event: { data: AdvancedSearchResultItem }) {
  if (event.data.objectGuid) {
    propertySheetGuid.value = event.data.objectGuid
    propertySheetVisible.value = true
  }
}

function openBulkDialog() {
  if (selectedRows.value.length === 0) return
  bulkDialogVisible.value = true
}

function onBulkCompleted() {
  executeSearch()
}

function getAttrValue(item: AdvancedSearchResultItem, col: string): string {
  const vals = item.attributes[col]
  if (!vals || vals.length === 0) return ''
  return vals.join('; ')
}

function exportCsv() {
  if (results.value.length === 0) return

  const cols = visibleColumns.value.length > 0 ? visibleColumns.value : ['distinguishedName', 'cn', 'objectClass']
  const header = cols.join(',')
  const rows = results.value.map(item => {
    return cols.map(col => {
      const val = getAttrValue(item, col) || ''
      return `"${val.replace(/"/g, '""')}"`
    }).join(',')
  })

  const csv = [header, ...rows].join('\n')
  const blob = new Blob([csv], { type: 'text/csv' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'search_results.csv'
  a.click()
  URL.revokeObjectURL(url)
}

// Saved searches
async function loadSavedSearches() {
  try {
    savedSearches.value = await getSavedSearches()
  } catch { /* ignore */ }
}

async function onSaveSearch() {
  if (!saveSearchName.value) return
  try {
    await saveSearch({
      name: saveSearchName.value,
      baseDn: baseDn.value,
      scope: scope.value,
      filter: currentFilter.value,
      attributes: attributesList.value ? attributesList.value.split(',').map(a => a.trim()).filter(Boolean) : undefined,
    })
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Search saved successfully', life: 3000 })
    saveDialogVisible.value = false
    saveSearchName.value = ''
    await loadSavedSearches()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function onLoadSavedSearch(search: SavedSearch) {
  baseDn.value = search.baseDn || ''
  scope.value = search.scope || 'subtree'
  rawFilter.value = search.filter || ''
  filterMode.value = 'raw'
  if (search.attributes) {
    attributesList.value = search.attributes.join(', ')
  }
  savedSearchDropdown.value = null
}

async function onDeleteSavedSearch(id: string) {
  try {
    await deleteSavedSearch(id)
    await loadSavedSearches()
  } catch { /* ignore */ }
}

function openColumnConfig() {
  columnInput.value = visibleColumns.value.join(', ')
  columnDialogVisible.value = true
}

function applyColumns() {
  visibleColumns.value = columnInput.value.split(',').map(s => s.trim()).filter(Boolean)
  columnDialogVisible.value = false
}

loadSavedSearches()
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Advanced Search</h1>
      <p>Search the directory using LDAP filters</p>
    </div>

    <div class="card" style="margin-bottom: 1rem">
      <div style="display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 1rem; margin-bottom: 1rem">
        <div>
          <label class="field-label">Base DN</label>
          <InputText v-model="baseDn" size="small" style="width: 100%" placeholder="Leave empty for domain root" />
        </div>
        <div>
          <label class="field-label">Scope</label>
          <Select v-model="scope" :options="scopeOptions" optionLabel="label" optionValue="value"
                  size="small" style="width: 100%" />
        </div>
        <div>
          <label class="field-label">Max Results</label>
          <InputNumber v-model="maxResults" :min="1" :max="10000" size="small" style="width: 100%" />
        </div>
      </div>

      <div style="margin-bottom: 1rem">
        <div style="display: flex; align-items: center; gap: 1rem; margin-bottom: 0.75rem">
          <label class="field-label" style="margin: 0">Filter</label>
          <SelectButton v-model="filterMode" :options="filterModeOptions" optionLabel="label" optionValue="value"
                        :allowEmpty="false" size="small" />
        </div>

        <div v-if="filterMode === 'visual'">
          <LdapFilterBuilder v-model="visualFilter" />
        </div>
        <div v-else>
          <Textarea v-model="rawFilter" rows="3" style="width: 100%; font-family: monospace; font-size: 0.8125rem"
                    placeholder="(&(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))" />
        </div>
      </div>

      <div style="display: flex; gap: 1rem; margin-bottom: 1rem">
        <div style="flex: 1">
          <label class="field-label">Attributes to Return</label>
          <InputText v-model="attributesList" size="small" style="width: 100%"
                     placeholder="* (all) or comma-separated: cn, mail, department" />
        </div>
      </div>

      <div class="toolbar" style="margin-bottom: 0">
        <Button label="Search" icon="pi pi-search" @click="executeSearch" :loading="loading" />
        <Button label="Save Search" icon="pi pi-save" severity="secondary" outlined size="small"
                @click="saveDialogVisible = true" :disabled="!currentFilter" />

        <Select v-if="savedSearches.length > 0"
                v-model="savedSearchDropdown" :options="savedSearches" optionLabel="name"
                placeholder="Load saved search..." size="small" style="width: 220px"
                @change="(e: any) => { if (e.value) onLoadSavedSearch(e.value) }" />

        <div class="toolbar-spacer" />

        <Button icon="pi pi-cog" severity="secondary" text size="small" v-tooltip="'Configure columns'"
                @click="openColumnConfig" :disabled="results.length === 0" />
        <Button label="Export CSV" icon="pi pi-download" severity="secondary" outlined size="small"
                @click="exportCsv" :disabled="results.length === 0" />
      </div>
    </div>

    <!-- Results -->
    <div v-if="loading" style="text-align: center; padding: 3rem">
      <ProgressSpinner />
    </div>

    <div v-else-if="hasSearched && results.length === 0" class="card"
         style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
      No results found matching the filter.
    </div>

    <template v-else-if="results.length > 0">
      <div class="toolbar">
        <Tag :value="`${results.length} result(s) of ~${totalCount}`" severity="info" />
        <div v-if="selectedRows.length > 0" style="display: flex; align-items: center; gap: 0.5rem">
          <Tag :value="`${selectedRows.length} selected`" severity="warn" />
          <Button label="Bulk Actions" icon="pi pi-bolt" size="small" severity="warn"
                  @click="openBulkDialog" />
        </div>
      </div>

      <div class="card" style="padding: 0">
        <DataTable
          :value="results"
          v-model:selection="selectedRows"
          selectionMode="multiple"
          dataKey="dn"
          stripedRows
          size="small"
          scrollable
          scrollHeight="calc(100vh - 500px)"
          :paginator="results.length > 50"
          :rows="50"
          :rowsPerPageOptions="[25, 50, 100, 200]"
          @row-dblclick="onRowDoubleClick"
        >
          <Column selectionMode="multiple" headerStyle="width: 3rem" />
          <Column header="Name" sortable sortField="name" style="min-width: 200px">
            <template #body="{ data }">
              <div style="display: flex; align-items: center; gap: 0.5rem">
                <i :class="objectClassIcon(data.objectClass)" style="color: var(--p-text-muted-color); font-size: 0.875rem"></i>
                <span>{{ data.name || data.dn }}</span>
              </div>
            </template>
          </Column>
          <Column v-for="col in visibleColumns" :key="col" :header="col" sortable :sortField="`attributes.${col}`"
                  style="min-width: 150px">
            <template #body="{ data }">
              <span style="font-size: 0.8125rem; color: var(--p-text-muted-color)">{{ getAttrValue(data, col) }}</span>
            </template>
          </Column>
          <template #empty>
            <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
              No results found
            </div>
          </template>
        </DataTable>
      </div>
    </template>

    <!-- Save Search Dialog -->
    <Dialog v-model:visible="saveDialogVisible" header="Save Search" modal :style="{ width: '400px' }">
      <div style="margin-bottom: 1rem">
        <label class="field-label">Search Name</label>
        <InputText v-model="saveSearchName" size="small" style="width: 100%" placeholder="My search..." />
      </div>
      <div style="font-size: 0.8125rem; color: var(--p-text-muted-color)">
        <div>Filter: <code>{{ currentFilter }}</code></div>
        <div>Scope: {{ scope }}</div>
      </div>
      <template #footer>
        <div style="display: flex; justify-content: flex-end; gap: 0.5rem">
          <Button label="Cancel" severity="secondary" text @click="saveDialogVisible = false" />
          <Button label="Save" icon="pi pi-save" @click="onSaveSearch" :disabled="!saveSearchName" />
        </div>
      </template>
    </Dialog>

    <!-- Column Config Dialog -->
    <Dialog v-model:visible="columnDialogVisible" header="Configure Columns" modal :style="{ width: '500px' }">
      <p style="font-size: 0.875rem; color: var(--p-text-muted-color); margin-bottom: 0.75rem">
        Enter comma-separated attribute names to show as columns.
      </p>
      <InputText v-model="columnInput" size="small" style="width: 100%" />
      <div v-if="availableColumns.length > 0" style="margin-top: 0.75rem">
        <p style="font-size: 0.8125rem; font-weight: 600; margin-bottom: 0.375rem">Available columns:</p>
        <div style="display: flex; flex-wrap: wrap; gap: 0.25rem">
          <Tag v-for="col in availableColumns" :key="col" :value="col"
               style="cursor: pointer; font-size: 0.75rem"
               @click="columnInput = columnInput ? columnInput + ', ' + col : col" />
        </div>
      </div>
      <template #footer>
        <div style="display: flex; justify-content: flex-end; gap: 0.5rem">
          <Button label="Cancel" severity="secondary" text @click="columnDialogVisible = false" />
          <Button label="Apply" @click="applyColumns" />
        </div>
      </template>
    </Dialog>

    <!-- Property Sheet -->
    <PropertySheet
      v-if="propertySheetVisible"
      :objectGuid="propertySheetGuid"
      :visible="propertySheetVisible"
      @update:visible="propertySheetVisible = $event"
    />

    <!-- Bulk Operations -->
    <BulkOperationDialog
      :visible="bulkDialogVisible"
      :selectedDns="selectedDns"
      @update:visible="bulkDialogVisible = $event"
      @completed="onBulkCompleted"
    />
  </div>
</template>

<style scoped>
.field-label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
  display: block;
  margin-bottom: 0.375rem;
}
</style>
