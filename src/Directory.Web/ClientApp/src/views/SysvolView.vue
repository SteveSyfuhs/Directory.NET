<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import ToggleSwitch from 'primevue/toggleswitch'
import Tag from 'primevue/tag'
import Message from 'primevue/message'
import Breadcrumb from 'primevue/breadcrumb'
import ProgressSpinner from 'primevue/progressspinner'
import Dialog from 'primevue/dialog'
import { useToast } from 'primevue/usetoast'
import {
  fetchSysvolRoot,
  browseSysvol,
  downloadSysvolFile,
  uploadSysvolFile,
  deleteSysvolFile,
  fetchSysvolReplicationStatus,
  fetchSysvolConflicts,
  fetchSysvolConfig,
  updateSysvolConfig,
  validateSysvolConfig,
} from '../api/sysvol'
import type { SysvolItem, SysvolReplicationStatus, SysvolConflict, SysvolConfig } from '../types/sysvol'

const toast = useToast()

const items = ref<SysvolItem[]>([])
const loading = ref(true)
const currentPath = ref('')
const replStatus = ref<SysvolReplicationStatus | null>(null)
const conflicts = ref<SysvolConflict[]>([])
const deleteDialogVisible = ref(false)
const deleteTarget = ref<SysvolItem | null>(null)

// SYSVOL share configuration
const sysvolConfig = ref<SysvolConfig>({
  sysvolSharePath: '',
  netlogonSharePath: '',
  dfsNamespace: '',
  useDfsReplication: false,
  smbServerHostname: '',
})
const configLoading = ref(true)
const configSaving = ref(false)
const configValidating = ref(false)
const configDirty = ref(false)

const isSysvolConfigured = computed(() => !!sysvolConfig.value.sysvolSharePath && !!sysvolConfig.value.smbServerHostname)

// Breadcrumb
const home = { icon: 'pi pi-folder', command: () => navigateTo('') }
const breadcrumbItems = computed(() => {
  if (!currentPath.value) return []
  const parts = currentPath.value.split('/')
  return parts.map((p, i) => ({
    label: p,
    command: () => navigateTo(parts.slice(0, i + 1).join('/')),
  }))
})

onMounted(() => {
  loadSysvolConfig()
  loadContents()
  loadReplicationInfo()
})

async function loadSysvolConfig() {
  configLoading.value = true
  try {
    sysvolConfig.value = await fetchSysvolConfig()
    configDirty.value = false
  } catch {
    // Config may not exist yet — that's fine
  } finally {
    configLoading.value = false
  }
}

function markConfigDirty() {
  configDirty.value = true
}

