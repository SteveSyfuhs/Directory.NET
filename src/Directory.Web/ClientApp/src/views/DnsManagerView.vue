<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import Tree from 'primevue/tree'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Toolbar from 'primevue/toolbar'
import ProgressSpinner from 'primevue/progressspinner'
import Select from 'primevue/select'
import ConfirmDialog from 'primevue/confirmdialog'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import {
  listZones,
  listRecords,
  deleteRecord,
  deleteZone,
  getDnsStatistics,
  triggerScavenging,
  registerSrvRecords,
} from '../api/dns'
import {
  getDnssecSettings,
  updateDnssecSettings,
  signZone,
  listDnssecKeys,
  generateDnssecKey,
  deleteDnssecKey,
  getDsRecord,
} from '../api/dnssec'
import type { DnsZone, DnsRecord, DnsStatistics } from '../types/dns'
import type { DnssecSettings, DnssecKey, DnssecDsRecord } from '../types/dnssec'
import { RECORD_TYPE_COLORS } from '../types/dns'
import DnsRecordDialog from '../components/DnsRecordDialog.vue'
import DnsZoneDialog from '../components/DnsZoneDialog.vue'
import DnsZonePropertiesDialog from '../components/DnsZonePropertiesDialog.vue'

const toast = useToast()
const confirm = useConfirm()

const loading = ref(true)
const recordsLoading = ref(false)
const zones = ref<DnsZone[]>([])
const selectedZone = ref<DnsZone | null>(null)
const records = ref<DnsRecord[]>([])
const statistics = ref<DnsStatistics | null>(null)
const showStatistics = ref(true)
const typeFilter = ref<string | null>(null)
const selectedRecords = ref<DnsRecord[]>([])

// Dialogs
const showRecordDialog = ref(false)
const editingRecord = ref<DnsRecord | null>(null)
const showZoneDialog = ref(false)
const showZonePropsDialog = ref(false)

// DNSSEC state
const dnssecSettings = ref<DnssecSettings | null>(null)
const dnssecKeys = ref<DnssecKey[]>([])
const dsRecord = ref<DnssecDsRecord | null>(null)
const dnssecLoading = ref(false)
const showDnssecPanel = ref(false)
const signingZone = ref(false)
const generatingKey = ref(false)
const newKeyType = ref<'KSK' | 'ZSK'>('ZSK')
const newKeyAlgorithm = ref(13)

const keyAlgorithmOptions = [
  { label: 'ECDSAP256SHA256 (13) - Recommended', value: 13 },
  { label: 'RSASHA256 (8)', value: 8 },
]

const typeFilterOptions = [
  { label: 'All Types', value: null },
  { label: 'A', value: 'A' },
  { label: 'AAAA', value: 'AAAA' },
  { label: 'CNAME', value: 'CNAME' },
  { label: 'MX', value: 'MX' },
  { label: 'SRV', value: 'SRV' },
  { label: 'PTR', value: 'PTR' },
  { label: 'NS', value: 'NS' },
  { label: 'TXT', value: 'TXT' },
]

const treeNodes = computed(() => {
  const forward = zones.value.filter(z => !z.isReverse)
  const reverse = zones.value.filter(z => z.isReverse)

  return [
    {
      key: 'server-root',
      label: statistics.value?.serverHostname ?? 'DNS Server',
      icon: 'pi pi-server',
      children: [
        {
          key: 'forward-zones',
          label: 'Forward Lookup Zones',
          icon: 'pi pi-folder',
          children: forward.map(z => ({
            key: `zone:${z.name}`,
            label: z.name,
            icon: 'pi pi-globe',
            data: z,
          })),
        },
        {
          key: 'reverse-zones',
          label: 'Reverse Lookup Zones',
          icon: 'pi pi-folder',
          children: reverse.map(z => ({
            key: `zone:${z.name}`,
            label: z.name,
            icon: 'pi pi-globe',
            data: z,
          })),
        },
      ],
    },
  ]
})

const selectedTreeNode = ref<any>(null)

const filteredRecords = computed(() => {
  if (!typeFilter.value) return records.value
  return records.value.filter(r => r.type === typeFilter.value)
})

onMounted(() => loadData())

