<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import ProgressSpinner from 'primevue/progressspinner'
import { searchObjects } from '../api/objects'
import { resolveObject } from '../api/objects'
import { cnFromDn } from '../utils/format'
import type { ObjectSummary } from '../api/types'

const props = defineProps<{
  modelValue: string
  label?: string
  objectFilter?: string
  disabled?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [val: string]
}>()

const dialogVisible = ref(false)
const searchQuery = ref('')
const searchResults = ref<ObjectSummary[]>([])
const searching = ref(false)
const resolvedName = ref('')

let searchTimeout: ReturnType<typeof setTimeout> | null = null

// Resolve display name when modelValue changes
watch(() => props.modelValue, async (dn) => {
  if (!dn) {
    resolvedName.value = ''
    return
  }
  try {
    const result = await resolveObject(dn)
    resolvedName.value = result.displayName || result.name || cnFromDn(dn)
  } catch {
    resolvedName.value = cnFromDn(dn)
  }
}, { immediate: true })

const displayValue = computed(() => {
  if (!props.modelValue) return ''
  return resolvedName.value || cnFromDn(props.modelValue)
})

function openBrowser() {
  searchQuery.value = ''
  searchResults.value = []
  dialogVisible.value = true
}

function onSearchInput() {
  if (searchTimeout) clearTimeout(searchTimeout)
  if (!searchQuery.value || searchQuery.value.length < 2) {
    searchResults.value = []
    return
  }
  searchTimeout = setTimeout(() => doSearch(), 300)
}

async function doSearch() {
  if (!searchQuery.value || searchQuery.value.length < 2) return
  searching.value = true
  try {
    const filter = props.objectFilter
      ? `(&${props.objectFilter}(|(cn=*${searchQuery.value}*)(displayName=*${searchQuery.value}*)))`
      : `(|(cn=*${searchQuery.value}*)(displayName=*${searchQuery.value}*))`
    const result = await searchObjects('', filter, 20)
    searchResults.value = result.items
  } catch {
    searchResults.value = []
  } finally {
    searching.value = false
  }
}

function selectObject(obj: ObjectSummary) {
  emit('update:modelValue', obj.dn)
  dialogVisible.value = false
}

function clearValue() {
  emit('update:modelValue', '')
}
</script>

<template>
  <div class="dn-picker">
    <div v-if="label" class="dn-picker-label">{{ label }}</div>
    <div class="dn-picker-input-row">
      <InputText :modelValue="displayValue" disabled class="dn-picker-input"
                 :placeholder="'Select ' + (label || 'object') + '...'" />
      <Button icon="pi pi-search" size="small" severity="secondary" @click="openBrowser"
              :disabled="disabled" v-tooltip="'Browse...'" />
      <Button icon="pi pi-times" size="small" severity="danger" text @click="clearValue"
              :disabled="disabled || !modelValue" v-tooltip="'Clear'" />
    </div>
    <div v-if="modelValue" class="dn-picker-dn">{{ modelValue }}</div>

    <Dialog v-model:visible="dialogVisible" header="Select Object" modal :style="{ width: '550px' }">
      <div style="margin-bottom: 1rem">
        <InputText v-model="searchQuery" placeholder="Search by name..." style="width: 100%"
                   @input="onSearchInput" autofocus />
      </div>
      <div v-if="searching" style="text-align: center; padding: 1rem">
        <ProgressSpinner style="width: 2rem; height: 2rem" />
      </div>
      <DataTable v-else :value="searchResults" stripedRows size="small" scrollable scrollHeight="300px"
                 selectionMode="single" @row-dblclick="(e: any) => selectObject(e.data)">
        <Column header="Name" style="min-width: 200px">
          <template #body="{ data }">
            <div style="display: flex; align-items: center; gap: 0.5rem">
              <i :class="data.objectClass === 'group' ? 'pi pi-users' : 'pi pi-user'"
                 style="color: var(--p-text-muted-color)"></i>
              <span>{{ data.name }}</span>
            </div>
          </template>
        </Column>
        <Column field="objectClass" header="Type" style="width: 100px" />
        <Column style="width: 60px">
          <template #body="{ data }">
            <Button icon="pi pi-check" size="small" severity="success" text @click="selectObject(data)" />
          </template>
        </Column>
        <template #empty>
          <div style="text-align: center; padding: 1rem; color: var(--p-text-muted-color)">
            {{ searchQuery.length >= 2 ? 'No results found' : 'Type at least 2 characters to search' }}
          </div>
        </template>
      </DataTable>
    </Dialog>
  </div>
</template>

<style scoped>
.dn-picker {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}
.dn-picker-label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
}
.dn-picker-input-row {
  display: flex;
  gap: 0.25rem;
  align-items: center;
}
.dn-picker-input {
  flex: 1;
}
.dn-picker-dn {
  font-family: monospace;
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  word-break: break-all;
}
</style>
