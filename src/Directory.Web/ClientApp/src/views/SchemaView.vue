<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import Button from 'primevue/button'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import { fetchAttributes, fetchClasses, fetchSchemaReplicationStatus, fetchSchemaReplicationHistory, forceSchemaSync } from '../api/schema'
import type { SchemaAttribute, SchemaClass } from '../api/types'
import type { SchemaReplicationStatus, SchemaChangeEntry } from '../api/schema'

const toast = useToast()

const attributes = ref<SchemaAttribute[]>([])
const classes = ref<SchemaClass[]>([])
const loadingAttrs = ref(true)
const loadingClasses = ref(true)
const attrFilter = ref('')
const classFilter = ref('')
const expandedClassRows = ref<Record<string, boolean>>({})

// Replication state
const replStatus = ref<SchemaReplicationStatus | null>(null)
const replHistory = ref<SchemaChangeEntry[]>([])
const loadingRepl = ref(true)
const syncing = ref(false)

onMounted(async () => {
  fetchAttributes().then((data) => { attributes.value = data }).catch((e) => {
    toast.add({ severity: 'error', summary: 'Error', detail: `Failed to load attributes: ${e.message}`, life: 5000 })
  }).finally(() => { loadingAttrs.value = false })
  fetchClasses().then((data) => { classes.value = data }).catch((e) => {
    toast.add({ severity: 'error', summary: 'Error', detail: `Failed to load classes: ${e.message}`, life: 5000 })
  }).finally(() => { loadingClasses.value = false })
  loadReplicationData()
})

async function loadReplicationData() {
  loadingRepl.value = true
  try {
    const [status, history] = await Promise.all([
      fetchSchemaReplicationStatus(),
      fetchSchemaReplicationHistory(20),
    ])
    replStatus.value = status
    replHistory.value = history
  } catch {
    // Replication data may not be available
  } finally {
    loadingRepl.value = false
  }
}

async function handleForceSync() {
  syncing.value = true
  try {
    await forceSchemaSync()
    toast.add({ severity: 'success', summary: 'Schema Synced', detail: 'Schema has been reloaded from the store.', life: 3000 })
    await loadReplicationData()
  } catch {
    toast.add({ severity: 'error', summary: 'Sync Failed', detail: 'Could not sync schema from store.', life: 5000 })
  } finally {
    syncing.value = false
  }
}

function healthSeverity(health: string): string {
  if (health === 'Healthy') return 'success'
  if (health === 'Warning') return 'warn'
  return 'danger'
}

function formatTimestamp(ts: string): string {
  if (!ts) return 'N/A'
  return new Date(ts).toLocaleString()
}

const filteredAttributes = computed(() => {
  if (!attrFilter.value) return attributes.value
  const q = attrFilter.value.toLowerCase()
  return attributes.value.filter(
    (a) => a.name.toLowerCase().includes(q) || a.oid.includes(q) || a.syntax.toLowerCase().includes(q)
  )
})

