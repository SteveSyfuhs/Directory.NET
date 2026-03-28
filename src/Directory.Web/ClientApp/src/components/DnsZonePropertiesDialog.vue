<script setup lang="ts">
import { ref, watch } from 'vue'
import Dialog from 'primevue/dialog'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import Checkbox from 'primevue/checkbox'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import { getZoneProperties, updateZoneProperties } from '../api/dns'
import type { DnsZoneProperties } from '../types/dns'

const props = defineProps<{
  visible: boolean
  zoneName: string
}>()

const emit = defineEmits<{
  'update:visible': [val: boolean]
  updated: []
}>()

const toast = useToast()
const loading = ref(false)
const saving = ref(false)
const zoneProps = ref<DnsZoneProperties | null>(null)

// General tab
const dynamicUpdate = ref('Secure')
const status = ref('Running')

// SOA tab
const soaPrimaryServer = ref('')
const soaResponsiblePerson = ref('')
const soaSerial = ref(0)
const soaRefresh = ref(900)
const soaRetry = ref(600)
const soaExpire = ref(86400)
const soaMinTtl = ref(60)

// Name Servers tab
const nameServers = ref<string[]>([])
const newNsServer = ref('')

// Zone Transfers tab
const allowTransfer = ref('None')
const notifyServers = ref<string[]>([])
const newNotifyServer = ref('')

// Aging tab
const agingEnabled = ref(false)
const noRefreshInterval = ref(168)
const refreshInterval = ref(168)

const dynamicUpdateOptions = [
  { label: 'None', value: 'None' },
  { label: 'Nonsecure and Secure', value: 'NonsecureAndSecure' },
  { label: 'Secure Only', value: 'Secure' },
]

const transferOptions = [
  { label: 'None', value: 'None' },
  { label: 'Any Server', value: 'Any' },
  { label: 'Listed Servers Only', value: 'Listed' },
]

watch(() => props.visible, async (v) => {
  if (v && props.zoneName) {
    await loadProperties()
  }
})

