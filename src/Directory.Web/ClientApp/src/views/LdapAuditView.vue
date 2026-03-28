<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import Card from 'primevue/card'
import ProgressSpinner from 'primevue/progressspinner'
import ToggleSwitch from 'primevue/toggleswitch'
import { useToast } from 'primevue/usetoast'
import {
  fetchLdapAudit,
  fetchLdapAuditStatistics,
  fetchLdapActiveConnections,
} from '../api/ldapAudit'
import type {
  LdapAuditEntry,
  LdapAuditStatistics,
  LdapActiveConnection,
} from '../api/ldapAudit'

const toast = useToast()

// Audit entries
const entries = ref<LdapAuditEntry[]>([])
const loading = ref(true)

// Filters
const selectedOperation = ref<string | null>(null)
const filterClientIp = ref('')
const filterBoundDn = ref('')
const filterTargetDn = ref('')
const startDate = ref<Date | null>(null)
const endDate = ref<Date | null>(null)

// Statistics
const statistics = ref<LdapAuditStatistics | null>(null)

// Active connections
const activeConnections = ref<LdapActiveConnection[]>([])

// Auto-refresh
const autoRefresh = ref(false)
let refreshInterval: ReturnType<typeof setInterval> | null = null

const operationOptions = [
  { label: 'All Operations', value: null },
  { label: 'Bind', value: 'Bind' },
  { label: 'Search', value: 'Search' },
  { label: 'Add', value: 'Add' },
  { label: 'Modify', value: 'Modify' },
  { label: 'Delete', value: 'Delete' },
  { label: 'ModifyDN', value: 'ModifyDN' },
  { label: 'Compare', value: 'Compare' },
  { label: 'Extended', value: 'Extended' },
]

onMounted(() => {
  loadAll()
})

onUnmounted(() => {
  stopAutoRefresh()
})

function toggleAutoRefresh() {
  if (autoRefresh.value) {
    refreshInterval = setInterval(() => loadAll(), 5000)
  } else {
    stopAutoRefresh()
  }
}

function stopAutoRefresh() {
  if (refreshInterval) {
    clearInterval(refreshInterval)
    refreshInterval = null
  }
}

async function loadAll() {
  await Promise.all([loadAuditEntries(), loadStatistics(), loadActiveConnections()])
}