const filteredClasses = computed(() => {
  if (!classFilter.value) return classes.value
  const q = classFilter.value.toLowerCase()
  return classes.value.filter(
    (c) => c.name.toLowerCase().includes(q) || c.oid.includes(q) || (c.superiorClass?.toLowerCase().includes(q))
  )
})
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Schema Browser</h1>
      <p>Explore Active Directory schema attributes and classes</p>
    </div>

    <Tabs value="attributes">
      <TabList>
        <Tab value="attributes">Attributes</Tab>
        <Tab value="classes">Classes</Tab>
        <Tab value="replication">Replication</Tab>
      </TabList>
      <TabPanels>
      <TabPanel value="attributes">
        <div class="toolbar">
          <InputText v-model="attrFilter" placeholder="Filter attributes..." size="small" style="width: 300px" />
          <div class="toolbar-spacer" />
          <span style="color: var(--p-text-muted-color); font-size: 0.8125rem">{{ filteredAttributes.length }} attributes</span>
        </div>

        <div v-if="loadingAttrs" style="text-align: center; padding: 3rem">
          <ProgressSpinner />
        </div>
        <DataTable
          v-else
          :value="filteredAttributes"
          stripedRows
          size="small"
          scrollable
          scrollHeight="calc(100vh - 320px)"
          :paginator="filteredAttributes.length > 100"
          :rows="100"
          :rowsPerPageOptions="[50, 100, 250]"
        >
          <Column field="name" header="Name" sortable style="min-width: 220px">
            <template #body="{ data }">
              <span style="font-family: monospace; font-size: 0.8125rem">{{ data.name }}</span>
            </template>
          </Column>
          <Column field="oid" header="OID" sortable style="width: 220px">
            <template #body="{ data }">
              <span style="font-family: monospace; font-size: 0.75rem; color: var(--p-text-muted-color)">{{ data.oid }}</span>
            </template>
          </Column>
          <Column field="syntax" header="Syntax" sortable style="width: 200px" />
          <Column header="Single" sortable sortField="isSingleValued" style="width: 90px">
            <template #body="{ data }">
              <Tag :value="data.isSingleValued ? 'Yes' : 'No'"
                   :severity="data.isSingleValued ? 'info' : 'secondary'" />
            </template>
          </Column>
          <Column header="Indexed" sortable sortField="isIndexed" style="width: 90px">
            <template #body="{ data }">
              <Tag v-if="data.isIndexed" value="Yes" severity="success" />
              <span v-else style="color: var(--p-text-muted-color)">No</span>
            </template>
          </Column>
          <Column header="GC" sortable sortField="isInGlobalCatalog" style="width: 70px">
            <template #body="{ data }">
              <i v-if="data.isInGlobalCatalog" class="pi pi-check" style="color: var(--app-success-text)"></i>
            </template>
          </Column>
          <template #empty>
            <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">No attributes found</div>
          </template>
        </DataTable>
      </TabPanel>

      <TabPanel value="classes">
        <div class="toolbar">
          <InputText v-model="classFilter" placeholder="Filter classes..." size="small" style="width: 300px" />
          <div class="toolbar-spacer" />
          <span style="color: var(--p-text-muted-color); font-size: 0.8125rem">{{ filteredClasses.length }} classes</span>
        </div>

        <div v-if="loadingClasses" style="text-align: center; padding: 3rem">
          <ProgressSpinner />
        </div>
        <DataTable
          v-else
          :value="filteredClasses"
          v-model:expandedRows="expandedClassRows"
          dataKey="oid"
          stripedRows
          size="small"
          scrollable
          scrollHeight="calc(100vh - 320px)"
          :paginator="filteredClasses.length > 100"
          :rows="100"
          :rowsPerPageOptions="[50, 100, 250]"
        >
          <Column expander style="width: 40px" />
          <Column field="name" header="Name" sortable style="min-width: 220px">
            <template #body="{ data }">
              <span style="font-family: monospace; font-size: 0.8125rem">{{ data.name }}</span>
            </template>
          </Column>
          <Column field="oid" header="OID" sortable style="width: 220px">
            <template #body="{ data }">
              <span style="font-family: monospace; font-size: 0.75rem; color: var(--p-text-muted-color)">{{ data.oid }}</span>
            </template>
          </Column>
          <Column field="classType" header="Type" sortable style="width: 130px">
            <template #body="{ data }">
              <Tag :value="data.classType"
                   :severity="data.classType === 'Structural' ? 'info' : data.classType === 'Abstract' ? 'warn' : 'secondary'" />
            </template>
          </Column>
          <Column field="superiorClass" header="Superior" sortable style="width: 180px">
            <template #body="{ data }">
              <span style="color: var(--p-text-muted-color)">{{ data.superiorClass || '' }}</span>
            </template>
          </Column>
          <Column header="Must" sortable sortField="mustContain.length" style="width: 80px">
            <template #body="{ data }">{{ data.mustContain?.length || 0 }}</template>
          </Column>
          <Column header="May" sortable sortField="mayContain.length" style="width: 80px">
            <template #body="{ data }">{{ data.mayContain?.length || 0 }}</template>
          </Column>
          <template #expansion="{ data }">
            <div style="padding: 1rem 2rem">
              <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1.5rem">
                <div>
                  <h4 style="font-size: 0.8125rem; font-weight: 600; color: var(--p-text-muted-color); margin-bottom: 0.5rem">
                    Must Contain ({{ data.mustContain?.length || 0 }})
                  </h4>
                  <div v-if="data.mustContain?.length" style="display: flex; flex-wrap: wrap; gap: 0.25rem">
                    <Tag v-for="attr in data.mustContain" :key="attr" :value="attr" severity="info" />
                  </div>
                  <span v-else style="color: var(--p-text-muted-color); font-size: 0.8125rem">None</span>
                </div>
                <div>
                  <h4 style="font-size: 0.8125rem; font-weight: 600; color: var(--p-text-muted-color); margin-bottom: 0.5rem">
                    May Contain ({{ data.mayContain?.length || 0 }})
                  </h4>
                  <div v-if="data.mayContain?.length" style="display: flex; flex-wrap: wrap; gap: 0.25rem">
                    <Tag v-for="attr in data.mayContain" :key="attr" :value="attr" severity="secondary" />
                  </div>
                  <span v-else style="color: var(--p-text-muted-color); font-size: 0.8125rem">None</span>
                </div>
              </div>
            </div>
          </template>
          <template #empty>
            <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">No classes found</div>
          </template>
        </DataTable>
      </TabPanel>

      <TabPanel value="replication">
        <div v-if="loadingRepl" style="text-align: center; padding: 3rem">
          <ProgressSpinner />
        </div>
        <div v-else>
          <!-- Status cards -->
          <div class="stat-grid" style="margin-bottom: 1.5rem">
            <div class="stat-card">
              <div class="stat-icon blue"><i class="pi pi-database"></i></div>
              <div>
                <div class="stat-value">{{ replStatus?.currentSchemaVersion ?? 0 }}</div>
                <div class="stat-label">Schema Version</div>
              </div>
            </div>
            <div class="stat-card">
              <div class="stat-icon green"><i class="pi pi-clock"></i></div>
              <div>
                <div class="stat-value" style="font-size: 1rem">{{ replStatus ? formatTimestamp(replStatus.lastSyncTime) : 'N/A' }}</div>
                <div class="stat-label">Last Sync Time</div>
              </div>
            </div>
            <div class="stat-card">
              <div class="stat-icon purple"><i class="pi pi-server"></i></div>
              <div>
                <div class="stat-value" style="font-size: 1rem">{{ replStatus?.originServer || 'N/A' }}</div>
                <div class="stat-label">Origin Server</div>
              </div>
            </div>
            <div class="stat-card">
              <div class="stat-icon" :class="replStatus?.health === 'Healthy' ? 'green' : replStatus?.health === 'Warning' ? 'amber' : 'purple'">
                <i class="pi" :class="replStatus?.health === 'Healthy' ? 'pi-check-circle' : replStatus?.health === 'Warning' ? 'pi-exclamation-triangle' : 'pi-times-circle'"></i>
              </div>
              <div>
                <Tag :value="replStatus?.health || 'Unknown'" :severity="healthSeverity(replStatus?.health || '')" />
                <div class="stat-label">Replication Health</div>
              </div>
            </div>
          </div>

          <!-- Pending changes + sync button -->
          <div class="toolbar">
            <span v-if="replStatus && replStatus.pendingChanges > 0" style="color: var(--app-warn-text)">
              <i class="pi pi-exclamation-triangle" style="margin-right: 0.25rem"></i>
              {{ replStatus.pendingChanges }} pending change(s)
            </span>
            <div class="toolbar-spacer" />
            <Button label="Refresh" icon="pi pi-refresh" severity="secondary" size="small" @click="loadReplicationData" />
            <Button label="Force Sync" icon="pi pi-sync" size="small" :loading="syncing" @click="handleForceSync" />
          </div>

          <!-- Recent changes table -->
          <div class="card">
            <div class="card-title">Recent Schema Changes</div>
            <DataTable
              :value="replHistory"
              stripedRows
              size="small"
              scrollable
              scrollHeight="calc(100vh - 560px)"
              :paginator="replHistory.length > 20"
              :rows="20"
            >
              <Column field="timestamp" header="Timestamp" sortable style="width: 200px">
                <template #body="{ data }">
                  <span style="font-size: 0.8125rem">{{ formatTimestamp(data.timestamp) }}</span>
                </template>
              </Column>
              <Column field="changeType" header="Change Type" sortable style="width: 180px">
                <template #body="{ data }">
                  <Tag :value="data.changeType"
                       :severity="data.changeType.includes('Added') ? 'success' : 'info'" />
                </template>
              </Column>
              <Column field="objectName" header="Object" sortable style="min-width: 200px">
                <template #body="{ data }">
                  <span style="font-family: monospace; font-size: 0.8125rem">{{ data.objectName }}</span>
                </template>
              </Column>
              <Column field="schemaVersion" header="Version" sortable style="width: 100px" />
              <Column field="originServer" header="Origin Server" sortable style="width: 180px" />
              <template #empty>
                <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">No schema changes recorded</div>
              </template>
            </DataTable>
          </div>
        </div>
      </TabPanel>
      </TabPanels>
    </Tabs>
  </div>
</template>
