<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import { fetchAuditLog } from '../api/audit'
import type { AuditLogEntry } from '../api/audit'
import { exportToCsv } from '../composables/useExport'

const toast = useToast()

const entries = ref<AuditLogEntry[]>([])
const loading = ref(true)
const filterDn = ref('')
const startDate = ref<Date | null>(null)
const endDate = ref<Date | null>(null)
const selectedAction = ref<string | null>(null)

const actionOptions = [
  { label: 'All Actions', value: null },
  { label: 'Create', value: 'Create' },
  { label: 'Modify', value: 'Modify' },
  { label: 'Delete', value: 'Delete' },
  { label: 'Move', value: 'Move' },
  { label: 'Enable', value: 'Enable' },
  { label: 'Disable', value: 'Disable' },
  { label: 'Reset Password', value: 'ResetPassword' },
  { label: 'Unlock', value: 'Unlock' },
  { label: 'Add Member', value: 'AddMember' },
  { label: 'Remove Member', value: 'RemoveMember' },
]

onMounted(() => loadAuditLog())

async function loadAuditLog() {
  loading.value = true
  try {
    const result = await fetchAuditLog({
      startDate: startDate.value ? startDate.value.toISOString() : undefined,
      endDate: endDate.value ? endDate.value.toISOString() : undefined,
      action: selectedAction.value || undefined,
      targetDn: filterDn.value || undefined,
      pageSize: 500,
    })
    entries.value = result.items
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

const filteredEntries = computed(() => {
  if (!filterDn.value) return entries.value
  const q = filterDn.value.toLowerCase()
  return entries.value.filter(
    (e) =>
      (e.targetDn?.toLowerCase().includes(q)) ||
      (e.actor?.toLowerCase().includes(q)) ||
      (e.action?.toLowerCase().includes(q))
  )
})

function formatTimestamp(ts: string) {
  if (!ts) return ''
  try {
    return new Date(ts).toLocaleString()
  } catch {
    return ts
  }
}

function actionSeverity(action: string): string {
  switch (action) {
    case 'Create': return 'success'
    case 'Delete': return 'danger'
    case 'Disable': return 'warn'
    case 'Enable': return 'success'
    case 'ResetPassword': return 'warn'
    case 'Modify': return 'info'
    default: return 'secondary'
  }
}

function onExport() {
  const columns = [
    { field: 'timestamp', header: 'Timestamp' },
    { field: 'action', header: 'Action' },
    { field: 'targetDn', header: 'Target DN' },
    { field: 'objectClass', header: 'Object Class' },
    { field: 'actor', header: 'Actor' },
    { field: 'sourceIp', header: 'Source IP' },
    { field: 'success', header: 'Success' },
  ]
  exportToCsv(filteredEntries.value as Record<string, any>[], columns, 'audit-log')
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Audit Log</h1>
      <p>Track changes made to directory objects including who made them and when.</p>
    </div>

    <div class="toolbar">
      <DatePicker v-model="startDate" placeholder="Start date" size="small" showIcon dateFormat="yy-mm-dd"
                  style="width: 160px" />
      <DatePicker v-model="endDate" placeholder="End date" size="small" showIcon dateFormat="yy-mm-dd"
                  style="width: 160px" />
      <Select v-model="selectedAction" :options="actionOptions" optionLabel="label" optionValue="value"
              placeholder="Action type" size="small" style="width: 170px" />
      <Button label="Search" icon="pi pi-search" size="small" @click="loadAuditLog" />
      <Button icon="pi pi-refresh" size="small" severity="secondary" text
              @click="loadAuditLog" v-tooltip="'Refresh'" />
      <div class="toolbar-spacer" />
      <Button icon="pi pi-download" label="Export CSV" severity="secondary" outlined size="small"
              @click="onExport" v-tooltip="'Export audit log to CSV'" />
      <InputText v-model="filterDn" placeholder="Filter by DN or actor..." size="small" style="width: 280px" />
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else class="card" style="padding: 0">
      <DataTable
        :value="filteredEntries"
        dataKey="id"
        stripedRows
        size="small"
        scrollable
        scrollHeight="calc(100vh - 280px)"
        :paginator="filteredEntries.length > 50"
        :rows="50"
        :rowsPerPageOptions="[25, 50, 100, 200]"
      >
        <Column header="Timestamp" sortable sortField="timestamp" style="width: 180px">
          <template #body="{ data }">
            <span style="color: var(--p-text-muted-color)">{{ formatTimestamp(data.timestamp) }}</span>
          </template>
        </Column>
        <Column header="Action" sortable sortField="action" style="width: 140px">
          <template #body="{ data }">
            <Tag :value="data.action" :severity="actionSeverity(data.action)" />
          </template>
        </Column>
        <Column field="targetDn" header="Target DN" sortable style="min-width: 300px">
          <template #body="{ data }">
            <span style="font-family: monospace; font-size: 0.8125rem">{{ data.targetDn }}</span>
          </template>
        </Column>
        <Column field="objectClass" header="Object Class" sortable style="width: 130px" />
        <Column field="actor" header="Actor" sortable style="min-width: 180px">
          <template #body="{ data }">
            <div style="display: flex; align-items: center; gap: 0.5rem">
              <i class="pi pi-user" style="color: var(--p-text-muted-color)"></i>
              <span>{{ data.actor }}</span>
            </div>
          </template>
        </Column>
        <Column field="sourceIp" header="Source IP" sortable style="width: 140px">
          <template #body="{ data }">
            <span style="color: var(--p-text-muted-color); font-family: monospace; font-size: 0.8125rem">{{ data.sourceIp }}</span>
          </template>
        </Column>
        <Column header="Success" sortable sortField="success" style="width: 100px">
          <template #body="{ data }">
            <Tag :value="data.success ? 'Yes' : 'No'" :severity="data.success ? 'success' : 'danger'" />
          </template>
        </Column>
        <template #empty>
          <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
            <i class="pi pi-info-circle" style="font-size: 1.5rem; display: block; margin-bottom: 0.5rem"></i>
            <p style="margin: 0; font-size: 0.875rem">No audit log entries found. Try adjusting the date range or filters.</p>
          </div>
        </template>
      </DataTable>
    </div>
  </div>
</template>
