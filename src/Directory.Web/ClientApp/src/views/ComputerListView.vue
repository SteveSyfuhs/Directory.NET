<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import { searchObjects, deleteObject } from '../api/objects'
import { enableUser as enableAccount, disableUser as disableAccount } from '../api/users'
import type { ObjectSummary } from '../api/types'
import { relativeTime } from '../utils/format'
import PropertySheet from '../components/PropertySheet.vue'
import CreateComputerDialog from '../components/CreateComputerDialog.vue'
import ColumnChooser from '../components/ColumnChooser.vue'
import { exportToCsv } from '../composables/useExport'

const toast = useToast()
const confirm = useConfirm()

const createComputerVisible = ref(false)

// Column chooser
const columnChooserVisible = ref(false)
const COMP_COLUMN_KEY = 'computerlist-columns'
const allAvailableCompColumns = [
  { field: 'name', label: 'Name' },
  { field: 'samAccountName', label: 'SAM Account' },
  { field: 'description', label: 'Description' },
  { field: 'dNSHostName', label: 'DNS Host Name' },
  { field: 'operatingSystem', label: 'Operating System' },
  { field: 'operatingSystemVersion', label: 'OS Version' },
  { field: 'enabled', label: 'Status' },
  { field: 'whenChanged', label: 'Modified' },
  { field: 'whenCreated', label: 'Created' },
  { field: 'lastLogon', label: 'Last Logon' },
  { field: 'dn', label: 'Distinguished Name' },
]
const defaultCompColumns = ['name', 'samAccountName', 'description', 'enabled', 'whenChanged']
function loadCompColumns(): string[] {
  try { const s = localStorage.getItem(COMP_COLUMN_KEY); if (s) return JSON.parse(s) } catch {}
  return defaultCompColumns
}
const selectedCompColumns = ref<string[]>(loadCompColumns())
const computers = ref<ObjectSummary[]>([])
const loading = ref(true)
const filterText = ref('')
const selectedComputer = ref<ObjectSummary | null>(null)
const propertySheetVisible = ref(false)
const propertySheetGuid = ref('')

onMounted(() => loadComputers())