async function saveSysvolConfig() {
  configSaving.value = true
  try {
    sysvolConfig.value = await updateSysvolConfig(sysvolConfig.value)
    configDirty.value = false
    toast.add({ severity: 'success', summary: 'Saved', detail: 'SYSVOL configuration updated. GPO file system paths have been refreshed.', life: 5000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Save Failed', detail: e.message, life: 5000 })
  } finally {
    configSaving.value = false
  }
}

async function testSysvolConfig() {
  configValidating.value = true
  try {
    const result = await validateSysvolConfig(sysvolConfig.value)
    if (result.isValid) {
      toast.add({ severity: 'success', summary: 'Valid', detail: result.message, life: 5000 })
    } else {
      toast.add({ severity: 'error', summary: 'Validation Errors', detail: result.errors.join('; '), life: 8000 })
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Validation Failed', detail: e.message, life: 5000 })
  } finally {
    configValidating.value = false
  }
}

async function loadContents() {
  loading.value = true
  try {
    if (currentPath.value) {
      items.value = await browseSysvol(currentPath.value)
    } else {
      items.value = await fetchSysvolRoot()
    }
  } catch {
    items.value = []
  } finally {
    loading.value = false
  }
}

async function loadReplicationInfo() {
  try {
    const [status, conflictList] = await Promise.all([
      fetchSysvolReplicationStatus(),
      fetchSysvolConflicts(),
    ])
    replStatus.value = status
    conflicts.value = conflictList
  } catch {
    // May not be available
  }
}

function navigateTo(path: string) {
  currentPath.value = path
  loadContents()
}

function handleRowClick(item: SysvolItem) {
  if (item.isDirectory) {
    navigateTo(item.path)
  }
}

async function handleDownload(item: SysvolItem) {
  try {
    const blob = await downloadSysvolFile(item.path)
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = item.name
    a.click()
    URL.revokeObjectURL(url)
  } catch {
    toast.add({ severity: 'error', summary: 'Download Failed', detail: 'Could not download the file.', life: 5000 })
  }
}

function confirmDelete(item: SysvolItem) {
  deleteTarget.value = item
  deleteDialogVisible.value = true
}

async function handleDelete() {
  if (!deleteTarget.value) return
  try {
    await deleteSysvolFile(deleteTarget.value.path)
    toast.add({ severity: 'success', summary: 'Deleted', detail: `${deleteTarget.value.name} has been deleted.`, life: 3000 })
    deleteDialogVisible.value = false
    deleteTarget.value = null
    await loadContents()
    await loadReplicationInfo()
  } catch {
    toast.add({ severity: 'error', summary: 'Delete Failed', detail: 'Could not delete the file.', life: 5000 })
  }
}

async function handleUpload() {
  const input = document.createElement('input')
  input.type = 'file'
  input.onchange = async () => {
    const file = input.files?.[0]
    if (!file) return
    try {
      const targetPath = currentPath.value ? `${currentPath.value}/${file.name}` : file.name
      await uploadSysvolFile(targetPath, file)
      toast.add({ severity: 'success', summary: 'Uploaded', detail: `${file.name} has been uploaded.`, life: 3000 })
      await loadContents()
      await loadReplicationInfo()
    } catch {
      toast.add({ severity: 'error', summary: 'Upload Failed', detail: 'Could not upload the file.', life: 5000 })
    }
  }
  input.click()
}

function formatSize(bytes?: number): string {
  if (bytes == null) return ''
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1048576).toFixed(1)} MB`
}

function formatTimestamp(ts?: string): string {
  if (!ts) return ''
  return new Date(ts).toLocaleString()
}

function healthSeverity(health: string): string {
  if (health === 'Healthy') return 'success'
  if (health === 'Warning') return 'warn'
  return 'danger'
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>SYSVOL</h1>
      <p>DFS-R / SYSVOL file replication store for Group Policy templates and logon scripts</p>
    </div>

    <!-- SYSVOL Share Configuration -->
    <div class="card" style="margin-bottom: 1.5rem">
      <div class="card-title">
        <i class="pi pi-cog" style="margin-right: 0.5rem"></i>
        SYSVOL Share Configuration
      </div>

      <Message severity="info" :closable="false" style="margin-bottom: 1rem">
        Directory.NET delegates file-based Group Policy storage to a Windows SMB file server.
        Configure the UNC paths below to point to your existing SYSVOL share.
      </Message>

      <div v-if="configLoading" style="text-align: center; padding: 2rem">
        <ProgressSpinner />
      </div>
      <template v-else>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem">
          <div>
            <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">SMB Server Hostname</label>
            <InputText v-model="sysvolConfig.smbServerHostname" placeholder="dc01.contoso.com"
                       style="width: 100%" size="small" @input="markConfigDirty" />
            <small style="color: var(--p-text-muted-color)">The Windows file server hosting the SYSVOL share</small>
          </div>
          <div>
            <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">SYSVOL Share Path</label>
            <InputText v-model="sysvolConfig.sysvolSharePath" placeholder="\\dc01.contoso.com\SYSVOL"
                       style="width: 100%" size="small" @input="markConfigDirty" />
            <small style="color: var(--p-text-muted-color)">UNC path to the SYSVOL share (must start with \\)</small>
          </div>
          <div>
            <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">NETLOGON Share Path</label>
            <InputText v-model="sysvolConfig.netlogonSharePath" placeholder="\\dc01.contoso.com\NETLOGON"
                       style="width: 100%" size="small" @input="markConfigDirty" />
            <small style="color: var(--p-text-muted-color)">UNC path to the NETLOGON share</small>
          </div>
          <div>
            <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">Use DFS Replication</label>
            <div style="display: flex; align-items: center; gap: 0.75rem; margin-top: 0.25rem">
              <ToggleSwitch v-model="sysvolConfig.useDfsReplication" @change="markConfigDirty" />
              <span style="color: var(--p-text-muted-color); font-size: 0.875rem">
                {{ sysvolConfig.useDfsReplication ? 'Enabled' : 'Disabled' }}
              </span>
            </div>
          </div>
        </div>

        <div v-if="sysvolConfig.useDfsReplication" style="margin-bottom: 1rem">
          <label style="display: block; margin-bottom: 0.25rem; font-weight: 600">DFS Namespace</label>
          <InputText v-model="sysvolConfig.dfsNamespace" placeholder="\\contoso.com\DFSRoot\SYSVOL"
                     style="width: 50%" size="small" @input="markConfigDirty" />
          <small style="display: block; color: var(--p-text-muted-color)">DFS-N path for SYSVOL replication</small>
        </div>

        <div style="display: flex; gap: 0.5rem; align-items: center">
          <Button label="Save Configuration" icon="pi pi-save" size="small" @click="saveSysvolConfig"
                  :loading="configSaving" :disabled="!configDirty" />
          <Button label="Test Connection" icon="pi pi-check-circle" size="small" severity="secondary" outlined
                  @click="testSysvolConfig" :loading="configValidating" />
          <Tag v-if="isSysvolConfigured" value="Configured" severity="success" style="margin-left: 0.5rem" />
          <Tag v-else value="Not Configured" severity="warn" style="margin-left: 0.5rem" />
        </div>
      </template>
    </div>

    <!-- Replication status cards -->
    <div class="stat-grid">
      <div class="stat-card">
        <div class="stat-icon blue"><i class="pi pi-file"></i></div>
        <div>
          <div class="stat-value">{{ replStatus?.totalFiles ?? 0 }}</div>
          <div class="stat-label">Total Files</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon green"><i class="pi pi-cloud"></i></div>
        <div>
          <div class="stat-value">{{ formatSize(replStatus?.totalSizeBytes) || '0 B' }}</div>
          <div class="stat-label">Total Size</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon purple"><i class="pi pi-clock"></i></div>
        <div>
          <div class="stat-value" style="font-size: 1rem">{{ replStatus ? formatTimestamp(replStatus.lastReplicationTime) : 'N/A' }}</div>
          <div class="stat-label">Last Replication</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon" :class="replStatus?.replicationHealth === 'Healthy' ? 'green' : replStatus?.replicationHealth === 'Warning' ? 'amber' : 'purple'">
          <i class="pi" :class="replStatus?.replicationHealth === 'Healthy' ? 'pi-check-circle' : 'pi-exclamation-triangle'"></i>
        </div>
        <div>
          <Tag :value="replStatus?.replicationHealth || 'Unknown'" :severity="healthSeverity(replStatus?.replicationHealth || '')" />
          <div class="stat-label">Replication Health</div>
        </div>
      </div>
    </div>

    <!-- Conflicts panel -->
    <div v-if="conflicts.length > 0" class="card" style="margin-bottom: 1rem; border-color: var(--app-warn-border); background: var(--app-warn-bg)">
      <div class="card-title" style="color: var(--app-warn-text-strong)">
        <i class="pi pi-exclamation-triangle" style="margin-right: 0.5rem"></i>
        Replication Conflicts ({{ conflicts.length }})
      </div>
      <DataTable :value="conflicts" size="small" stripedRows>
        <Column field="path" header="Path" style="min-width: 200px" />
        <Column field="localVersion" header="Local Version" style="width: 120px" />
        <Column field="remoteVersion" header="Remote Version" style="width: 130px" />
        <Column field="detectedAt" header="Detected" style="width: 180px">
          <template #body="{ data }">{{ formatTimestamp(data.detectedAt) }}</template>
        </Column>
      </DataTable>
    </div>

    <!-- Breadcrumb + toolbar -->
    <div class="card">
      <div class="toolbar">
        <Breadcrumb :home="home" :model="breadcrumbItems" />
        <div class="toolbar-spacer" />
        <Button label="Upload File" icon="pi pi-upload" size="small" @click="handleUpload" />
        <Button label="Refresh" icon="pi pi-refresh" severity="secondary" size="small" @click="loadContents" />
      </div>

      <!-- File table -->
      <div v-if="loading" style="text-align: center; padding: 3rem">
        <ProgressSpinner />
      </div>
      <DataTable
        v-else
        :value="items"
        stripedRows
        size="small"
        scrollable
        scrollHeight="calc(100vh - 520px)"
        @row-click="({ data }) => handleRowClick(data)"
        :rowClass="(data: SysvolItem) => data.isDirectory ? 'cursor-pointer' : ''"
      >
        <Column header="" style="width: 40px">
          <template #body="{ data }">
            <i :class="data.isDirectory ? 'pi pi-folder' : 'pi pi-file'" style="color: var(--p-text-muted-color)"></i>
          </template>
        </Column>
        <Column field="name" header="Name" sortable style="min-width: 250px">
          <template #body="{ data }">
            <span :style="data.isDirectory ? 'font-weight: 600; cursor: pointer' : 'font-family: monospace; font-size: 0.8125rem'">
              {{ data.name }}
            </span>
          </template>
        </Column>
        <Column header="Size" sortable sortField="size" style="width: 120px">
          <template #body="{ data }">
            <span style="color: var(--p-text-muted-color); font-size: 0.8125rem">{{ data.isDirectory ? '' : formatSize(data.size) }}</span>
          </template>
        </Column>
        <Column header="Modified" sortable sortField="lastModified" style="width: 200px">
          <template #body="{ data }">
            <span style="font-size: 0.8125rem">{{ formatTimestamp(data.lastModified) }}</span>
          </template>
        </Column>
        <Column field="version" header="Version" sortable style="width: 90px" />
        <Column header="Actions" style="width: 140px">
          <template #body="{ data }">
            <div v-if="!data.isDirectory" style="display: flex; gap: 0.25rem">
              <Button icon="pi pi-download" text rounded size="small" @click.stop="handleDownload(data)" v-tooltip="'Download'" />
              <Button icon="pi pi-trash" text rounded size="small" severity="danger" @click.stop="confirmDelete(data)" v-tooltip="'Delete'" />
            </div>
          </template>
        </Column>
        <template #empty>
          <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">No files found in this directory</div>
        </template>
      </DataTable>
    </div>

    <!-- Delete confirmation dialog -->
    <Dialog v-model:visible="deleteDialogVisible" header="Confirm Delete" :modal="true" :style="{ width: '400px' }">
      <p>Are you sure you want to delete <strong>{{ deleteTarget?.name }}</strong>?</p>
      <p style="color: var(--p-text-muted-color); font-size: 0.875rem">This action will soft-delete the file. It can be recovered through replication.</p>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="deleteDialogVisible = false" />
        <Button label="Delete" severity="danger" icon="pi pi-trash" @click="handleDelete" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.cursor-pointer {
  cursor: pointer;
}
</style>