async function loadData() {
  loading.value = true
  try {
    const [z, stats] = await Promise.all([listZones(), getDnsStatistics()])
    zones.value = z
    statistics.value = stats
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function onNodeSelect(node: any) {
  if (node.key === 'server-root') {
    selectedZone.value = null
    showStatistics.value = true
    try {
      statistics.value = await getDnsStatistics()
    } catch (_) {}
    return
  }

  if (node.key?.startsWith('zone:')) {
    showStatistics.value = false
    selectedZone.value = node.data
    showDnssecPanel.value = false
    await Promise.all([loadRecords(node.data.name), loadDnssecData(node.data.name)])
    return
  }

  // Folder nodes: do nothing special
  showStatistics.value = false
  selectedZone.value = null
  records.value = []
}

async function loadRecords(zoneName: string) {
  recordsLoading.value = true
  typeFilter.value = null
  try {
    records.value = await listRecords(zoneName)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    recordsLoading.value = false
  }
}

function onNewRecord() {
  editingRecord.value = null
  showRecordDialog.value = true
}

function onEditRecord(record: DnsRecord) {
  editingRecord.value = record
  showRecordDialog.value = true
}

function onDeleteRecord(record: DnsRecord) {
  confirm.require({
    message: `Delete record "${record.name}" (${record.type})?`,
    header: 'Confirm Delete',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await deleteRecord(selectedZone.value!.name, record.id)
        toast.add({ severity: 'success', summary: 'Deleted', detail: `Record ${record.name} deleted`, life: 3000 })
        await loadRecords(selectedZone.value!.name)
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      }
    },
  })
}

function onDeleteZone() {
  if (!selectedZone.value) return
  confirm.require({
    message: `Delete zone "${selectedZone.value.name}"? All records will be lost.`,
    header: 'Confirm Delete Zone',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await deleteZone(selectedZone.value!.name)
        toast.add({ severity: 'success', summary: 'Deleted', detail: `Zone deleted`, life: 3000 })
        selectedZone.value = null
        records.value = []
        showStatistics.value = true
        await loadData()
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      }
    },
  })
}

