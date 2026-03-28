<script setup lang="ts">
import { ref, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import { fetchSummary, fetchDcHealth, fetchRecentChanges } from '../api/dashboard'
import type { DashboardSummary, DcHealth, ObjectSummary } from '../api/types'
import { relativeTime, objectClassIcon, functionalLevelName } from '../utils/format'

const summary = ref<DashboardSummary | null>(null)
const dcHealth = ref<DcHealth[]>([])
const recentChanges = ref<ObjectSummary[]>([])
const loading = ref(true)

onMounted(async () => {
  try {
    const [s, dc, rc] = await Promise.all([
      fetchSummary(),
      fetchDcHealth(),
      fetchRecentChanges(),
    ])
    summary.value = s
    dcHealth.value = dc
    recentChanges.value = rc
  } catch (e) {
    console.error('Failed to load dashboard', e)
  } finally {
    loading.value = false
  }
})

const stats = [
  { key: 'userCount', label: 'Users', icon: 'pi pi-user', color: 'blue' },
  { key: 'computerCount', label: 'Computers', icon: 'pi pi-desktop', color: 'green' },
  { key: 'groupCount', label: 'Groups', icon: 'pi pi-users', color: 'purple' },
  { key: 'ouCount', label: 'OUs', icon: 'pi pi-folder', color: 'amber' },
]

function getStatValue(key: string): number {
  if (!summary.value) return 0
  return (summary.value as any)[key] ?? 0
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Dashboard</h1>
      <p>Active Directory environment overview</p>
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <template v-else>
      <!-- Stat Cards -->
      <div class="stat-grid">
        <div v-for="stat in stats" :key="stat.key" class="stat-card">
          <div class="stat-icon" :class="stat.color">
            <i :class="stat.icon"></i>
          </div>
          <div>
            <div class="stat-value">{{ getStatValue(stat.key).toLocaleString() }}</div>
            <div class="stat-label">{{ stat.label }}</div>
          </div>
        </div>
      </div>

      <!-- Domain Info -->
      <div v-if="summary" class="card" style="margin-bottom: 1.5rem">
        <div class="card-title">Domain Information</div>
        <div class="domain-info-grid">
          <div class="domain-info-item">
            <span class="domain-info-label">Domain Name</span>
            <span class="domain-info-value">{{ summary.domainName }}</span>
          </div>
          <div class="domain-info-item">
            <span class="domain-info-label">Domain DN</span>
            <span class="domain-info-value">{{ summary.domainDn }}</span>
          </div>
          <div class="domain-info-item">
            <span class="domain-info-label">Domain SID</span>
            <span class="domain-info-value">{{ summary.domainSid }}</span>
          </div>
          <div class="domain-info-item">
            <span class="domain-info-label">Functional Level</span>
            <span class="domain-info-value">{{ functionalLevelName(summary.functionalLevel) }}</span>
          </div>
          <div class="domain-info-item">
            <span class="domain-info-label">Total Objects</span>
            <span class="domain-info-value">{{ summary.totalObjects.toLocaleString() }}</span>
          </div>
        </div>
      </div>

      <!-- DC Health -->
      <div class="card" style="margin-bottom: 1.5rem">
        <div class="card-title">Domain Controller Health</div>
        <DataTable :value="dcHealth" stripedRows size="small">
          <Column field="hostname" header="Hostname" sortable />
          <Column field="siteName" header="Site" sortable />
          <Column header="Status" style="width: 120px">
            <template #body="{ data }">
              <Tag :value="data.isHealthy ? 'Healthy' : 'Unhealthy'"
                   :severity="data.isHealthy ? 'success' : 'danger'" />
            </template>
          </Column>
          <Column header="Last Heartbeat" style="width: 160px">
            <template #body="{ data }">
              {{ relativeTime(data.lastHeartbeat) }}
            </template>
          </Column>
        </DataTable>
      </div>

      <!-- Recent Changes -->
      <div class="card">
        <div class="card-title">Recent Changes</div>
        <DataTable :value="recentChanges" stripedRows size="small" :rows="10">
          <Column header="Name">
            <template #body="{ data }">
              <div style="display: flex; align-items: center; gap: 0.5rem">
                <i :class="objectClassIcon(data.objectClass)" style="color: var(--p-text-muted-color)"></i>
                <span>{{ data.name || data.dn }}</span>
              </div>
            </template>
          </Column>
          <Column field="objectClass" header="Type" sortable style="width: 140px" />
          <Column header="Modified" sortable sortField="whenChanged" style="width: 140px">
            <template #body="{ data }">
              {{ relativeTime(data.whenChanged) }}
            </template>
          </Column>
        </DataTable>
      </div>
    </template>
  </div>
</template>

<style scoped>
.domain-info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 1rem;
}

.domain-info-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.domain-info-label {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--p-text-muted-color);
}

.domain-info-value {
  font-size: 0.9375rem;
  color: var(--p-text-color);
  font-family: 'SF Mono', 'Cascadia Code', 'Fira Code', monospace;
}
</style>
