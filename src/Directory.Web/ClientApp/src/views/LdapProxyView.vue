<script setup lang="ts">
import { ref, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import InputSwitch from 'primevue/inputswitch'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import type { LdapProxyBackend, ProxyRoute, ProxyMode, AttributeMapping } from '../types/ldapProxy'
import {
  fetchProxyBackends,
  createProxyBackend,
  updateProxyBackend,
  deleteProxyBackend,
  testProxyBackend,
  fetchProxyRoutes,
  createProxyRoute,
  updateProxyRoute,
  deleteProxyRoute,
} from '../api/ldapProxy'

const toast = useToast()
const loading = ref(false)
const backends = ref<LdapProxyBackend[]>([])
const routes = ref<ProxyRoute[]>([])

// Backend dialog
const showBackendDialog = ref(false)
const editingBackend = ref<Partial<LdapProxyBackend>>({})
const isNewBackend = ref(false)

// Route dialog
const showRouteDialog = ref(false)
const editingRoute = ref<Partial<ProxyRoute>>({})
const isNewRoute = ref(false)

const proxyModeOptions = [
  { label: 'Pass-Through', value: 'PassThrough' },
  { label: 'Read Only', value: 'ReadOnly' },
  { label: 'Write-Through', value: 'WriteThrough' },
  { label: 'Cache', value: 'Cache' },
]

onMounted(async () => {
  await Promise.all([loadBackends(), loadRoutes()])
})

async function loadBackends() {
  loading.value = true
  try {
    backends.value = await fetchProxyBackends()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function loadRoutes() {
  try {
    routes.value = await fetchProxyRoutes()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function openAddBackend() {
  isNewBackend.value = true
  editingBackend.value = { port: 389, useSsl: false, isEnabled: true, priority: 0, timeoutMs: 5000, attributeMappings: [] }
  showBackendDialog.value = true
}

function openEditBackend(backend: LdapProxyBackend) {
  isNewBackend.value = false
  editingBackend.value = { ...backend, attributeMappings: [...(backend.attributeMappings || [])] }
  showBackendDialog.value = true
}

async function saveBackend() {
  try {
    if (isNewBackend.value) {
      await createProxyBackend(editingBackend.value)
      toast.add({ severity: 'success', summary: 'Backend Created', life: 3000 })
    } else {
      await updateProxyBackend(editingBackend.value.id!, editingBackend.value)
      toast.add({ severity: 'success', summary: 'Backend Updated', life: 3000 })
    }
    showBackendDialog.value = false
    await loadBackends()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function removeBackend(backend: LdapProxyBackend) {
  try {
    await deleteProxyBackend(backend.id)
    toast.add({ severity: 'success', summary: 'Backend Deleted', life: 3000 })
    await Promise.all([loadBackends(), loadRoutes()])
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function testBackend(backend: LdapProxyBackend) {
  try {
    const result = await testProxyBackend(backend.id)
    if (result.success) {
      toast.add({ severity: 'success', summary: 'Connection OK', detail: `${result.message} (${result.latencyMs}ms)`, life: 5000 })
    } else {
      toast.add({ severity: 'error', summary: 'Connection Failed', detail: result.message, life: 5000 })
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function addAttrMapping() {
  if (!editingBackend.value.attributeMappings) editingBackend.value.attributeMappings = []
  editingBackend.value.attributeMappings.push({ localName: '', remoteName: '' })
}

function removeAttrMapping(index: number) {
  editingBackend.value.attributeMappings?.splice(index, 1)
}

function openAddRoute() {
  isNewRoute.value = true
  editingRoute.value = { mode: 'PassThrough' as ProxyMode }
  showRouteDialog.value = true
}

function openEditRoute(route: ProxyRoute) {
  isNewRoute.value = false
  editingRoute.value = { ...route }
  showRouteDialog.value = true
}

async function saveRoute() {
  try {
    if (isNewRoute.value) {
      await createProxyRoute(editingRoute.value)
      toast.add({ severity: 'success', summary: 'Route Created', life: 3000 })
    } else {
      await updateProxyRoute(editingRoute.value.id!, editingRoute.value)
      toast.add({ severity: 'success', summary: 'Route Updated', life: 3000 })
    }
    showRouteDialog.value = false
    await loadRoutes()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function removeRoute(route: ProxyRoute) {
  try {
    await deleteProxyRoute(route.id)
    toast.add({ severity: 'success', summary: 'Route Deleted', life: 3000 })
    await loadRoutes()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function backendName(backendId: string) {
  return backends.value.find(b => b.id === backendId)?.name ?? backendId
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>LDAP Proxy / Virtual Directory</h1>
      <p>Route LDAP queries to backend servers with attribute mapping and caching.</p>
    </div>

    <!-- Backends -->
    <div class="card" style="margin-bottom: 1.5rem">
      <div class="toolbar">
        <span class="card-title" style="margin-bottom: 0">Backends</span>
        <span class="toolbar-spacer"></span>
        <Button label="Add Backend" icon="pi pi-plus" size="small" @click="openAddBackend" />
      </div>
      <DataTable :value="backends" :loading="loading" size="small" stripedRows>
        <Column field="name" header="Name" />
        <Column field="host" header="Host" />
        <Column field="port" header="Port" style="width: 80px" />
        <Column field="baseDn" header="Base DN" />
        <Column field="priority" header="Priority" style="width: 80px" />
        <Column field="isEnabled" header="Enabled" style="width: 80px">
          <template #body="{ data }">
            <Tag :value="data.isEnabled ? 'Yes' : 'No'" :severity="data.isEnabled ? 'success' : 'secondary'" />
          </template>
        </Column>
        <Column header="Actions" style="width: 180px">
          <template #body="{ data }">
            <Button icon="pi pi-bolt" size="small" text rounded v-tooltip="'Test'" @click="testBackend(data)" />
            <Button icon="pi pi-pencil" size="small" text rounded v-tooltip="'Edit'" @click="openEditBackend(data)" />
            <Button icon="pi pi-trash" size="small" text rounded severity="danger" v-tooltip="'Delete'" @click="removeBackend(data)" />
          </template>
        </Column>
      </DataTable>
    </div>

    <!-- Routes -->
    <div class="card">
      <div class="toolbar">
        <span class="card-title" style="margin-bottom: 0">Routes</span>
        <span class="toolbar-spacer"></span>
        <Button label="Add Route" icon="pi pi-plus" size="small" @click="openAddRoute" />
      </div>
      <DataTable :value="routes" size="small" stripedRows>
        <Column field="baseDn" header="Base DN" />
        <Column field="backendId" header="Backend">
          <template #body="{ data }">{{ backendName(data.backendId) }}</template>
        </Column>
        <Column field="mode" header="Mode">
          <template #body="{ data }">
            <Tag :value="data.mode" />
          </template>
        </Column>
        <Column header="Actions" style="width: 120px">
          <template #body="{ data }">
            <Button icon="pi pi-pencil" size="small" text rounded v-tooltip="'Edit'" @click="openEditRoute(data)" />
            <Button icon="pi pi-trash" size="small" text rounded severity="danger" v-tooltip="'Delete'" @click="removeRoute(data)" />
          </template>
        </Column>
      </DataTable>
    </div>

    <!-- Backend Dialog -->
    <Dialog v-model:visible="showBackendDialog" :header="isNewBackend ? 'Add Backend' : 'Edit Backend'" :style="{ width: '600px' }" modal>
      <div style="display: flex; flex-direction: column; gap: 1rem">
        <div>
          <label>Name</label>
          <InputText v-model="editingBackend.name" style="width: 100%" />
        </div>
        <div style="display: flex; gap: 1rem">
          <div style="flex: 1"><label>Host</label><InputText v-model="editingBackend.host" style="width: 100%" /></div>
          <div style="width: 100px"><label>Port</label><InputNumber v-model="editingBackend.port" style="width: 100%" /></div>
        </div>
        <div><label>Base DN</label><InputText v-model="editingBackend.baseDn" style="width: 100%" /></div>
        <div style="display: flex; gap: 1rem">
          <div style="flex: 1"><label>Bind DN</label><InputText v-model="editingBackend.bindDn" style="width: 100%" /></div>
          <div style="flex: 1"><label>Bind Password</label><InputText v-model="editingBackend.bindPassword" type="password" style="width: 100%" /></div>
        </div>
        <div style="display: flex; gap: 1rem; align-items: center">
          <label style="display: flex; align-items: center; gap: 0.5rem"><InputSwitch v-model="editingBackend.useSsl" /> SSL/TLS</label>
          <label style="display: flex; align-items: center; gap: 0.5rem"><InputSwitch v-model="editingBackend.isEnabled" /> Enabled</label>
          <div><label>Priority</label><InputNumber v-model="editingBackend.priority" style="width: 80px" /></div>
          <div><label>Timeout (ms)</label><InputNumber v-model="editingBackend.timeoutMs" style="width: 100px" /></div>
        </div>
        <div>
          <div style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem">
            <strong>Attribute Mappings</strong>
            <Button icon="pi pi-plus" size="small" text rounded @click="addAttrMapping" />
          </div>
          <div v-for="(m, i) in editingBackend.attributeMappings" :key="i" style="display: flex; gap: 0.5rem; margin-bottom: 0.25rem; align-items: center">
            <InputText v-model="m.localName" placeholder="Local name" style="flex: 1" />
            <i class="pi pi-arrow-right" />
            <InputText v-model="m.remoteName" placeholder="Remote name" style="flex: 1" />
            <InputText v-model="m.transformExpression" placeholder="Transform" style="width: 120px" />
            <Button icon="pi pi-trash" size="small" text severity="danger" @click="removeAttrMapping(i)" />
          </div>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="showBackendDialog = false" />
        <Button label="Save" icon="pi pi-check" @click="saveBackend" />
      </template>
    </Dialog>

    <!-- Route Dialog -->
    <Dialog v-model:visible="showRouteDialog" :header="isNewRoute ? 'Add Route' : 'Edit Route'" :style="{ width: '450px' }" modal>
      <div style="display: flex; flex-direction: column; gap: 1rem">
        <div><label>Base DN</label><InputText v-model="editingRoute.baseDn" style="width: 100%" placeholder="OU=External,DC=example,DC=com" /></div>
        <div>
          <label>Backend</label>
          <Select v-model="editingRoute.backendId" :options="backends" optionLabel="name" optionValue="id" style="width: 100%" placeholder="Select backend" />
        </div>
        <div>
          <label>Mode</label>
          <Select v-model="editingRoute.mode" :options="proxyModeOptions" optionLabel="label" optionValue="value" style="width: 100%" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="showRouteDialog = false" />
        <Button label="Save" icon="pi pi-check" @click="saveRoute" />
      </template>
    </Dialog>
  </div>
</template>