async function loadAuditEntries() {
  loading.value = true
  try {
    const result = await fetchLdapAudit({
      operation: selectedOperation.value || undefined,
      clientIp: filterClientIp.value || undefined,
      boundDn: filterBoundDn.value || undefined,
      targetDn: filterTargetDn.value || undefined,
      from: startDate.value ? startDate.value.toISOString() : undefined,
      to: endDate.value ? endDate.value.toISOString() : undefined,
      limit: 500,
    })
    entries.value = result.items
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function loadStatistics() {
  try {
    statistics.value = await fetchLdapAuditStatistics()
  } catch {
    // Statistics may fail silently
  }
}

async function loadActiveConnections() {
  try {
    activeConnections.value = await fetchLdapActiveConnections()
  } catch {
    // Active connections may fail silently
  }
}

// Computed: top operations as sorted array for the bar chart
const topOperationsArray = computed(() => {
  if (!statistics.value?.topOperations) return []
  return Object.entries(statistics.value.topOperations)
    .sort(([, a], [, b]) => b - a)
    .slice(0, 8)
})

const maxOpCount = computed(() => {
  if (topOperationsArray.value.length === 0) return 1
  return Math.max(...topOperationsArray.value.map(([, v]) => v), 1)
})

function formatTimestamp(ts: string) {
  if (!ts) return ''
  try {
    return new Date(ts).toLocaleString()
  } catch {
    return ts
  }
}

function operationSeverity(op: string): string {
  switch (op) {
    case 'Bind': return 'info'
    case 'Search': return 'secondary'
    case 'Add': return 'success'
    case 'Modify': return 'warn'
    case 'Delete': return 'danger'
    case 'ModifyDN': return 'warn'
    case 'Compare': return 'secondary'
    case 'Extended': return 'info'
    default: return 'secondary'
  }
}

function resultSeverity(code: string): string {
  if (code === '0' || code === 'Success') return 'success'
  if (code === 'Cancelled') return 'warn'
  if (code === 'Error') return 'danger'
  const num = parseInt(code)
  if (!isNaN(num) && num > 0) return 'danger'
  return 'secondary'
}

function formatDuration(ms: number): string {
  if (ms < 1) return '<1ms'
  if (ms < 1000) return `${ms}ms`
  return `${(ms / 1000).toFixed(1)}s`
}

function idleTime(lastActivity: string): string {
  const diff = Date.now() - new Date(lastActivity).getTime()
  if (diff < 1000) return 'now'
  if (diff < 60000) return `${Math.floor(diff / 1000)}s`
  if (diff < 3600000) return `${Math.floor(diff / 60000)}m`
  return `${Math.floor(diff / 3600000)}h`
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>LDAP Protocol Audit</h1>
      <p>Monitor and audit LDAP protocol-level operations including binds, searches, modifications, and more.</p>
    </div>

    <!-- Statistics Panel -->
    <div class="stat-grid" v-if="statistics">
      <div class="stat-card">
        <div class="stat-icon blue"><i class="pi pi-bolt"></i></div>
        <div>
          <div class="stat-value">{{ statistics.operationsPerSecond.toFixed(1) }}</div>
          <div class="stat-label">Ops/second</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon green"><i class="pi pi-list"></i></div>
        <div>
          <div class="stat-value">{{ statistics.totalEntries.toLocaleString() }}</div>
          <div class="stat-label">Buffered Entries</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon purple"><i class="pi pi-clock"></i></div>
        <div>
          <div class="stat-value">{{ statistics.averageDurationMs.toFixed(1) }}ms</div>
          <div class="stat-label">Avg Duration</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon amber"><i class="pi pi-link"></i></div>
        <div>
          <div class="stat-value">{{ activeConnections.length }}</div>
          <div class="stat-label">Active Connections</div>
        </div>
      </div>
    </div>

    <!-- Top Operations Bar Chart -->
    <div v-if="topOperationsArray.length > 0" class="card" style="margin-bottom: 1rem">
      <div class="card-title">Top Operations</div>
      <div style="display: flex; flex-direction: column; gap: 0.5rem">
        <div v-for="[op, count] in topOperationsArray" :key="op"
             style="display: flex; align-items: center; gap: 0.75rem">
          <span style="width: 80px; font-size: 0.8125rem; font-weight: 500">{{ op }}</span>
          <div style="flex: 1; height: 20px; background: var(--p-surface-ground); border-radius: 4px; overflow: hidden">
            <div :style="{ width: (count / maxOpCount * 100) + '%', height: '100%', background: 'var(--app-accent-color)', borderRadius: '4px', transition: 'width 0.3s ease' }"></div>
          </div>
          <span style="width: 60px; text-align: right; font-size: 0.8125rem; color: var(--p-text-muted-color)">{{ count }}</span>
        </div>
      </div>
    </div>

    <!-- Active Connections Panel -->
    <div v-if="activeConnections.length > 0" class="card" style="margin-bottom: 1rem">
      <div class="card-title">Active Connections</div>
      <DataTable :value="activeConnections" size="small" stripedRows>
        <Column field="clientIp" header="Client IP" style="width: 150px">
          <template #body="{ data }">
            <span style="font-family: monospace; font-size: 0.8125rem">{{ data.clientIp }}:{{ data.clientPort }}</span>
          </template>
        </Column>
        <Column field="boundDn" header="Bound DN" style="min-width: 200px">
          <template #body="{ data }">
            <span style="font-family: monospace; font-size: 0.8125rem">{{ data.boundDn || '(anonymous)' }}</span>
          </template>
        </Column>
        <Column header="Connected Since" style="width: 180px">
          <template #body="{ data }">
            <span style="color: var(--p-text-muted-color)">{{ formatTimestamp(data.connectedSince) }}</span>
          </template>
        </Column>
        <Column header="Idle" style="width: 80px">
          <template #body="{ data }">
            <span style="color: var(--p-text-muted-color)">{{ idleTime(data.lastActivity) }}</span>
          </template>
        </Column>
        <Column field="requestCount" header="Requests" style="width: 100px" />
      </DataTable>
    </div>

    <!-- Filters Toolbar -->
    <div class="toolbar">
      <Select v-model="selectedOperation" :options="operationOptions" optionLabel="label" optionValue="value"
              placeholder="Operation" size="small" style="width: 160px" />
      <InputText v-model="filterClientIp" placeholder="Client IP..." size="small" style="width: 140px" />
      <InputText v-model="filterBoundDn" placeholder="Bound DN..." size="small" style="width: 180px" />
      <InputText v-model="filterTargetDn" placeholder="Target DN..." size="small" style="width: 180px" />
      <DatePicker v-model="startDate" placeholder="From" size="small" showIcon dateFormat="yy-mm-dd"
                  style="width: 160px" />
      <DatePicker v-model="endDate" placeholder="To" size="small" showIcon dateFormat="yy-mm-dd"
                  style="width: 160px" />
      <Button label="Search" icon="pi pi-search" size="small" @click="loadAll" />
      <Button icon="pi pi-refresh" size="small" severity="secondary" text
              @click="loadAll" v-tooltip="'Refresh'" />
      <div class="toolbar-spacer" />
      <div style="display: flex; align-items: center; gap: 0.5rem">
        <span style="font-size: 0.8125rem; color: var(--p-text-muted-color)">Auto-refresh</span>
        <ToggleSwitch v-model="autoRefresh" @update:modelValue="toggleAutoRefresh" />
      </div>
    </div>

    <!-- Audit Entries Table -->
    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else class="card" style="padding: 0">
      <DataTable
        :value="entries"
        dataKey="id"
        stripedRows
        size="small"
        scrollable
        scrollHeight="calc(100vh - 480px)"
        :paginator="entries.length > 50"
        :rows="50"
        :rowsPerPageOptions="[25, 50, 100, 200]"
      >
        <Column header="Timestamp" sortable sortField="timestamp" style="width: 170px">
          <template #body="{ data }">
            <span style="color: var(--p-text-muted-color); font-size: 0.8125rem">{{ formatTimestamp(data.timestamp) }}</span>
          </template>
        </Column>
        <Column header="Operation" sortable sortField="operation" style="width: 110px">
          <template #body="{ data }">
            <Tag :value="data.operation" :severity="operationSeverity(data.operation)" />
          </template>
        </Column>
        <Column header="Client" sortable sortField="clientIp" style="width: 150px">
          <template #body="{ data }">
            <span style="font-family: monospace; font-size: 0.8125rem">{{ data.clientIp }}:{{ data.clientPort }}</span>
          </template>
        </Column>
        <Column field="boundDn" header="Bound DN" sortable style="min-width: 180px">
          <template #body="{ data }">
            <span style="font-family: monospace; font-size: 0.8125rem">{{ data.boundDn }}</span>
          </template>
        </Column>
        <Column field="targetDn" header="Target DN" sortable style="min-width: 250px">
          <template #body="{ data }">
            <span style="font-family: monospace; font-size: 0.8125rem">{{ data.targetDn }}</span>
          </template>
        </Column>
        <Column header="Result" sortable sortField="resultCode" style="width: 90px">
          <template #body="{ data }">
            <Tag :value="data.resultCode" :severity="resultSeverity(data.resultCode)" />
          </template>
        </Column>
        <Column header="Duration" sortable sortField="durationMs" style="width: 90px">
          <template #body="{ data }">
            <span style="color: var(--p-text-muted-color); font-size: 0.8125rem">{{ formatDuration(data.durationMs) }}</span>
          </template>
        </Column>
        <template #empty>
          <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
            <i class="pi pi-info-circle" style="font-size: 1.5rem; display: block; margin-bottom: 0.5rem"></i>
            <p style="margin: 0; font-size: 0.875rem">No LDAP audit entries found. Operations will appear here as LDAP clients connect and perform operations.</p>
          </div>
        </template>
      </DataTable>
    </div>
  </div>
</template>
