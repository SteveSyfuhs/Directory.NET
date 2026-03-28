<script setup lang="ts">
import { ref, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import Panel from 'primevue/panel'
import { useToast } from 'primevue/usetoast'
import { getReplicationStatus, getFsmoRoles } from '../api/admin'
import { relativeTime } from '../utils/format'

const toast = useToast()
const status = ref<any>(null)
const fsmoRoles = ref<Record<string, string>>({})
const loading = ref(true)

onMounted(async () => {
  try {
    const [s, f] = await Promise.all([getReplicationStatus(), getFsmoRoles()])
    status.value = s
    fsmoRoles.value = f
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
})

const fsmoList = ref([
  { role: 'Schema Master', description: 'Controls schema modifications' },
  { role: 'Domain Naming Master', description: 'Controls domain/partition additions' },
  { role: 'PDC Emulator', description: 'Password changes, time sync, GPO authority' },
  { role: 'RID Master', description: 'Allocates RID pools to DCs' },
  { role: 'Infrastructure Master', description: 'Cross-domain reference updates' },
])
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Replication & FSMO</h1>
      <p>Monitor replication status and FSMO role holders</p>
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">View the health of Active Directory replication across domain controllers and identify which DC holds each FSMO (Flexible Single Master Operations) role. FSMO roles are specialized operations that only one DC can perform at a time to prevent conflicts.</p>
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else-if="!status && Object.keys(fsmoRoles).length === 0" style="text-align: center; padding: 3rem; color: var(--p-text-muted-color)">
      <i class="pi pi-sync" style="font-size: 2rem; margin-bottom: 0.5rem; display: block; opacity: 0.4"></i>
      No replication data available. Ensure domain controllers are online and replication is configured between sites.
    </div>

    <div v-else style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
      <Panel header="FSMO Role Holders" class="card">
        <DataTable :value="fsmoList" size="small" stripedRows>
          <Column field="role" header="Role" style="font-weight: 500" />
          <Column header="Holder">
            <template #body="{ data }">
              <span style="font-size: 0.85rem; color: var(--p-text-muted-color)">
                {{ fsmoRoles[data.role] || 'Not assigned' }}
              </span>
            </template>
          </Column>
        </DataTable>
      </Panel>

      <Panel header="Domain Controllers" class="card">
        <div v-if="status" style="margin-bottom: 1rem">
          <Tag :value="status.healthStatus" :severity="status.healthStatus === 'Healthy' ? 'success' : 'warn'" />
          <span style="margin-left: 0.5rem; color: var(--p-text-muted-color)">{{ status.dcCount }} DC(s)</span>
        </div>
        <DataTable v-if="status?.domainControllers" :value="status.domainControllers" size="small" stripedRows>
          <Column field="distinguishedName" header="NTDS Settings DN">
            <template #body="{ data }">
              <span style="font-size: 0.85rem">{{ data.distinguishedName }}</span>
            </template>
          </Column>
          <Column header="Last Updated" style="width: 130px">
            <template #body="{ data }">
              <span style="color: var(--p-text-muted-color)">{{ relativeTime(data.lastUpdated) }}</span>
            </template>
          </Column>
        </DataTable>
      </Panel>
    </div>
  </div>
</template>
