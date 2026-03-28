<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Panel from 'primevue/panel'
import Tree from 'primevue/tree'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import {
  listSites, listSiteServers, listSiteSubnets,
  createSite, updateSite, deleteSite,
  listAllSubnets, createSubnet, deleteSubnet,
  listSiteLinks, createSiteLink, updateSiteLink, deleteSiteLink,
  listSiteLinkBridges, createSiteLinkBridge, deleteSiteLinkBridge,
  listReplicationConnections, createReplicationConnection, deleteReplicationConnection,
  triggerKcc, moveServer, listTransports,
} from '../api/admin'

const toast = useToast()
const loading = ref(true)
const detailLoading = ref(false)

// Data
const sites = ref<any[]>([])
const allSubnets = ref<any[]>([])
const siteLinks = ref<any[]>([])
const siteLinkBridges = ref<any[]>([])
const transports = ref<any[]>([])
const servers = ref<any[]>([])
const subnets = ref<any[]>([])
const connections = ref<any[]>([])

// Selection
const selectedNode = ref<any>(null)
const selectedType = ref<string>('')
const selectedData = ref<any>(null)

// Dialogs
const showSiteDialog = ref(false)
const showSubnetDialog = ref(false)
const showSiteLinkDialog = ref(false)
const showBridgeDialog = ref(false)
const showConnectionDialog = ref(false)
const showMoveDialog = ref(false)
const saving = ref(false)

// Forms
const siteForm = ref({ name: '', description: '' })
const subnetForm = ref({ subnetAddress: '', siteDn: '', description: '', location: '' })
const siteLinkForm = ref({ name: '', sites: [] as string[], cost: 100, replInterval: 180, description: '' })
const siteLinkIsNew = ref(true)
const bridgeForm = ref({ name: '', siteLinks: [] as string[] })
const connectionForm = ref({ fromServer: '', name: '', transportType: 'IP' })
const connectionContext = ref({ siteName: '', serverName: '' })
const moveForm = ref({ targetSite: '', serverName: '', siteName: '' })

// Tree
const treeNodes = computed(() => {
  const siteNodes = sites.value.map(s => ({
    key: `site-${s.name}`,
    label: s.name,
    icon: 'pi pi-globe',
    type: 'site',
    data: s,
    leaf: false,
    children: [],
  }))

  const subnetNode = {
    key: 'subnets-root',
    label: `Subnets (${allSubnets.value.length})`,
    icon: 'pi pi-sitemap',
    type: 'subnets-folder',
    data: null,
    leaf: allSubnets.value.length === 0,
    children: allSubnets.value.map(sub => ({
      key: `subnet-${sub.objectGuid}`,
      label: sub.name,
      icon: 'pi pi-sitemap',
      type: 'subnet',
      data: sub,
      leaf: true,
    })),
  }

  const siteLinkNodes = siteLinks.value.map(sl => ({
    key: `sitelink-${sl.name}`,
    label: sl.name,
    icon: 'pi pi-arrows-h',
    type: 'sitelink',
    data: sl,
    leaf: true,
  }))

  const transportNode = {
    key: 'transports-root',
    label: 'Inter-Site Transports',
    icon: 'pi pi-share-alt',
    type: 'transports-folder',
    data: null,
    leaf: false,
    children: [
      {
        key: 'transport-ip',
        label: 'IP',
        icon: 'pi pi-wifi',
        type: 'transport',
        data: { name: 'IP' },
        leaf: false,
        children: siteLinkNodes,
      },
    ],
  }

  return [
    {
      key: 'sites-root',
      label: `Sites (${sites.value.length})`,
      icon: 'pi pi-globe',
      type: 'sites-folder',
      data: null,
      leaf: false,
      children: siteNodes,
    },
    subnetNode,
    transportNode,
  ]
})

onMounted(() => loadAll())

