<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import Checkbox from 'primevue/checkbox'
import Select from 'primevue/select'
import Chip from 'primevue/chip'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import { useToast } from 'primevue/usetoast'
import {
  getMappings,
  createMapping,
  deleteMapping,
  getSettings,
  updateSettings,
} from '../api/smartCard'
import type { SmartCardMapping, SmartCardSettings, MappingType } from '../types/smartCard'

const toast = useToast()

const activeTab = ref('0')

// Settings
const settings = ref<SmartCardSettings>({
  enabled: false,
  defaultMappingType: 'UpnMapping',
  requireSmartCardLogon: false,
  validateCertificateChain: true,
  checkRevocation: true,
  trustedCAs: [],
  modifiedAt: '',
})
const settingsLoading = ref(false)
const settingsSaving = ref(false)
const tempCA = ref('')

// Mappings
const searchQuery = ref('')
const searching = ref(false)
const resolvedDn = ref('')
const mappings = ref<SmartCardMapping[]>([])
const mappingsLoading = ref(false)

// Create mapping
const createVisible = ref(false)
const createSaving = ref(false)
const createForm = ref({
  userDn: '',
  certificateData: '',
  mappingType: 'UpnMapping' as MappingType,
})

// Delete
const deleteVisible = ref(false)
const deleteTarget = ref<SmartCardMapping | null>(null)
const deleting = ref(false)

const mappingTypes: { label: string; value: MappingType }[] = [
  { label: 'Explicit Mapping', value: 'ExplicitMapping' },
  { label: 'Subject Mapping', value: 'SubjectMapping' },
  { label: 'Issuer + Subject', value: 'IssuerAndSubjectMapping' },
  { label: 'UPN Mapping', value: 'UpnMapping' },
  { label: 'SAN Email Mapping', value: 'SubjectAlternativeNameMapping' },
]

const hasUser = computed(() => resolvedDn.value !== '')

onMounted(async () => {
  await loadSettings()
})

async function loadSettings() {
  settingsLoading.value = true
  try {
    settings.value = await getSettings()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Load Failed', detail: e.message, life: 5000 })
  } finally {
    settingsLoading.value = false
  }
}

async function saveSettings() {
  settingsSaving.value = true
  try {
    settings.value = await updateSettings(settings.value)
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Smart card settings updated.', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    settingsSaving.value = false
  }
}

function addCA() {
  const val = tempCA.value.trim()
  if (val && !settings.value.trustedCAs.includes(val)) {
    settings.value.trustedCAs.push(val)
    tempCA.value = ''
  }
}

function removeCA(index: number) {
  settings.value.trustedCAs.splice(index, 1)
}

async function lookupUser() {
  if (!searchQuery.value.trim()) return
  searching.value = true
  resolvedDn.value = searchQuery.value.trim()
  try {
    await loadMappings()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'User Not Found', detail: e.message, life: 5000 })
    resolvedDn.value = ''
    mappings.value = []
  } finally {
    searching.value = false
  }
}

async function loadMappings() {
  mappingsLoading.value = true
  try {
    mappings.value = await getMappings(resolvedDn.value)
  } finally {
    mappingsLoading.value = false
  }
}

function openCreate() {
  createForm.value = {
    userDn: resolvedDn.value,
    certificateData: '',
    mappingType: settings.value.defaultMappingType || 'UpnMapping',
  }
  createVisible.value = true
}

async function doCreate() {
  createSaving.value = true
  try {
    await createMapping(createForm.value.userDn, createForm.value.certificateData, createForm.value.mappingType)
    toast.add({ severity: 'success', summary: 'Created', detail: 'Certificate mapping created.', life: 3000 })
    createVisible.value = false
    await loadMappings()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    createSaving.value = false
  }
}

function openDelete(mapping: SmartCardMapping) {
  deleteTarget.value = mapping
  deleteVisible.value = true
}

async function doDelete() {
  if (!deleteTarget.value) return
  deleting.value = true
  try {
    await deleteMapping(resolvedDn.value, deleteTarget.value.id)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'Mapping deleted.', life: 3000 })
    deleteVisible.value = false
    await loadMappings()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    deleting.value = false
  }
}

function formatDate(d: string | null) {
  if (!d) return '-'
  return new Date(d).toLocaleString()
}