async function onScavenging() {
  try {
    await triggerScavenging()
    toast.add({ severity: 'success', summary: 'Scavenging', detail: 'Scavenging initiated', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

const registeringSrv = ref(false)
async function onRegisterSrv() {
  registeringSrv.value = true
  try {
    const result = await registerSrvRecords()
    toast.add({
      severity: result.errors.length > 0 ? 'warn' : 'success',
      summary: 'SRV Registration',
      detail: `Registered ${result.registered}/${result.total} records${result.errors.length > 0 ? ` (${result.errors.length} errors)` : ''}`,
      life: 5000
    })
    // Reload zones and records
    await loadData()
    if (selectedZone.value) await loadRecords(selectedZone.value.name)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    registeringSrv.value = false
  }
}

function onRecordSaved() {
  showRecordDialog.value = false
  if (selectedZone.value) loadRecords(selectedZone.value.name)
}

function onZoneCreated() {
  showZoneDialog.value = false
  loadData()
}

function onZonePropsUpdated() {
  showZonePropsDialog.value = false
}

// ── DNSSEC Functions ──────────────────────────────────────────

async function loadDnssecData(zoneName: string) {
  dnssecLoading.value = true
  dsRecord.value = null
  try {
    const [settings, keys] = await Promise.all([
      getDnssecSettings(zoneName),
      listDnssecKeys(zoneName),
    ])
    dnssecSettings.value = settings
    dnssecKeys.value = keys
    // Try to load DS record if we have a KSK
    if (keys.some(k => k.keyType === 'KSK')) {
      try { dsRecord.value = await getDsRecord(zoneName) } catch (_) {}
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'DNSSEC Error', detail: e.message, life: 5000 })
  } finally {
    dnssecLoading.value = false
  }
}

async function onToggleDnssec() {
  if (!selectedZone.value || !dnssecSettings.value) return
  const enabling = !dnssecSettings.value.dnssecEnabled
  try {
    dnssecSettings.value = await updateDnssecSettings(selectedZone.value.name, {
      dnssecEnabled: enabling,
    })
    toast.add({
      severity: 'success',
      summary: 'DNSSEC',
      detail: `DNSSEC ${enabling ? 'enabled' : 'disabled'} for ${selectedZone.value.name}`,
      life: 3000,
    })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onSignZone() {
  if (!selectedZone.value) return
  signingZone.value = true
  try {
    const result = await signZone(selectedZone.value.name)
    toast.add({
      severity: 'success',
      summary: 'Zone Signed',
      detail: `Signed ${result.signedRRsets} RRsets`,
      life: 3000,
    })
    await loadDnssecData(selectedZone.value.name)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Signing Error', detail: e.message, life: 5000 })
  } finally {
    signingZone.value = false
  }
}

async function onGenerateKey() {
  if (!selectedZone.value) return
  generatingKey.value = true
  try {
    await generateDnssecKey(selectedZone.value.name, {
      keyType: newKeyType.value,
      algorithm: newKeyAlgorithm.value,
    })
    toast.add({ severity: 'success', summary: 'Key Generated', detail: `${newKeyType.value} generated`, life: 3000 })
    await loadDnssecData(selectedZone.value.name)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    generatingKey.value = false
  }
}

function onDeleteKey(key: DnssecKey) {
  if (!selectedZone.value) return
  confirm.require({
    message: `Delete ${key.keyType} key (tag ${key.keyTag})?`,
    header: 'Confirm Delete Key',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await deleteDnssecKey(selectedZone.value!.name, key.id)
        toast.add({ severity: 'success', summary: 'Deleted', detail: `Key deleted`, life: 3000 })
        await loadDnssecData(selectedZone.value!.name)
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      }
    },
  })
}

function getRecordTypeColor(type: string): string {
  return RECORD_TYPE_COLORS[type] ?? 'var(--p-text-muted-color)'
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>DNS Manager</h1>
      <p>Manage DNS zones, records, and server configuration</p>
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">Manage Active Directory-integrated DNS zones and records. Forward lookup zones resolve hostnames to IP addresses, while reverse lookup zones resolve IP addresses back to hostnames. AD-integrated zones are stored in the directory and replicated automatically to all domain controllers.</p>
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else class="dns-layout">
      <!-- Left Panel: Zone Tree -->
      <div class="dns-tree-panel card" style="padding: 0">
        <div style="padding: 0.75rem 1rem; border-bottom: 1px solid var(--p-surface-border); font-weight: 600; font-size: 0.875rem">
          DNS Zones
        </div>
        <Tree
          :value="treeNodes"
          selectionMode="single"
          v-model:selectionKeys="selectedTreeNode"
          @node-select="onNodeSelect"
          class="dns-tree"
        />
      </div>

      <!-- Right Panel: Records or Statistics -->
      <div class="dns-content-panel">
        <!-- Statistics View -->
        <div v-if="showStatistics && statistics" class="card">
          <h2 style="margin: 0 0 1rem; font-size: 1.125rem; font-weight: 600">Server Statistics</h2>
          <div class="stat-grid">
            <div class="stat-card">
              <div class="stat-icon blue"><i class="pi pi-globe"></i></div>
              <div>
                <div class="stat-value">{{ statistics.zoneCount }}</div>
                <div class="stat-label">Zones</div>
              </div>
            </div>
            <div class="stat-card">
              <div class="stat-icon green"><i class="pi pi-list"></i></div>
              <div>
                <div class="stat-value">{{ statistics.recordCount }}</div>
                <div class="stat-label">Records</div>
              </div>
            </div>
            <div class="stat-card">
              <div class="stat-icon purple"><i class="pi pi-arrow-right-arrow-left"></i></div>
              <div>
                <div class="stat-value">{{ statistics.forwarderCount }}</div>
                <div class="stat-label">Forwarders</div>
              </div>
            </div>
            <div class="stat-card">
              <div class="stat-icon amber"><i class="pi pi-check-circle"></i></div>
              <div>
                <div class="stat-value">{{ statistics.uptime }}</div>
                <div class="stat-label">Status</div>
              </div>
            </div>
          </div>
          <div style="margin-top: 1rem; font-size: 0.875rem; color: var(--p-text-muted-color)">
            <p><strong>Hostname:</strong> {{ statistics.serverHostname }}</p>
            <p><strong>Port:</strong> {{ statistics.port }}</p>
            <p><strong>IP Addresses:</strong> {{ statistics.serverIpAddresses?.join(', ') }}</p>
          </div>
        </div>

        <!-- Records View -->
        <div v-else-if="selectedZone" class="card" style="padding: 0">
          <Toolbar style="border: none; border-bottom: 1px solid var(--p-surface-border); border-radius: 0.75rem 0.75rem 0 0">
            <template #start>
              <Button label="New Record" icon="pi pi-plus" size="small" @click="onNewRecord" />
              <Button label="Properties" icon="pi pi-cog" size="small" severity="secondary" @click="showZonePropsDialog = true" style="margin-left: 0.5rem" />
              <Button :label="showDnssecPanel ? 'Records' : 'DNSSEC'" :icon="showDnssecPanel ? 'pi pi-list' : 'pi pi-shield'" size="small" severity="secondary" @click="showDnssecPanel = !showDnssecPanel" style="margin-left: 0.5rem" />
              <Button label="Delete Zone" icon="pi pi-trash" size="small" severity="danger" @click="onDeleteZone" style="margin-left: 0.5rem" />
            </template>
            <template #end>
              <Select
                v-model="typeFilter"
                :options="typeFilterOptions"
                optionLabel="label"
                optionValue="value"
                placeholder="Filter by type"
                size="small"
                style="width: 10rem"
              />
              <Button icon="pi pi-refresh" size="small" severity="secondary" @click="loadRecords(selectedZone!.name)" style="margin-left: 0.5rem" />
            </template>
          </Toolbar>

          <!-- DNSSEC Panel -->
          <div v-if="showDnssecPanel" style="padding: 1.5rem">
            <div v-if="dnssecLoading" style="text-align: center; padding: 2rem">
              <ProgressSpinner />
            </div>
            <div v-else-if="dnssecSettings">
              <!-- DNSSEC Toggle -->
              <div style="display: flex; align-items: center; gap: 1rem; margin-bottom: 1.5rem; padding: 1rem; background: var(--app-neutral-bg); border-radius: 0.5rem; border: 1px solid var(--app-neutral-border)">
                <i class="pi pi-shield" style="font-size: 1.5rem; color: var(--app-accent-color)"></i>
                <div style="flex: 1">
                  <div style="font-weight: 600; font-size: 0.9375rem">DNSSEC Protection</div>
                  <div style="font-size: 0.8125rem; color: var(--p-text-muted-color)">
                    {{ dnssecSettings.dnssecEnabled ? 'Zone is signed with DNSSEC' : 'DNSSEC is disabled for this zone' }}
                  </div>
                </div>
                <Tag :value="dnssecSettings.dnssecEnabled ? 'Enabled' : 'Disabled'"
                     :severity="dnssecSettings.dnssecEnabled ? 'success' : 'secondary'" />
                <Button :label="dnssecSettings.dnssecEnabled ? 'Disable' : 'Enable'"
                        :severity="dnssecSettings.dnssecEnabled ? 'danger' : 'success'"
                        size="small" @click="onToggleDnssec" />
              </div>

              <!-- Settings -->
              <div v-if="dnssecSettings.dnssecEnabled" style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1.5rem">
                <div class="stat-card">
                  <div class="stat-icon blue"><i class="pi pi-calendar"></i></div>
                  <div>
                    <div class="stat-value" style="font-size: 1.25rem">{{ dnssecSettings.signatureValidityDays }}d</div>
                    <div class="stat-label">Signature Validity</div>
                  </div>
                </div>
                <div class="stat-card">
                  <div class="stat-icon purple"><i class="pi pi-sync"></i></div>
                  <div>
                    <div class="stat-value" style="font-size: 1.25rem">{{ dnssecSettings.keyRolloverIntervalDays }}d</div>
                    <div class="stat-label">Key Rollover Interval</div>
                  </div>
                </div>
              </div>

              <!-- Last Signed -->
              <div v-if="dnssecSettings.lastSignedAt" style="margin-bottom: 1rem; font-size: 0.875rem; color: var(--p-text-muted-color)">
                <i class="pi pi-clock"></i> Last signed: {{ new Date(dnssecSettings.lastSignedAt).toLocaleString() }}
              </div>

              <!-- Sign Zone Button -->
              <div v-if="dnssecSettings.dnssecEnabled" style="margin-bottom: 1.5rem">
                <Button label="Sign Zone" icon="pi pi-pencil" size="small" @click="onSignZone" :loading="signingZone"
                        :disabled="dnssecKeys.length === 0" />
                <span v-if="dnssecKeys.length === 0" style="margin-left: 0.5rem; font-size: 0.8125rem; color: var(--app-warn-text)">
                  Generate KSK and ZSK keys first
                </span>
              </div>

              <!-- Key Management -->
              <h3 style="font-size: 0.9375rem; font-weight: 600; margin: 1.5rem 0 0.75rem">DNSSEC Keys</h3>
              <div style="display: flex; gap: 0.5rem; align-items: center; margin-bottom: 0.75rem">
                <Select v-model="newKeyType" :options="[{label: 'KSK (Key Signing Key)', value: 'KSK'}, {label: 'ZSK (Zone Signing Key)', value: 'ZSK'}]"
                        optionLabel="label" optionValue="value" size="small" style="width: 14rem" />
                <Select v-model="newKeyAlgorithm" :options="keyAlgorithmOptions"
                        optionLabel="label" optionValue="value" size="small" style="width: 16rem" />
                <Button label="Generate Key" icon="pi pi-plus" size="small" @click="onGenerateKey" :loading="generatingKey" />
              </div>

              <DataTable :value="dnssecKeys" size="small" stripedRows>
                <Column field="keyType" header="Key Type" style="width: 6rem">
                  <template #body="{ data }">
                    <Tag :value="data.keyType" :severity="data.keyType === 'KSK' ? 'warn' : 'info'" />
                  </template>
                </Column>
                <Column field="algorithmName" header="Algorithm" />
                <Column field="keyTag" header="Key Tag" style="width: 6rem">
                  <template #body="{ data }">
                    <span style="font-family: monospace">{{ data.keyTag }}</span>
                  </template>
                </Column>
                <Column field="createdAt" header="Created" style="width: 10rem">
                  <template #body="{ data }">
                    {{ new Date(data.createdAt).toLocaleDateString() }}
                  </template>
                </Column>
                <Column field="expiresAt" header="Expires" style="width: 10rem">
                  <template #body="{ data }">
                    {{ data.expiresAt ? new Date(data.expiresAt).toLocaleDateString() : 'Never' }}
                  </template>
                </Column>
                <Column field="isActive" header="Active" style="width: 5rem">
                  <template #body="{ data }">
                    <Tag :value="data.isActive ? 'Yes' : 'No'" :severity="data.isActive ? 'success' : 'secondary'" />
                  </template>
                </Column>
                <Column header="Actions" style="width: 5rem">
                  <template #body="{ data }">
                    <Button icon="pi pi-trash" size="small" severity="danger" text @click="onDeleteKey(data)" />
                  </template>
                </Column>
                <template #empty>
                  <div style="text-align: center; padding: 1.5rem; color: var(--p-text-muted-color)">
                    No DNSSEC keys generated. Create a KSK and ZSK to enable zone signing.
                  </div>
                </template>
              </DataTable>

              <!-- DS Record -->
              <div v-if="dsRecord" style="margin-top: 1.5rem">
                <h3 style="font-size: 0.9375rem; font-weight: 600; margin-bottom: 0.5rem">DS Record (for registrar)</h3>
                <div style="background: var(--app-neutral-bg); border: 1px solid var(--app-neutral-border); border-radius: 0.5rem; padding: 1rem; font-family: monospace; font-size: 0.8125rem; word-break: break-all">
                  {{ dsRecord.dsRecord }}
                </div>
                <div style="margin-top: 0.5rem; font-size: 0.8125rem; color: var(--p-text-muted-color)">
                  Key Tag: {{ dsRecord.keyTag }} | Algorithm: {{ dsRecord.algorithm }} | Digest Type: {{ dsRecord.digestType }} (SHA-256)
                </div>
              </div>
            </div>
          </div>

          <!-- Records Panel -->
          <div v-else-if="recordsLoading" style="text-align: center; padding: 3rem">
            <ProgressSpinner />
          </div>

          <DataTable
            v-else
            :value="filteredRecords"
            v-model:selection="selectedRecords"
            dataKey="id"
            stripedRows
            size="small"
            paginator
            :rows="25"
            :rowsPerPageOptions="[25, 50, 100]"
            sortField="name"
            :sortOrder="1"
          >
            <template #header>
              <div style="font-weight: 600; font-size: 0.875rem">
                {{ selectedZone.name }} &mdash; {{ filteredRecords.length }} record(s)
              </div>
            </template>
            <Column field="name" header="Name" sortable style="min-width: 14rem">
              <template #body="{ data }">
                <span style="font-family: monospace; font-size: 0.8125rem">{{ data.name }}</span>
              </template>
            </Column>
            <Column field="type" header="Type" sortable style="width: 7rem">
              <template #body="{ data }">
                <Tag
                  :value="data.type"
                  :style="{ backgroundColor: getRecordTypeColor(data.type), color: 'var(--p-surface-0)', fontSize: '0.75rem', fontWeight: 600 }"
                />
              </template>
            </Column>
            <Column field="data" header="Data" sortable style="min-width: 16rem">
              <template #body="{ data }">
                <span style="font-family: monospace; font-size: 0.8125rem">{{ data.data }}</span>
              </template>
            </Column>
            <Column field="ttl" header="TTL" sortable style="width: 6rem">
              <template #body="{ data }">
                <span style="font-size: 0.8125rem">{{ data.ttl }}s</span>
              </template>
            </Column>
            <Column header="Actions" style="width: 8rem">
              <template #body="{ data }">
                <Button icon="pi pi-pencil" size="small" severity="secondary" text @click="onEditRecord(data)" />
                <Button icon="pi pi-trash" size="small" severity="danger" text @click="onDeleteRecord(data)" />
              </template>
            </Column>
            <template #empty>
              <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
                No records in this zone. Click "New Record" to add DNS records such as A, AAAA, CNAME, MX, SRV, or TXT entries.
              </div>
            </template>
          </DataTable>
        </div>

        <!-- No selection -->
        <div v-else class="card" style="text-align: center; padding: 3rem; color: var(--p-text-muted-color)">
          Select a zone from the tree to view and manage its DNS records. Use "New Zone" below to create a forward or reverse lookup zone.
        </div>

        <!-- Global Toolbar -->
        <div class="toolbar" style="margin-top: 1rem">
          <Button label="New Zone" icon="pi pi-plus" size="small" @click="showZoneDialog = true" />
          <Button label="Register AD SRV Records" icon="pi pi-server" size="small" severity="secondary"
                  @click="onRegisterSrv" :loading="registeringSrv"
                  v-tooltip="'Create standard AD SRV records (_ldap._tcp, _kerberos._tcp, _gc._tcp, etc.)'" />
          <Button label="Scavenging" icon="pi pi-clock" size="small" severity="secondary" @click="onScavenging" />
          <div class="toolbar-spacer"></div>
          <Button label="Refresh" icon="pi pi-refresh" size="small" severity="secondary" @click="loadData" />
        </div>
      </div>
    </div>

    <!-- Dialogs -->
    <DnsRecordDialog
      :visible="showRecordDialog"
      @update:visible="showRecordDialog = $event"
      :zoneName="selectedZone?.name ?? ''"
      :record="editingRecord"
      @saved="onRecordSaved"
    />

    <DnsZoneDialog
      :visible="showZoneDialog"
      @update:visible="showZoneDialog = $event"
      @created="onZoneCreated"
    />

    <DnsZonePropertiesDialog
      v-if="selectedZone"
      :visible="showZonePropsDialog"
      @update:visible="showZonePropsDialog = $event"
      :zoneName="selectedZone.name"
      @updated="onZonePropsUpdated"
    />

    <ConfirmDialog />
  </div>
</template>

<style scoped>
.dns-layout {
  display: grid;
  grid-template-columns: 300px 1fr;
  gap: 1rem;
  align-items: start;
}

.dns-tree-panel {
  position: sticky;
  top: 0;
  max-height: calc(100vh - 10rem);
  overflow-y: auto;
}

.dns-tree {
  padding: 0.5rem;
}

.dns-tree :deep(.p-tree-node-label) {
  font-size: 0.875rem;
}

.dns-tree :deep(.p-tree-node-content) {
  padding: 0.375rem 0.5rem;
  border-radius: 0.375rem;
}

.dns-tree :deep(.p-tree-node-content:hover) {
  background: var(--p-surface-hover);
}

.dns-tree :deep(.p-tree-node-content.p-tree-node-selected) {
  background: var(--p-primary-50);
}
</style>
