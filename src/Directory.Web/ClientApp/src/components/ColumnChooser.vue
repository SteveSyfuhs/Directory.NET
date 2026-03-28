<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Checkbox from 'primevue/checkbox'
import Button from 'primevue/button'

const props = defineProps<{
  visible: boolean
  /** All available columns with labels */
  availableColumns: { field: string; label: string }[]
  /** Currently selected column field names */
  selectedColumns: string[]
  /** localStorage key for persistence */
  storageKey: string
}>()

const emit = defineEmits<{
  'update:visible': [val: boolean]
  'update:selectedColumns': [val: string[]]
}>()

const searchText = ref('')
const localSelected = ref<string[]>([])

watch(() => props.visible, (v) => {
  if (v) {
    localSelected.value = [...props.selectedColumns]
    searchText.value = ''
  }
})

const filteredColumns = computed(() => {
  if (!searchText.value) return props.availableColumns
  const q = searchText.value.toLowerCase()
  return props.availableColumns.filter(
    c => c.field.toLowerCase().includes(q) || c.label.toLowerCase().includes(q)
  )
})

function isSelected(field: string): boolean {
  return localSelected.value.includes(field)
}

function toggleColumn(field: string, checked: boolean) {
  if (checked) {
    if (!localSelected.value.includes(field)) {
      localSelected.value.push(field)
    }
  } else {
    localSelected.value = localSelected.value.filter(f => f !== field)
  }
}

function onApply() {
  emit('update:selectedColumns', [...localSelected.value])
  localStorage.setItem(props.storageKey, JSON.stringify(localSelected.value))
  emit('update:visible', false)
}

function onReset() {
  // Reset to first 5 or all if fewer
  const defaults = props.availableColumns.slice(0, 5).map(c => c.field)
  localSelected.value = defaults
}
</script>

<template>
  <Dialog :visible="visible" @update:visible="(v: boolean) => emit('update:visible', v)"
          header="Choose Columns" modal :style="{ width: '420px' }">
    <InputText v-model="searchText" placeholder="Search attributes..." style="width: 100%; margin-bottom: 0.75rem" />

    <div class="column-list">
      <div v-for="col in filteredColumns" :key="col.field" class="column-item">
        <Checkbox
          :modelValue="isSelected(col.field)"
          @update:modelValue="(val: boolean) => toggleColumn(col.field, val)"
          :binary="true"
          :inputId="`col-${col.field}`"
        />
        <label :for="`col-${col.field}`" class="column-label">
          <span class="column-field">{{ col.label }}</span>
          <span class="column-ldap">{{ col.field }}</span>
        </label>
      </div>
      <div v-if="filteredColumns.length === 0" class="column-empty">
        No matching attributes found
      </div>
    </div>

    <div class="selection-count">
      {{ localSelected.length }} column(s) selected
    </div>

    <template #footer>
      <Button label="Reset" severity="secondary" text @click="onReset" />
      <div style="flex: 1" />
      <Button label="Cancel" severity="secondary" text @click="emit('update:visible', false)" />
      <Button label="Apply" icon="pi pi-check" @click="onApply" />
    </template>
  </Dialog>
</template>

<style scoped>
.column-list {
  max-height: 350px;
  overflow-y: auto;
  border: 1px solid var(--app-neutral-border);
  border-radius: 0.375rem;
}

.column-item {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid var(--app-neutral-bg);
  transition: background 0.15s;
}

.column-item:last-child {
  border-bottom: none;
}

.column-item:hover {
  background: var(--p-surface-ground);
}

.column-label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  cursor: pointer;
  flex: 1;
}

.column-field {
  font-size: 0.8125rem;
  font-weight: 500;
  color: var(--p-text-color);
}

.column-ldap {
  font-family: monospace;
  font-size: 0.6875rem;
  color: var(--p-text-color);
}

.column-empty {
  padding: 1.5rem;
  text-align: center;
  color: var(--p-text-muted-color);
  font-style: italic;
}

.selection-count {
  margin-top: 0.5rem;
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  text-align: right;
}
</style>
