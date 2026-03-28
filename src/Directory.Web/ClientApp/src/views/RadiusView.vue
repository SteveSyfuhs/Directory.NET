<script setup lang="ts">
import { ref, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import Checkbox from 'primevue/checkbox'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import { useToast } from 'primevue/usetoast'
import {
  getSettings,
  updateSettings,
  getClients,
  addClient,
  updateClient,
  deleteClient,
  getLog,
} from '../api/radius'
import type { RadiusSettings, RadiusClient, RadiusLogEntry } from '../types/radius'

const toast = useToast()

const activeTab = ref('0')
const loading = ref(true)

// ── Settings ──────────────────────────────────────────────────
const settings = ref<RadiusSettings>({
  enabled: false,
  port: 1812,
  accountingPort: 1813,
  clients: [],
})
const settingsSaving = ref(false)

async function loadSettings() {
  try { settings.value = await getSettings() } catch { /* ignore */ }
}

async function saveSettings() {
  settingsSaving.value = true
  try {
    settings.value = await updateSettings({
      enabled: settings.value.enabled,
      port: settings.value.port,
      accountingPort: settings.value.accountingPort,
    })
    toast.add({ severity: 'success', summary: 'Settings saved', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    settingsSaving.value = false
  }
}

// ── Clients ───────────────────────────────────────────────────
const clients = ref<RadiusClient[]>([])
const clientEditVisible = ref(false)
const clientEditSaving = ref(false)
const isNewClient = ref(false)
const clientForm = ref<RadiusClient>(emptyClient())
const clientDeleteVisible = ref(false)
const clientDeleteTarget = ref<RadiusClient | null>(null)
const clientDeleting = ref(false)

function emptyClient(): RadiusClient {
  return {
    id: '',
    name: '',
    ipAddress: '',
    sharedSecret: '',
    description: '',
    isEnabled: true,
  }
}

function openNewClient() {
  clientForm.value = emptyClient()
  isNewClient.value = true
  clientEditVisible.value = true
}

function openEditClient(client: RadiusClient) {
  clientForm.value = { ...client }
  isNewClient.value = false
  clientEditVisible.value = true
}

async function saveClient() {
  clientEditSaving.value = true
  try {
    if (isNewClient.value) {
      await addClient(clientForm.value)
      toast.add({ severity: 'success', summary: 'Client added', life: 3000 })
    } else {
      await updateClient(clientForm.value.id, clientForm.value)
      toast.add({ severity: 'success', summary: 'Client updated', life: 3000 })
    }
    clientEditVisible.value = false
    await loadClients()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    clientEditSaving.value = false
  }
}

function confirmDeleteClient(client: RadiusClient) {
  clientDeleteTarget.value = client
  clientDeleteVisible.value = true
}

async function doDeleteClient() {
  if (!clientDeleteTarget.value) return
  clientDeleting.value = true
  try {
    await deleteClient(clientDeleteTarget.value.id)
    toast.add({ severity: 'success', summary: 'Client deleted', life: 3000 })
    clientDeleteVisible.value = false
    await loadClients()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    clientDeleting.value = false
  }
}

async function loadClients() {
  try { clients.value = await getClients() } catch { /* ignore */ }
}

// ── Log ───────────────────────────────────────────────────────
const logEntries = ref<RadiusLogEntry[]>([])

async function loadLog() {
  try { logEntries.value = await getLog() } catch { /* ignore */ }
}

// ── Lifecycle ─────────────────────────────────────────────────
onMounted(async () => {
  loading.value = true
  await Promise.all([loadSettings(), loadClients(), loadLog()])
  loading.value = false
})
</script>

<template>
  <div>
    <div class="page-header">
      <h1>RADIUS Server</h1>
      <p>Network device authentication via RADIUS protocol (RFC 2865) against the directory service.</p>
    </div>

    <Tabs v-model:value="activeTab">
      <TabList>
        <Tab value="0">Settings</Tab>
        <Tab value="1">RADIUS Clients</Tab>
        <Tab value="2">Authentication Log</Tab>
      </TabList>
      <TabPanels>
        <!-- Settings -->
        <TabPanel value="0">
          <div class="card" style="max-width: 500px">
            <h3 style="margin-top: 0">RADIUS Server Settings</h3>
            <div class="field" style="margin-bottom: 1rem">
              <label style="display: flex; align-items: center; gap: 0.5rem">
                <Checkbox v-model="settings.enabled" :binary="true" /> Enable RADIUS Server
              </label>
            </div>
            <div class="field" style="margin-bottom: 1rem">
              <label>Authentication Port</label>
              <InputNumber v-model="settings.port" :min="1" :max="65535" style="width: 100%" />
            </div>
            <div class="field" style="margin-bottom: 1rem">
              <label>Accounting Port</label>
              <InputNumber v-model="settings.accountingPort" :min="1" :max="65535" style="width: 100%" />
            </div>
            <Button label="Save Settings" icon="pi pi-save" :loading="settingsSaving" @click="saveSettings" />
          </div>
        </TabPanel>

        <!-- Clients -->
        <TabPanel value="1">
          <div class="toolbar">
            <Button label="Add Client" icon="pi pi-plus" @click="openNewClient" />
          </div>
          <DataTable :value="clients" :loading="loading" stripedRows>
            <Column field="name" header="Name" sortable />
            <Column field="ipAddress" header="IP Address" sortable />
            <Column field="description" header="Description" />
            <Column header="Status" style="width: 100px">
              <template #body="{ data }">
                <Tag :value="data.isEnabled ? 'Enabled' : 'Disabled'" :severity="data.isEnabled ? 'success' : 'danger'" />
              </template>
            </Column>
            <Column header="Actions" style="width: 130px">
              <template #body="{ data }">
                <Button icon="pi pi-pencil" text rounded @click="openEditClient(data)" class="mr-1" />
                <Button icon="pi pi-trash" text rounded severity="danger" @click="confirmDeleteClient(data)" />
              </template>
            </Column>
          </DataTable>
        </TabPanel>

        <!-- Log -->
        <TabPanel value="2">
          <div class="toolbar">
            <Button label="Refresh" icon="pi pi-refresh" text @click="loadLog" />
          </div>
          <DataTable :value="logEntries" :loading="loading" stripedRows paginator :rows="25" sortField="timestamp" :sortOrder="-1">
            <Column header="Time" field="timestamp" sortable style="width: 180px">
              <template #body="{ data }">{{ new Date(data.timestamp).toLocaleString() }}</template>
            </Column>
            <Column field="username" header="Username" sortable />
            <Column field="clientName" header="Client" sortable />
            <Column field="clientIp" header="Client IP" sortable />
            <Column header="Result" style="width: 100px">
              <template #body="{ data }">
                <Tag :value="data.success ? 'Accept' : 'Reject'" :severity="data.success ? 'success' : 'danger'" />
              </template>
            </Column>
            <Column field="reason" header="Reason" />
          </DataTable>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <!-- Client Editor Dialog -->
    <Dialog v-model:visible="clientEditVisible" :header="isNewClient ? 'Add RADIUS Client' : 'Edit RADIUS Client'" :style="{ width: '450px' }" modal>
      <div class="field" style="margin-bottom: 1rem">
        <label>Name</label>
        <InputText v-model="clientForm.name" style="width: 100%" placeholder="e.g. Core Switch" />
      </div>
      <div class="field" style="margin-bottom: 1rem">
        <label>IP Address</label>
        <InputText v-model="clientForm.ipAddress" style="width: 100%" placeholder="192.168.1.1" />
      </div>
      <div class="field" style="margin-bottom: 1rem">
        <label>Shared Secret</label>
        <InputText v-model="clientForm.sharedSecret" style="width: 100%" placeholder="Strong shared secret" />
      </div>
      <div class="field" style="margin-bottom: 1rem">
        <label>Description</label>
        <InputText v-model="clientForm.description" style="width: 100%" />
      </div>
      <div class="field" style="margin-bottom: 1rem">
        <label style="display: flex; align-items: center; gap: 0.5rem">
          <Checkbox v-model="clientForm.isEnabled" :binary="true" /> Enabled
        </label>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="clientEditVisible = false" />
        <Button :label="isNewClient ? 'Add' : 'Save'" icon="pi pi-check" :loading="clientEditSaving" @click="saveClient" />
      </template>
    </Dialog>

    <!-- Delete Client Dialog -->
    <Dialog v-model:visible="clientDeleteVisible" header="Delete Client" :style="{ width: '400px' }" modal>
      <p>Are you sure you want to delete RADIUS client <strong>{{ clientDeleteTarget?.name }}</strong>?</p>
      <template #footer>
        <Button label="Cancel" text @click="clientDeleteVisible = false" />
        <Button label="Delete" icon="pi pi-trash" severity="danger" :loading="clientDeleting" @click="doDeleteClient" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.field label {
  display: block;
  font-weight: 600;
  margin-bottom: 0.375rem;
  font-size: 0.875rem;
}
.mr-1 { margin-right: 0.25rem; }
</style>
