<script setup lang="ts">
import { ref, onMounted } from 'vue'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputSwitch from 'primevue/inputswitch'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import type { RegionConfiguration } from '../types/multiRegion'
import {
  fetchRegions,
  createRegion,
  updateRegion,
  deleteRegion,
  setPrimaryRegion,
  checkRegionHealth,
} from '../api/multiRegion'

const toast = useToast()
const loading = ref(false)
const regions = ref<RegionConfiguration[]>([])

const showEditDialog = ref(false)
const editingRegion = ref<Partial<RegionConfiguration>>({})
const isNew = ref(false)
const checking = ref(false)

const newEndpoint = ref('')

onMounted(async () => {
  await loadRegions()
})

async function loadRegions() {
  loading.value = true
  try {
    regions.value = await fetchRegions()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

function openCreate() {
  isNew.value = true
  editingRegion.value = { isEnabled: true, isPrimary: false, dcEndpoints: [] }
  showEditDialog.value = true
}

function openEdit(region: RegionConfiguration) {
  isNew.value = false
  editingRegion.value = { ...region, dcEndpoints: [...region.dcEndpoints] }
  showEditDialog.value = true
}

async function saveRegion() {
  try {
    if (isNew.value) {
      await createRegion(editingRegion.value)
      toast.add({ severity: 'success', summary: 'Region Created', life: 3000 })
    } else {
      await updateRegion(editingRegion.value.id!, editingRegion.value)
      toast.add({ severity: 'success', summary: 'Region Updated', life: 3000 })
    }
    showEditDialog.value = false
    await loadRegions()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function removeRegion(region: RegionConfiguration) {
  try {
    await deleteRegion(region.id)
    toast.add({ severity: 'success', summary: 'Region Deleted', life: 3000 })
    await loadRegions()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function makePrimary(region: RegionConfiguration) {
  try {
    await setPrimaryRegion(region.id)
    toast.add({ severity: 'success', summary: 'Primary Region Updated', life: 3000 })
    await loadRegions()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function runHealthCheck() {
  checking.value = true
  try {
    await checkRegionHealth()
    toast.add({ severity: 'success', summary: 'Health Check Complete', life: 3000 })
    await loadRegions()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    checking.value = false
  }
}

function addDcEndpoint() {
  if (newEndpoint.value.trim()) {
    if (!editingRegion.value.dcEndpoints) editingRegion.value.dcEndpoints = []
    editingRegion.value.dcEndpoints.push(newEndpoint.value.trim())
    newEndpoint.value = ''
  }
}

function removeDcEndpoint(index: number) {
  editingRegion.value.dcEndpoints?.splice(index, 1)
}

function healthSeverity(health: string) {
  switch (health) {
    case 'Healthy': return 'success'
    case 'Degraded': return 'warn'
    case 'Offline': return 'danger'
    default: return 'secondary'
  }
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Multi-Region Configuration</h1>
      <p>Configure and manage directory service regions with Cosmos DB multi-region support.</p>
    </div>

    <div class="toolbar">
      <Button label="Add Region" icon="pi pi-plus" size="small" @click="openCreate" />
      <Button label="Health Check" icon="pi pi-heart" size="small" severity="secondary" :loading="checking" @click="runHealthCheck" />
    </div>

    <!-- Region Cards -->
    <div style="display: grid; grid-template-columns: repeat(auto-fill, minmax(350px, 1fr)); gap: 1rem">
      <div v-for="region in regions" :key="region.id" class="card" :style="{ borderLeft: region.isPrimary ? '4px solid var(--app-accent-color)' : '' }">
        <div style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 1rem">
          <strong style="font-size: 1.1rem">{{ region.name }}</strong>
          <Tag v-if="region.isPrimary" value="Primary" severity="info" />
          <Tag :value="region.health" :severity="healthSeverity(region.health)" />
          <span style="flex: 1"></span>
          <Tag :value="region.isEnabled ? 'Enabled' : 'Disabled'" :severity="region.isEnabled ? 'success' : 'secondary'" />
        </div>

        <div style="margin-bottom: 0.75rem">
          <div style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin-bottom: 0.25rem">Cosmos DB Endpoint</div>
          <div style="font-size: 0.875rem; word-break: break-all">{{ region.cosmosDbEndpoint || '(not set)' }}</div>
        </div>

        <div style="margin-bottom: 0.75rem">
          <div style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin-bottom: 0.25rem">Preferred Region</div>
          <div style="font-size: 0.875rem">{{ region.preferredRegion || '(not set)' }}</div>
        </div>

        <div style="margin-bottom: 0.75rem">
          <div style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin-bottom: 0.25rem">DC Endpoints ({{ region.dcEndpoints.length }})</div>
          <div v-for="ep in region.dcEndpoints" :key="ep" style="font-size: 0.875rem">{{ ep }}</div>
          <div v-if="!region.dcEndpoints.length" style="font-size: 0.875rem; color: var(--p-text-muted-color)">(none)</div>
        </div>

        <div v-if="region.lastHealthCheck" style="font-size: 0.75rem; color: var(--p-text-muted-color); margin-bottom: 0.75rem">
          Last health check: {{ new Date(region.lastHealthCheck).toLocaleString() }}
        </div>

        <div style="display: flex; gap: 0.5rem">
          <Button icon="pi pi-pencil" size="small" text rounded v-tooltip="'Edit'" @click="openEdit(region)" />
          <Button v-if="!region.isPrimary" icon="pi pi-star" size="small" text rounded v-tooltip="'Set as Primary'" @click="makePrimary(region)" />
          <Button icon="pi pi-trash" size="small" text rounded severity="danger" v-tooltip="'Delete'" @click="removeRegion(region)" />
        </div>
      </div>
    </div>

    <div v-if="!regions.length && !loading" style="text-align: center; padding: 3rem; color: var(--p-text-muted-color)">
      No regions configured. Add a region to get started.
    </div>

    <!-- Edit Dialog -->
    <Dialog v-model:visible="showEditDialog" :header="isNew ? 'Add Region' : 'Edit Region'" :style="{ width: '500px' }" modal>
      <div style="display: flex; flex-direction: column; gap: 1rem">
        <div><label>Name</label><InputText v-model="editingRegion.name" style="width: 100%" placeholder="e.g., US East" /></div>
        <div><label>Cosmos DB Endpoint</label><InputText v-model="editingRegion.cosmosDbEndpoint" style="width: 100%" placeholder="https://myaccount.documents.azure.com:443/" /></div>
        <div><label>Preferred Region</label><InputText v-model="editingRegion.preferredRegion" style="width: 100%" placeholder="East US" /></div>
        <div style="display: flex; gap: 1.5rem">
          <label style="display: flex; align-items: center; gap: 0.5rem"><InputSwitch v-model="editingRegion.isPrimary" /> Primary</label>
          <label style="display: flex; align-items: center; gap: 0.5rem"><InputSwitch v-model="editingRegion.isEnabled" /> Enabled</label>
        </div>
        <div>
          <label>DC Endpoints</label>
          <div style="display: flex; gap: 0.5rem; margin-top: 0.25rem">
            <InputText v-model="newEndpoint" style="flex: 1" placeholder="https://dc1.example.com" @keyup.enter="addDcEndpoint" />
            <Button icon="pi pi-plus" size="small" @click="addDcEndpoint" />
          </div>
          <div v-for="(ep, i) in editingRegion.dcEndpoints" :key="i" style="display: flex; align-items: center; gap: 0.5rem; margin-top: 0.25rem">
            <span style="flex: 1; font-size: 0.875rem">{{ ep }}</span>
            <Button icon="pi pi-times" size="small" text severity="danger" @click="removeDcEndpoint(i)" />
          </div>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="showEditDialog = false" />
        <Button label="Save" icon="pi pi-check" @click="saveRegion" />
      </template>
    </Dialog>
  </div>
</template>