function mappingTypeLabel(type: MappingType): string {
  return mappingTypes.find(m => m.value === type)?.label || type
}

async function handleCertFile(event: Event) {
  const input = event.target as HTMLInputElement
  if (!input.files?.length) return
  const file = input.files[0]
  const reader = new FileReader()
  reader.onload = () => {
    const result = reader.result as ArrayBuffer
    const bytes = new Uint8Array(result)
    let binary = ''
    for (const b of bytes) binary += String.fromCharCode(b)
    createForm.value.certificateData = btoa(binary)
  }
  reader.readAsArrayBuffer(file)
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1><i class="pi pi-id-card" style="margin-right: 0.5rem;"></i>Smart Card / PIV</h1>
      <p>Manage smart card certificate mappings and PIV authentication settings.</p>
    </div>

    <Tabs :value="activeTab">
      <TabList>
        <Tab value="0">Settings</Tab>
        <Tab value="1">Certificate Mappings</Tab>
      </TabList>
      <TabPanels>
        <!-- Settings Tab -->
        <TabPanel value="0">
          <div class="card" style="margin-top: 1rem;">
            <div class="card-title">Smart Card Authentication Settings</div>
            <div v-if="settingsLoading" style="padding: 1rem; text-align: center;">Loading...</div>
            <div v-else style="display: flex; flex-direction: column; gap: 1rem;">
              <div style="display: flex; align-items: center; gap: 0.5rem;">
                <Checkbox v-model="settings.enabled" :binary="true" inputId="sc-enabled" />
                <label for="sc-enabled" style="font-weight: 600;">Enable Smart Card Authentication</label>
              </div>
              <div style="display: flex; align-items: center; gap: 0.5rem;">
                <Checkbox v-model="settings.requireSmartCardLogon" :binary="true" inputId="sc-require" />
                <label for="sc-require">Require Smart Card for Interactive Logon</label>
              </div>
              <div style="display: flex; align-items: center; gap: 0.5rem;">
                <Checkbox v-model="settings.validateCertificateChain" :binary="true" inputId="sc-chain" />
                <label for="sc-chain">Validate Certificate Chain</label>
              </div>
              <div style="display: flex; align-items: center; gap: 0.5rem;">
                <Checkbox v-model="settings.checkRevocation" :binary="true" inputId="sc-revoke" />
                <label for="sc-revoke">Check Certificate Revocation</label>
              </div>
              <div>
                <label style="font-size: 0.8125rem; font-weight: 600;">Default Mapping Type</label>
                <Select
                  v-model="settings.defaultMappingType"
                  :options="mappingTypes"
                  optionLabel="label"
                  optionValue="value"
                  style="width: 300px; margin-top: 0.25rem;"
                />
              </div>
              <div>
                <label style="font-size: 0.8125rem; font-weight: 600;">Trusted CA Thumbprints</label>
                <div style="display: flex; gap: 0.25rem; margin-top: 0.25rem;">
                  <InputText v-model="tempCA" placeholder="CA certificate thumbprint" style="flex: 1; max-width: 500px;" @keyup.enter="addCA" />
                  <Button icon="pi pi-plus" size="small" @click="addCA" />
                </div>
                <div style="margin-top: 0.5rem; display: flex; flex-wrap: wrap; gap: 0.25rem;">
                  <Chip v-for="(ca, i) in settings.trustedCAs" :key="i" :label="ca" removable @remove="removeCA(i)" />
                  <span v-if="settings.trustedCAs.length === 0" style="font-size: 0.8125rem; color: var(--p-text-muted-color);">No trusted CAs configured (any CA accepted).</span>
                </div>
              </div>
              <div style="border-top: 1px solid var(--p-surface-border); padding-top: 1rem; margin-top: 0.5rem;">
                <Button label="Save Settings" icon="pi pi-check" :loading="settingsSaving" @click="saveSettings" />
              </div>
            </div>
          </div>
        </TabPanel>

        <!-- Certificate Mappings Tab -->
        <TabPanel value="1">
          <div class="card" style="margin-top: 1rem; margin-bottom: 1.5rem;">
            <div class="card-title">Find User</div>
            <div class="toolbar">
              <InputText
                v-model="searchQuery"
                placeholder="Enter DN, UPN, or sAMAccountName..."
                style="flex: 1; min-width: 300px;"
                @keyup.enter="lookupUser"
              />
              <Button label="Lookup" icon="pi pi-search" :loading="searching" @click="lookupUser" />
            </div>
          </div>

          <template v-if="hasUser">
            <div class="card">
              <div class="toolbar">
                <div class="card-title" style="margin-bottom: 0;">Certificate Mappings</div>
                <span style="flex: 1;"></span>
                <Button label="Map Certificate" icon="pi pi-plus" @click="openCreate" />
              </div>

              <DataTable :value="mappings" :loading="mappingsLoading" stripedRows>
                <template #empty>No certificate mappings for this user.</template>
                <Column field="certificateSubject" header="Subject" />
                <Column field="certificateIssuer" header="Issuer" />
                <Column field="certificateThumbprint" header="Thumbprint">
                  <template #body="{ data }">
                    <code style="font-size: 0.75rem;">{{ data.certificateThumbprint }}</code>
                  </template>
                </Column>
                <Column field="upn" header="UPN">
                  <template #body="{ data }">{{ data.upn || '-' }}</template>
                </Column>
                <Column field="type" header="Mapping Type">
                  <template #body="{ data }">
                    <Tag :value="mappingTypeLabel(data.type)" severity="info" />
                  </template>
                </Column>
                <Column field="mappedAt" header="Mapped At">
                  <template #body="{ data }">{{ formatDate(data.mappedAt) }}</template>
                </Column>
                <Column field="isEnabled" header="Status">
                  <template #body="{ data }">
                    <Tag :value="data.isEnabled ? 'Enabled' : 'Disabled'" :severity="data.isEnabled ? 'success' : 'warn'" />
                  </template>
                </Column>
                <Column header="" style="width: 60px;">
                  <template #body="{ data }">
                    <Button icon="pi pi-trash" text rounded size="small" severity="danger" @click="openDelete(data)" v-tooltip="'Delete'" />
                  </template>
                </Column>
              </DataTable>
            </div>
          </template>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <!-- Create Mapping Dialog -->
    <Dialog v-model:visible="createVisible" header="Map Certificate to User" :modal="true" style="width: 520px;">
      <div style="display: flex; flex-direction: column; gap: 1rem;">
        <div>
          <label style="font-size: 0.8125rem; font-weight: 600;">User DN</label>
          <InputText v-model="createForm.userDn" style="width: 100%; margin-top: 0.25rem;" />
        </div>
        <div>
          <label style="font-size: 0.8125rem; font-weight: 600;">Mapping Type</label>
          <Select
            v-model="createForm.mappingType"
            :options="mappingTypes"
            optionLabel="label"
            optionValue="value"
            style="width: 100%; margin-top: 0.25rem;"
          />
        </div>
        <div>
          <label style="font-size: 0.8125rem; font-weight: 600;">Certificate (DER file)</label>
          <div style="margin-top: 0.25rem;">
            <input type="file" accept=".cer,.crt,.der,.pem" @change="handleCertFile" />
          </div>
        </div>
        <div v-if="!createForm.certificateData">
          <label style="font-size: 0.8125rem; font-weight: 600;">Or paste Base64 certificate data</label>
          <Textarea v-model="createForm.certificateData" rows="4" style="width: 100%; margin-top: 0.25rem;" placeholder="Base64-encoded DER certificate..." />
        </div>
      </div>
      <div style="display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 1rem;">
        <Button label="Cancel" severity="secondary" outlined @click="createVisible = false" />
        <Button label="Create Mapping" icon="pi pi-check" :loading="createSaving" @click="doCreate" :disabled="!createForm.certificateData" />
      </div>
    </Dialog>

    <!-- Delete Confirm -->
    <Dialog v-model:visible="deleteVisible" header="Delete Mapping" :modal="true" style="width: 400px;">
      <p>Are you sure you want to delete this certificate mapping?</p>
      <div style="display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 1rem;">
        <Button label="Cancel" severity="secondary" outlined @click="deleteVisible = false" />
        <Button label="Delete" severity="danger" :loading="deleting" @click="doDelete" />
      </div>
    </Dialog>
  </div>
</template>
