<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import { fetchNodes, fetchSections } from '../api/configuration'
import type { ConfigNode, ConfigSection } from '../types/configuration'

const toast = useToast()
const nodes = ref<ConfigNode[]>([])
const sections = ref<ConfigSection[]>([])
const loading = ref(true)
let refreshTimer: ReturnType<typeof setInterval> | null = null

onMounted(async () => {
  await loadData()
  refreshTimer = setInterval(loadData, 30000)
})

onUnmounted(() => {
  if (refreshTimer) clearInterval(refreshTimer)
})

async function loadData() {
  try {
    const [n, s] = await Promise.all([fetchNodes(), fetchSections()])
    nodes.value = n
    sections.value = s
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

const totalNodes = computed(() => nodes.value.length)

const nodesInSync = computed(() =>
  nodes.value.filter(n => getStatus(n) === 'In Sync').length
)

const nodesWithOverrides = computed(() =>
  sections.value.reduce((set, s) => {
    s.hasNodeOverrides.forEach(h => set.add(h))
    return set
  }, new Set<string>()).size
)

function getStatus(node: ConfigNode): string {
  const lastSeen = new Date(node.lastSeen).getTime()
  const fiveMinAgo = Date.now() - 5 * 60 * 1000

  if (lastSeen < fiveMinAgo && node.lastSeen !== '0001-01-01T00:00:00+00:00') {
    return 'Offline'
  }
  if (node.configVersion > 0 && node.configVersion < node.clusterVersion) {
    return 'Outdated'
  }
  return 'In Sync'
}

function getStatusSeverity(status: string): "success" | "warn" | "danger" | "info" {
  switch (status) {
    case 'In Sync': return 'success'
    case 'Outdated': return 'warn'
    case 'Offline': return 'danger'
    default: return 'info'
  }
}

function formatLastSeen(iso: string): string {
  if (!iso || iso === '0001-01-01T00:00:00+00:00') return 'Never'
  const d = new Date(iso)
  const now = new Date()
  const diffMs = now.getTime() - d.getTime()
  const diffSec = Math.floor(diffMs / 1000)
  if (diffSec < 60) return `${diffSec}s ago`
  const diffMin = Math.floor(diffSec / 60)
  if (diffMin < 60) return `${diffMin}m ago`
  const diffHr = Math.floor(diffMin / 60)
  if (diffHr < 24) return `${diffHr}h ago`
  return d.toLocaleDateString()
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Configuration Status</h1>
      <p>Monitor node configuration synchronization (auto-refreshes every 30s)</p>
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">Track whether each node in the cluster is running the latest configuration. Nodes that fall behind the cluster version are marked as outdated, and nodes that have not reported recently are shown as offline.</p>
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <template v-else>
      <!-- Summary cards -->
      <div class="stat-grid">
        <div class="stat-card">
          <div class="stat-icon blue">
            <i class="pi pi-server"></i>
          </div>
          <div>
            <div class="stat-value">{{ totalNodes }}</div>
            <div class="stat-label">Total Nodes</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon green">
            <i class="pi pi-check-circle"></i>
          </div>
          <div>
            <div class="stat-value">{{ nodesInSync }}</div>
            <div class="stat-label">In Sync</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon purple">
            <i class="pi pi-sliders-h"></i>
          </div>
          <div>
            <div class="stat-value">{{ nodesWithOverrides }}</div>
            <div class="stat-label">With Overrides</div>
          </div>
        </div>
      </div>

      <!-- Nodes table -->
      <div class="card" style="padding: 0">
        <DataTable :value="nodes" stripedRows size="small" dataKey="hostname">
          <template #header>
            <div style="font-weight: 600; padding: 0.25rem">Registered Nodes</div>
          </template>
          <Column field="hostname" header="Hostname" sortable />
          <Column field="site" header="Site" sortable />
          <Column field="configVersion" header="Config Version" sortable style="width: 140px">
            <template #body="{ data }">
              {{ data.configVersion || '—' }}
            </template>
          </Column>
          <Column header="Last Seen" sortable sortField="lastSeen" style="width: 140px">
            <template #body="{ data }">
              <span style="color: var(--p-text-muted-color)">{{ formatLastSeen(data.lastSeen) }}</span>
            </template>
          </Column>
          <Column header="Status" style="width: 120px">
            <template #body="{ data }">
              <Tag :value="getStatus(data)" :severity="getStatusSeverity(getStatus(data))" />
            </template>
          </Column>
          <template #empty>
            <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
              No registered nodes found. Nodes register automatically when they start and begin pulling configuration from the cluster.
            </div>
          </template>
        </DataTable>
      </div>
    </template>
  </div>
</template>