async function loadComputers() {
  loading.value = true
  try {
    const result = await searchObjects('', '(objectClass=computer)', 500)
    computers.value = result.items
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

const filteredComputers = computed(() => {
  if (!filterText.value) return computers.value
  const q = filterText.value.toLowerCase()
  return computers.value.filter(
    (c) =>
      (c.name?.toLowerCase().includes(q)) ||
      (c.samAccountName?.toLowerCase().includes(q)) ||
      (c.description?.toLowerCase().includes(q))
  )
})

async function onEnable() {
  if (!selectedComputer.value?.objectGuid) return
  try {
    await enableAccount(selectedComputer.value.objectGuid)
    toast.add({ severity: 'success', summary: 'Enabled', detail: 'Computer account enabled', life: 3000 })
    await loadComputers()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onDisable() {
  if (!selectedComputer.value?.objectGuid) return
  try {
    await disableAccount(selectedComputer.value.objectGuid)
    toast.add({ severity: 'success', summary: 'Disabled', detail: 'Computer account disabled', life: 3000 })
    await loadComputers()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onDelete() {
  if (!selectedComputer.value?.objectGuid) return
  const target = selectedComputer.value
  confirm.require({
    message: `Are you sure you want to delete the computer account "${target.name}"? This action cannot be undone.`,
    header: 'Delete Computer',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete',
    acceptProps: { severity: 'danger' },
    accept: async () => {
      try {
        await deleteObject(target.objectGuid!)
        toast.add({ severity: 'success', summary: 'Deleted', detail: 'Computer deleted', life: 3000 })
        selectedComputer.value = null
        await loadComputers()
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      }
    },
  })
}

function onExport() {
  const columns = selectedCompColumns.value.map(f => ({
    field: f,
    header: allAvailableCompColumns.find(c => c.field === f)?.label || f,
  }))
  exportToCsv(filteredComputers.value as Record<string, any>[], columns, 'computers')
}

function onRowDoubleClick(event: { data: ObjectSummary }) {
  if (event.data.objectGuid) {
    propertySheetGuid.value = event.data.objectGuid
    propertySheetVisible.value = true
  }
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Computers</h1>
      <p>Manage computer accounts</p>
    </div>

    <div class="toolbar">
      <Button label="Create Computer" icon="pi pi-plus" size="small"
              @click="createComputerVisible = true"
              v-tooltip="'Pre-stage a new computer account'" />
      <Button label="Enable" icon="pi pi-check" size="small" severity="success" outlined
              @click="onEnable" :disabled="!selectedComputer?.objectGuid"
              v-tooltip="'Enable the selected computer account'" />
      <Button label="Disable" icon="pi pi-ban" size="small" severity="warn" outlined
              @click="onDisable" :disabled="!selectedComputer?.objectGuid"
              v-tooltip="'Disable the selected computer account'" />
      <Button label="Delete" icon="pi pi-trash" size="small" severity="danger" outlined
              @click="onDelete" :disabled="!selectedComputer?.objectGuid"
              v-tooltip="'Delete the selected computer'" />
      <Button icon="pi pi-refresh" size="small" severity="secondary" text
              @click="loadComputers" v-tooltip="'Refresh'" />
      <div class="toolbar-spacer" />
      <Button icon="pi pi-download" label="Export CSV" severity="secondary" outlined size="small"
              @click="onExport" v-tooltip="'Export current list to CSV'" />
      <Button icon="pi pi-th-large" size="small" severity="secondary" text
              @click="columnChooserVisible = true" title="Choose columns" />
      <InputText v-model="filterText" placeholder="Search computers..." size="small" style="width: 260px" />
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else class="card" style="padding: 0">
      <DataTable
        :value="filteredComputers"
        v-model:selection="selectedComputer"
        selectionMode="single"
        dataKey="objectGuid"
        stripedRows
        size="small"
        scrollable
        scrollHeight="calc(100vh - 260px)"
        :paginator="filteredComputers.length > 50"
        :rows="50"
        :rowsPerPageOptions="[25, 50, 100, 200]"
        @row-dblclick="onRowDoubleClick"
      >
        <template v-for="col in selectedCompColumns" :key="col">
          <Column v-if="col === 'name'" header="Name" sortable sortField="name" style="min-width: 200px">
            <template #body="{ data }">
              <div style="display: flex; align-items: center; gap: 0.5rem">
                <i class="pi pi-desktop" style="color: var(--p-text-muted-color)"></i>
                <span>{{ data.name }}</span>
              </div>
            </template>
          </Column>
          <Column v-else-if="col === 'enabled'" header="Status" sortable sortField="enabled" style="width: 110px">
            <template #body="{ data }">
              <Tag v-if="data.enabled !== undefined"
                   :value="data.enabled ? 'Enabled' : 'Disabled'"
                   :severity="data.enabled ? 'success' : 'danger'" />
            </template>
          </Column>
          <Column v-else-if="col === 'whenChanged'" header="Modified" sortable sortField="whenChanged" style="width: 130px">
            <template #body="{ data }">
              <span style="color: var(--p-text-muted-color)">{{ relativeTime(data.whenChanged) }}</span>
            </template>
          </Column>
          <Column v-else
            :field="col"
            :header="allAvailableCompColumns.find(c => c.field === col)?.label || col"
            sortable
            style="min-width: 150px"
          >
            <template #body="{ data }">
              <span style="color: var(--p-text-muted-color)">{{ (data as any)[col] || '' }}</span>
            </template>
          </Column>
        </template>
        <template #empty>
          <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
            <i class="pi pi-info-circle" style="font-size: 1.5rem; display: block; margin-bottom: 0.5rem"></i>
            <p style="margin: 0; font-size: 0.875rem">No computers found. Use the <strong>Create Computer</strong> button to pre-stage a computer account, or adjust your search filter.</p>
          </div>
        </template>
      </DataTable>
    </div>

    <PropertySheet
      v-if="propertySheetVisible"
      :objectGuid="propertySheetGuid"
      :visible="propertySheetVisible"
      @update:visible="propertySheetVisible = $event"
    />
    <CreateComputerDialog
      :visible="createComputerVisible"
      containerDn=""
      @update:visible="createComputerVisible = $event"
      @created="loadComputers"
    />
    <ColumnChooser
      :visible="columnChooserVisible"
      :availableColumns="allAvailableCompColumns"
      :selectedColumns="selectedCompColumns"
      :storageKey="COMP_COLUMN_KEY"
      @update:visible="columnChooserVisible = $event"
      @update:selectedColumns="selectedCompColumns = $event"
    />
  </div>
</template>