async function loadAll() {
  loading.value = true
  try {
    const [s, sub, sl, slb, tr] = await Promise.all([
      listSites(),
      listAllSubnets(),
      listSiteLinks(),
      listSiteLinkBridges(),
      listTransports(),
    ])
    sites.value = s
    allSubnets.value = sub
    siteLinks.value = sl
    siteLinkBridges.value = slb
    transports.value = tr
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function onNodeSelect(node: any) {
  const n = node
  selectedType.value = n.type
  selectedData.value = n.data

  if (n.type === 'site') {
    detailLoading.value = true
    try {
      const [svrs, subs] = await Promise.all([
        listSiteServers(n.data.name),
        listSiteSubnets(n.data.name),
      ])
      servers.value = svrs
      subnets.value = subs
    } catch (e: any) {
      toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
    } finally {
      detailLoading.value = false
    }
  }
}

async function loadConnections(siteName: string, serverName: string) {
  try {
    connections.value = await listReplicationConnections(siteName, serverName)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

// Site CRUD
function openCreateSite() {
  siteForm.value = { name: '', description: '' }
  showSiteDialog.value = true
}

async function submitSite() {
  if (!siteForm.value.name.trim()) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Site name is required', life: 3000 })
    return
  }
  saving.value = true
  try {
    await createSite({ name: siteForm.value.name.trim(), description: siteForm.value.description || undefined })
    toast.add({ severity: 'success', summary: 'Created', detail: `Site '${siteForm.value.name}' created`, life: 3000 })
    showSiteDialog.value = false
    await loadAll()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function confirmDeleteSite(name: string) {
  if (!confirm(`Delete site '${name}'? This will also remove associated servers container.`)) return
  try {
    await deleteSite(name)
    toast.add({ severity: 'success', summary: 'Deleted', detail: `Site '${name}' deleted`, life: 3000 })
    selectedType.value = ''
    selectedData.value = null
    await loadAll()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

// Subnet CRUD
function openCreateSubnet() {
  const siteDn = selectedType.value === 'site' && selectedData.value ? selectedData.value.distinguishedName : ''
  subnetForm.value = { subnetAddress: '', siteDn, description: '', location: '' }
  showSubnetDialog.value = true
}

async function submitSubnet() {
  if (!subnetForm.value.subnetAddress.trim()) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Subnet address is required (e.g., 10.0.0.0/24)', life: 3000 })
    return
  }
  saving.value = true
  try {
    await createSubnet({
      subnetAddress: subnetForm.value.subnetAddress.trim(),
      siteDn: subnetForm.value.siteDn || undefined,
      description: subnetForm.value.description || undefined,
      location: subnetForm.value.location || undefined,
    })
    toast.add({ severity: 'success', summary: 'Created', detail: 'Subnet created', life: 3000 })
    showSubnetDialog.value = false
    await loadAll()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function confirmDeleteSubnet(id: string) {
  if (!confirm('Delete this subnet?')) return
  try {
    await deleteSubnet(id)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'Subnet deleted', life: 3000 })
    await loadAll()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

// Site Link CRUD
function openCreateSiteLink() {
  siteLinkIsNew.value = true
  siteLinkForm.value = { name: '', sites: [], cost: 100, replInterval: 180, description: '' }
  showSiteLinkDialog.value = true
}

function openEditSiteLink(sl: any) {
  siteLinkIsNew.value = false
  siteLinkForm.value = {
    name: sl.name,
    sites: [...(sl.sites || [])],
    cost: sl.cost ?? 100,
    replInterval: sl.replInterval ?? 180,
    description: sl.description || '',
  }
  showSiteLinkDialog.value = true
}

async function submitSiteLink() {
  if (!siteLinkForm.value.name.trim()) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Name is required', life: 3000 })
    return
  }
  saving.value = true
  try {
    if (siteLinkIsNew.value) {
      await createSiteLink({
        name: siteLinkForm.value.name.trim(),
        sites: siteLinkForm.value.sites,
        cost: siteLinkForm.value.cost,
        replInterval: siteLinkForm.value.replInterval,
        description: siteLinkForm.value.description || undefined,
      })
      toast.add({ severity: 'success', summary: 'Created', detail: 'Site link created', life: 3000 })
    } else {
      await updateSiteLink(siteLinkForm.value.name, {
        sites: siteLinkForm.value.sites,
        cost: siteLinkForm.value.cost,
        replInterval: siteLinkForm.value.replInterval,
        description: siteLinkForm.value.description,
      })
      toast.add({ severity: 'success', summary: 'Updated', detail: 'Site link updated', life: 3000 })
    }
    showSiteLinkDialog.value = false
    await loadAll()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function confirmDeleteSiteLink(name: string) {
  if (!confirm(`Delete site link '${name}'?`)) return
  try {
    await deleteSiteLink(name)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'Site link deleted', life: 3000 })
    await loadAll()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

// Bridge CRUD
function openCreateBridge() {
  bridgeForm.value = { name: '', siteLinks: [] }
  showBridgeDialog.value = true
}

async function submitBridge() {
  if (!bridgeForm.value.name.trim()) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Name is required', life: 3000 })
    return
  }
  saving.value = true
  try {
    await createSiteLinkBridge({ name: bridgeForm.value.name.trim(), siteLinks: bridgeForm.value.siteLinks })
    toast.add({ severity: 'success', summary: 'Created', detail: 'Bridge created', life: 3000 })
    showBridgeDialog.value = false
    await loadAll()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

// Connection CRUD
function openCreateConnection(siteName: string, serverName: string) {
  connectionContext.value = { siteName, serverName }
  connectionForm.value = { fromServer: '', name: '', transportType: 'IP' }
  showConnectionDialog.value = true
}

async function submitConnection() {
  if (!connectionForm.value.fromServer.trim()) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'From Server DN is required', life: 3000 })
    return
  }
  saving.value = true
  try {
    await createReplicationConnection(
      connectionContext.value.siteName,
      connectionContext.value.serverName,
      {
        fromServer: connectionForm.value.fromServer.trim(),
        name: connectionForm.value.name || undefined,
        transportType: connectionForm.value.transportType || undefined,
      },
    )
    toast.add({ severity: 'success', summary: 'Created', detail: 'Replication connection created', life: 3000 })
    showConnectionDialog.value = false
    await loadConnections(connectionContext.value.siteName, connectionContext.value.serverName)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function confirmDeleteConnection(siteName: string, serverName: string, connName: string) {
  if (!confirm('Delete this replication connection?')) return
  try {
    await deleteReplicationConnection(siteName, serverName, connName)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'Connection deleted', life: 3000 })
    await loadConnections(siteName, serverName)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

// KCC
async function onTriggerKcc(siteName: string, serverName: string) {
  try {
    const result = await triggerKcc(siteName, serverName)
    toast.add({ severity: 'success', summary: 'KCC Triggered', detail: result.message, life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

// Move server
function openMoveServer(server: any, siteName: string) {
  moveForm.value = { targetSite: '', serverName: server.name, siteName }
  showMoveDialog.value = true
}

async function submitMove() {
  if (!moveForm.value.targetSite.trim()) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Target site is required', life: 3000 })
    return
  }
  saving.value = true
  try {
    await moveServer(moveForm.value.siteName, moveForm.value.serverName, moveForm.value.targetSite.trim())
    toast.add({ severity: 'success', summary: 'Moved', detail: `Server moved to ${moveForm.value.targetSite}`, life: 3000 })
    showMoveDialog.value = false
    await loadAll()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

function cnFromDn(dn: string): string {
  const match = dn.match(/^(?:CN|OU)=([^,]+)/i)
  return match ? match[1] : dn
}

function addSiteToLink() {
  siteLinkForm.value.sites.push('')
}

function removeSiteFromLink(index: number) {
  siteLinkForm.value.sites.splice(index, 1)
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Sites & Services</h1>
      <p>Manage AD sites, subnets, site links, and domain controller topology</p>
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">Sites and Services controls network topology for Active Directory replication. Define sites to represent physical locations, assign subnets to map IP ranges to sites, and configure site links to control replication cost and frequency between domain controllers.</p>
    </div>

    <div class="toolbar">
      <Button label="New Site" icon="pi pi-plus" @click="openCreateSite" />
      <Button label="New Subnet" icon="pi pi-plus" severity="secondary" @click="openCreateSubnet" />
      <Button label="New Site Link" icon="pi pi-plus" severity="secondary" @click="openCreateSiteLink" />
      <div class="toolbar-spacer" />
      <Button label="Refresh" icon="pi pi-refresh" severity="secondary" @click="loadAll" />
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else style="display: grid; grid-template-columns: 320px 1fr; gap: 1rem; min-height: 500px">
      <!-- Left: Tree -->
      <div class="card" style="padding: 0.5rem; overflow-y: auto">
        <Tree :value="treeNodes" selectionMode="single"
              @node-select="onNodeSelect"
              style="border: none; padding: 0" />
      </div>

      <!-- Right: Detail panel -->
      <div>
        <div v-if="!selectedType" class="card" style="text-align: center; padding: 3rem; color: var(--p-text-muted-color)">
          <i class="pi pi-globe" style="font-size: 2rem; margin-bottom: 0.5rem; display: block; opacity: 0.4"></i>
          Select an item from the tree to view details.
          <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0.75rem 0 0 0">Use the toolbar to create sites, subnets, or site links. Select an existing item in the tree to view its properties, manage domain controllers, or configure replication connections.</p>
        </div>

        <div v-else-if="detailLoading" style="text-align: center; padding: 4rem">
          <ProgressSpinner />
        </div>

        <!-- Site detail -->
        <div v-else-if="selectedType === 'site' && selectedData">
          <Panel :header="'Site: ' + selectedData.name" class="card" style="margin-bottom: 1rem">
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; font-size: 0.875rem; margin-bottom: 1rem">
              <div>
                <div style="color: var(--p-text-muted-color)">Description</div>
                <div style="font-weight: 600">{{ selectedData.description || '-' }}</div>
              </div>
              <div>
                <div style="color: var(--p-text-muted-color)">DN</div>
                <div style="font-weight: 600; word-break: break-all; font-size: 0.8125rem">{{ selectedData.distinguishedName }}</div>
              </div>
            </div>
            <Button label="Delete Site" icon="pi pi-trash" severity="danger" size="small"
                    @click="confirmDeleteSite(selectedData.name)" />
          </Panel>

          <Panel header="Domain Controllers" class="card" style="margin-bottom: 1rem">
            <DataTable :value="servers" size="small" stripedRows>
              <Column field="name" header="Server" />
              <Column field="dnsHostName" header="DNS Host" />
              <Column header="Actions" style="width: 14rem">
                <template #body="{ data: svr }">
                  <div style="display: flex; gap: 0.25rem">
                    <Button icon="pi pi-sync" label="KCC" severity="info" text size="small"
                            @click="onTriggerKcc(selectedData.name, svr.name)" title="Trigger KCC" />
                    <Button icon="pi pi-link" label="Connections" severity="secondary" text size="small"
                            @click="loadConnections(selectedData.name, svr.name)" />
                    <Button icon="pi pi-arrow-right" severity="secondary" text size="small"
                            @click="openMoveServer(svr, selectedData.name)" title="Move to another site" />
                  </div>
                </template>
              </Column>
              <template #empty>
                <div style="text-align: center; padding: 1rem; color: var(--p-text-muted-color)">No domain controllers in this site. DCs appear here when they are registered in Active Directory and assigned to this site.</div>
              </template>
            </DataTable>
          </Panel>

          <!-- Replication Connections (loaded on demand) -->
          <Panel v-if="connections.length > 0" header="Replication Connections" class="card" style="margin-bottom: 1rem">
            <div style="margin-bottom: 0.5rem">
              <Button label="New Connection" icon="pi pi-plus" size="small"
                      @click="openCreateConnection(selectedData.name, servers[0]?.name)" />
            </div>
            <DataTable :value="connections" size="small" stripedRows>
              <Column field="name" header="Name" />
              <Column header="From Server">
                <template #body="{ data }">
                  <span style="font-size: 0.8125rem">{{ cnFromDn(data.fromServer) }}</span>
                </template>
              </Column>
              <Column field="transportType" header="Transport" style="width: 6rem" />
              <Column header="Enabled" style="width: 5rem">
                <template #body="{ data }">
                  <Tag :value="data.enabled ? 'Yes' : 'No'" :severity="data.enabled ? 'success' : 'danger'" />
                </template>
              </Column>
              <Column header="" style="width: 3rem">
                <template #body="{ data }">
                  <Button icon="pi pi-trash" severity="danger" text size="small"
                          @click="confirmDeleteConnection(selectedData.name, servers[0]?.name, data.objectGuid)" />
                </template>
              </Column>
            </DataTable>
          </Panel>

          <Panel header="Subnets" class="card">
            <DataTable :value="subnets" size="small" stripedRows>
              <Column field="name" header="Subnet" />
              <Column field="description" header="Description" />
              <template #empty>
                <div style="text-align: center; padding: 1rem; color: var(--p-text-muted-color)">No subnets assigned to this site. Assign subnets so that clients in those IP ranges can locate the nearest domain controller.</div>
              </template>
            </DataTable>
          </Panel>
        </div>

        <!-- Subnet detail -->
        <div v-else-if="selectedType === 'subnet' && selectedData">
          <Panel :header="'Subnet: ' + selectedData.name" class="card">
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; font-size: 0.875rem; margin-bottom: 1rem">
              <div>
                <div style="color: var(--p-text-muted-color)">Site</div>
                <div style="font-weight: 600">{{ selectedData.siteObject ? cnFromDn(selectedData.siteObject) : 'Unassigned' }}</div>
              </div>
              <div>
                <div style="color: var(--p-text-muted-color)">Description</div>
                <div style="font-weight: 600">{{ selectedData.description || '-' }}</div>
              </div>
              <div>
                <div style="color: var(--p-text-muted-color)">Location</div>
                <div style="font-weight: 600">{{ selectedData.location || '-' }}</div>
              </div>
            </div>
            <Button label="Delete Subnet" icon="pi pi-trash" severity="danger" size="small"
                    @click="confirmDeleteSubnet(selectedData.objectGuid)" />
          </Panel>
        </div>

        <!-- Site Link detail -->
        <div v-else-if="selectedType === 'sitelink' && selectedData">
          <Panel :header="'Site Link: ' + selectedData.name" class="card">
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; font-size: 0.875rem; margin-bottom: 1rem">
              <div>
                <div style="color: var(--p-text-muted-color)">Cost</div>
                <div style="font-weight: 600">{{ selectedData.cost }}</div>
              </div>
              <div>
                <div style="color: var(--p-text-muted-color)">Replication Interval (min)</div>
                <div style="font-weight: 600">{{ selectedData.replInterval }}</div>
              </div>
              <div>
                <div style="color: var(--p-text-muted-color)">Description</div>
                <div style="font-weight: 600">{{ selectedData.description || '-' }}</div>
              </div>
              <div>
                <div style="color: var(--p-text-muted-color)">Member Sites</div>
                <div style="font-weight: 600">
                  <div v-for="s in (selectedData.sites || [])" :key="s" style="font-size: 0.8125rem">{{ cnFromDn(s) }}</div>
                  <span v-if="!selectedData.sites?.length">None</span>
                </div>
              </div>
            </div>
            <div style="display: flex; gap: 0.5rem">
              <Button label="Edit" icon="pi pi-pencil" severity="info" size="small" @click="openEditSiteLink(selectedData)" />
              <Button label="Delete" icon="pi pi-trash" severity="danger" size="small" @click="confirmDeleteSiteLink(selectedData.name)" />
            </div>
          </Panel>
        </div>

        <!-- Default for folders -->
        <div v-else class="card" style="padding: 2rem">
          <div style="color: var(--p-text-muted-color); text-align: center">
            Select a specific item for details, or use the toolbar to create new objects.
          </div>
        </div>
      </div>
    </div>

    <!-- Create Site Dialog -->
    <Dialog v-model:visible="showSiteDialog" header="Create Site" modal :style="{ width: '28rem' }" :closable="!saving">
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">A site represents a physical network location (e.g., a branch office or datacenter). Domain controllers and subnets are assigned to sites to optimize replication and client authentication.</p>
      <div style="display: flex; flex-direction: column; gap: 1rem; padding-top: 0.5rem">
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Site Name*</label>
          <InputText v-model="siteForm.name" style="width: 100%" :disabled="saving" placeholder="e.g. Default-First-Site-Name" />
        </div>
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Description</label>
          <InputText v-model="siteForm.description" style="width: 100%" :disabled="saving" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showSiteDialog = false" :disabled="saving" />
        <Button label="Create" icon="pi pi-plus" @click="submitSite" :loading="saving" />
      </template>
    </Dialog>

    <!-- Create Subnet Dialog -->
    <Dialog v-model:visible="showSubnetDialog" header="Create Subnet" modal :style="{ width: '28rem' }" :closable="!saving">
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">Subnets map IP address ranges to sites. When a client authenticates, AD uses its IP address to determine which site it belongs to, directing it to the closest domain controller.</p>
      <div style="display: flex; flex-direction: column; gap: 1rem; padding-top: 0.5rem">
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Subnet Address*</label>
          <InputText v-model="subnetForm.subnetAddress" style="width: 100%" :disabled="saving" placeholder="e.g. 10.0.0.0/24" />
        </div>
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Site DN</label>
          <InputText v-model="subnetForm.siteDn" style="width: 100%" :disabled="saving"
                     placeholder="CN=Default-First-Site-Name,CN=Sites,..." />
        </div>
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Description</label>
          <InputText v-model="subnetForm.description" style="width: 100%" :disabled="saving" />
        </div>
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Location</label>
          <InputText v-model="subnetForm.location" style="width: 100%" :disabled="saving" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showSubnetDialog = false" :disabled="saving" />
        <Button label="Create" icon="pi pi-plus" @click="submitSubnet" :loading="saving" />
      </template>
    </Dialog>

    <!-- Create/Edit Site Link Dialog -->
    <Dialog v-model:visible="showSiteLinkDialog" :header="siteLinkIsNew ? 'Create Site Link' : 'Edit Site Link'" modal
            :style="{ width: '32rem' }" :closable="!saving">
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">Site links define replication paths between sites. The cost value determines which path the KCC prefers (lower cost is preferred), and the replication interval controls how often changes are replicated across this link.</p>
      <div style="display: flex; flex-direction: column; gap: 1rem; padding-top: 0.5rem">
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Name*</label>
          <InputText v-model="siteLinkForm.name" style="width: 100%" :disabled="saving || !siteLinkIsNew" />
        </div>
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Member Sites (DNs)</label>
          <div v-for="(s, i) in siteLinkForm.sites" :key="i" style="display: flex; gap: 0.25rem; margin-bottom: 0.25rem">
            <InputText v-model="siteLinkForm.sites[i]" style="flex: 1" :disabled="saving"
                       placeholder="CN=SiteName,CN=Sites,CN=Configuration,..." />
            <Button icon="pi pi-times" severity="danger" text size="small" @click="removeSiteFromLink(i)" />
          </div>
          <Button icon="pi pi-plus" label="Add Site" severity="secondary" text size="small" @click="addSiteToLink" />
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
          <div>
            <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Cost</label>
            <InputNumber v-model="siteLinkForm.cost" :min="1" style="width: 100%" :disabled="saving" />
          </div>
          <div>
            <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Replication Interval (min)</label>
            <InputNumber v-model="siteLinkForm.replInterval" :min="15" style="width: 100%" :disabled="saving" />
          </div>
        </div>
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Description</label>
          <InputText v-model="siteLinkForm.description" style="width: 100%" :disabled="saving" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showSiteLinkDialog = false" :disabled="saving" />
        <Button :label="siteLinkIsNew ? 'Create' : 'Save'" icon="pi pi-save" @click="submitSiteLink" :loading="saving" />
      </template>
    </Dialog>

    <!-- Create Bridge Dialog -->
    <Dialog v-model:visible="showBridgeDialog" header="Create Site Link Bridge" modal :style="{ width: '28rem' }" :closable="!saving">
      <div style="display: flex; flex-direction: column; gap: 1rem; padding-top: 0.5rem">
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Name*</label>
          <InputText v-model="bridgeForm.name" style="width: 100%" :disabled="saving" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showBridgeDialog = false" :disabled="saving" />
        <Button label="Create" icon="pi pi-plus" @click="submitBridge" :loading="saving" />
      </template>
    </Dialog>

    <!-- Create Connection Dialog -->
    <Dialog v-model:visible="showConnectionDialog" header="Create Replication Connection" modal :style="{ width: '28rem' }" :closable="!saving">
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">A replication connection defines a one-way replication path from a source DC to the target DC. The KCC normally creates these automatically, but you can add manual connections when needed.</p>
      <div style="display: flex; flex-direction: column; gap: 1rem; padding-top: 0.5rem">
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">From Server (NTDS Settings DN)*</label>
          <InputText v-model="connectionForm.fromServer" style="width: 100%" :disabled="saving"
                     placeholder="CN=NTDS Settings,CN=DC1,CN=Servers,..." />
        </div>
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Connection Name</label>
          <InputText v-model="connectionForm.name" style="width: 100%" :disabled="saving" placeholder="Auto-generated if empty" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showConnectionDialog = false" :disabled="saving" />
        <Button label="Create" icon="pi pi-plus" @click="submitConnection" :loading="saving" />
      </template>
    </Dialog>

    <!-- Move Server Dialog -->
    <Dialog v-model:visible="showMoveDialog" header="Move Server to Another Site" modal :style="{ width: '28rem' }" :closable="!saving">
      <div style="padding-top: 0.5rem">
        <p style="margin-bottom: 1rem; font-size: 0.875rem">
          Move <strong>{{ moveForm.serverName }}</strong> from <strong>{{ moveForm.siteName }}</strong> to:
        </p>
        <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Target Site Name*</label>
        <InputText v-model="moveForm.targetSite" style="width: 100%" :disabled="saving" placeholder="e.g. Branch-Office-Site" />
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showMoveDialog = false" :disabled="saving" />
        <Button label="Move" icon="pi pi-arrow-right" @click="submitMove" :loading="saving" />
      </template>
    </Dialog>
  </div>
</template>