async function loadProperties() {
  loading.value = true
  try {
    const p = await getZoneProperties(props.zoneName)
    zoneProps.value = p

    dynamicUpdate.value = p.dynamicUpdate || 'Secure'
    status.value = p.status || 'Running'

    soaPrimaryServer.value = p.soa?.primaryServer || ''
    soaResponsiblePerson.value = p.soa?.responsiblePerson || ''
    soaSerial.value = p.soa?.serial || 0
    soaRefresh.value = p.soa?.refresh || 900
    soaRetry.value = p.soa?.retry || 600
    soaExpire.value = p.soa?.expire || 86400
    soaMinTtl.value = p.soa?.minimumTtl || 60

    nameServers.value = [...(p.nameServers || [])]

    allowTransfer.value = p.zoneTransfers?.allowTransfer || 'None'
    notifyServers.value = [...(p.zoneTransfers?.notifyServers || [])]

    agingEnabled.value = p.aging?.agingEnabled || false
    noRefreshInterval.value = p.aging?.noRefreshInterval || 168
    refreshInterval.value = p.aging?.refreshInterval || 168
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

function addNameServer() {
  if (newNsServer.value.trim()) {
    nameServers.value.push(newNsServer.value.trim())
    newNsServer.value = ''
  }
}

function removeNameServer(idx: number) {
  nameServers.value.splice(idx, 1)
}

function addNotifyServer() {
  if (newNotifyServer.value.trim()) {
    notifyServers.value.push(newNotifyServer.value.trim())
    newNotifyServer.value = ''
  }
}

function removeNotifyServer(idx: number) {
  notifyServers.value.splice(idx, 1)
}

async function onSave() {
  saving.value = true
  try {
    await updateZoneProperties(props.zoneName, {
      dynamicUpdate: dynamicUpdate.value,
      soa: {
        primaryServer: soaPrimaryServer.value,
        responsiblePerson: soaResponsiblePerson.value,
        refresh: soaRefresh.value,
        retry: soaRetry.value,
        expire: soaExpire.value,
        minimumTtl: soaMinTtl.value,
      },
      nameServers: nameServers.value,
      zoneTransfers: {
        allowTransfer: allowTransfer.value,
        notifyServers: notifyServers.value,
      },
      aging: {
        agingEnabled: agingEnabled.value,
        noRefreshInterval: noRefreshInterval.value,
        refreshInterval: refreshInterval.value,
      },
    })
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Zone properties updated', life: 3000 })
    emit('updated')
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog
    :visible="visible"
    @update:visible="emit('update:visible', $event)"
    :header="`Zone Properties: ${zoneName}`"
    modal
    :style="{ width: '40rem' }"
    :closable="true"
  >
    <div v-if="loading" style="text-align: center; padding: 3rem">
      <ProgressSpinner />
    </div>

    <TabView v-else>
      <!-- General Tab -->
      <TabPanel header="General" value="general">
        <div class="props-form">
          <div class="form-field">
            <label>Zone Name</label>
            <InputText :modelValue="zoneName" disabled style="width: 100%" />
          </div>
          <div class="form-field">
            <label>Type</label>
            <InputText :modelValue="zoneProps?.type || 'primary'" disabled style="width: 100%" />
          </div>
          <div class="form-field">
            <label>Status</label>
            <InputText v-model="status" disabled style="width: 100%" />
          </div>
          <div class="form-field">
            <label>Dynamic Updates</label>
            <Select
              v-model="dynamicUpdate"
              :options="dynamicUpdateOptions"
              optionLabel="label"
              optionValue="value"
              style="width: 100%"
            />
          </div>
        </div>
      </TabPanel>

      <!-- SOA Tab -->
      <TabPanel header="SOA" value="soa">
        <div class="props-form">
          <div class="form-field">
            <label>Primary Server</label>
            <InputText v-model="soaPrimaryServer" style="width: 100%" />
          </div>
          <div class="form-field">
            <label>Responsible Person</label>
            <InputText v-model="soaResponsiblePerson" style="width: 100%" />
          </div>
          <div class="form-field">
            <label>Serial Number</label>
            <InputNumber :modelValue="soaSerial" disabled style="width: 100%" />
          </div>
          <div class="form-row">
            <div class="form-field" style="flex: 1">
              <label>Refresh (s)</label>
              <InputNumber v-model="soaRefresh" :min="0" style="width: 100%" />
            </div>
            <div class="form-field" style="flex: 1">
              <label>Retry (s)</label>
              <InputNumber v-model="soaRetry" :min="0" style="width: 100%" />
            </div>
          </div>
          <div class="form-row">
            <div class="form-field" style="flex: 1">
              <label>Expire (s)</label>
              <InputNumber v-model="soaExpire" :min="0" style="width: 100%" />
            </div>
            <div class="form-field" style="flex: 1">
              <label>Minimum TTL (s)</label>
              <InputNumber v-model="soaMinTtl" :min="0" style="width: 100%" />
            </div>
          </div>
        </div>
      </TabPanel>

      <!-- Name Servers Tab -->
      <TabPanel header="Name Servers" value="name-servers">
        <div class="props-form">
          <DataTable :value="nameServers.map((ns, i) => ({ idx: i, server: ns }))" size="small" stripedRows>
            <Column field="server" header="Name Server" />
            <Column header="" style="width: 4rem">
              <template #body="{ data }">
                <Button icon="pi pi-trash" size="small" severity="danger" text @click="removeNameServer(data.idx)" />
              </template>
            </Column>
            <template #empty>
              <div style="text-align: center; padding: 1rem; color: var(--p-text-muted-color)">No name servers</div>
            </template>
          </DataTable>
          <div style="display: flex; gap: 0.5rem; margin-top: 0.5rem">
            <InputText v-model="newNsServer" placeholder="ns.example.com" style="flex: 1" @keyup.enter="addNameServer" />
            <Button icon="pi pi-plus" size="small" @click="addNameServer" :disabled="!newNsServer.trim()" />
          </div>
        </div>
      </TabPanel>

      <!-- Zone Transfers Tab -->
      <TabPanel header="Zone Transfers" value="zone-transfers">
        <div class="props-form">
          <div class="form-field">
            <label>Allow Zone Transfers</label>
            <Select
              v-model="allowTransfer"
              :options="transferOptions"
              optionLabel="label"
              optionValue="value"
              style="width: 100%"
            />
          </div>

          <div v-if="allowTransfer === 'Listed'" class="form-field">
            <label>Notify Servers</label>
            <DataTable :value="notifyServers.map((s, i) => ({ idx: i, server: s }))" size="small" stripedRows>
              <Column field="server" header="Server" />
              <Column header="" style="width: 4rem">
                <template #body="{ data }">
                  <Button icon="pi pi-trash" size="small" severity="danger" text @click="removeNotifyServer(data.idx)" />
                </template>
              </Column>
              <template #empty>
                <div style="text-align: center; padding: 1rem; color: var(--p-text-muted-color)">No servers listed</div>
              </template>
            </DataTable>
            <div style="display: flex; gap: 0.5rem; margin-top: 0.5rem">
              <InputText v-model="newNotifyServer" placeholder="IP address" style="flex: 1" @keyup.enter="addNotifyServer" />
              <Button icon="pi pi-plus" size="small" @click="addNotifyServer" :disabled="!newNotifyServer.trim()" />
            </div>
          </div>
        </div>
      </TabPanel>

      <!-- Aging / Scavenging Tab -->
      <TabPanel header="Scavenging" value="scavenging">
        <div class="props-form">
          <div class="form-field">
            <div style="display: flex; align-items: center; gap: 0.5rem">
              <Checkbox v-model="agingEnabled" :binary="true" inputId="aging-enabled" />
              <label for="aging-enabled">Enable aging/scavenging for this zone</label>
            </div>
          </div>
          <div class="form-field">
            <label>No-Refresh Interval (hours)</label>
            <InputNumber v-model="noRefreshInterval" :min="0" :disabled="!agingEnabled" style="width: 100%" />
            <small class="text-muted">Period during which a record cannot be refreshed (default: 168 = 7 days)</small>
          </div>
          <div class="form-field">
            <label>Refresh Interval (hours)</label>
            <InputNumber v-model="refreshInterval" :min="0" :disabled="!agingEnabled" style="width: 100%" />
            <small class="text-muted">Period after no-refresh during which a record can be refreshed (default: 168 = 7 days)</small>
          </div>
        </div>
      </TabPanel>
    </TabView>

    <template #footer>
      <Button label="Cancel" severity="secondary" @click="emit('update:visible', false)" />
      <Button label="Save" icon="pi pi-check" :loading="saving" @click="onSave" />
    </template>
  </Dialog>
</template>

<style scoped>
.props-form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.form-field > label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-color);
}

.form-row {
  display: flex;
  gap: 0.75rem;
}

.text-muted {
  color: var(--p-text-muted-color);
  font-size: 0.75rem;
}
</style>
