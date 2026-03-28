<script setup lang="ts">
import { ref, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputSwitch from 'primevue/inputswitch'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import Textarea from 'primevue/textarea'
import { useToast } from 'primevue/usetoast'
import type { SamlServiceProvider, SamlAttributeMapping } from '../types/saml'
import {
  fetchSamlProviders,
  createSamlProvider,
  updateSamlProvider,
  deleteSamlProvider,
  getSamlMetadataUrl,
} from '../api/saml'

const toast = useToast()
const loading = ref(false)
const providers = ref<SamlServiceProvider[]>([])

const showEditDialog = ref(false)
const editingSp = ref<Partial<SamlServiceProvider>>({})
const isNew = ref(false)
const saving = ref(false)

const nameIdFormats = [
  { label: 'Email Address', value: 'urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress' },
  { label: 'Persistent', value: 'urn:oasis:names:tc:SAML:2.0:nameid-format:persistent' },
  { label: 'Transient', value: 'urn:oasis:names:tc:SAML:2.0:nameid-format:transient' },
  { label: 'Unspecified', value: 'urn:oasis:names:tc:SAML:2.0:nameid-format:unspecified' },
]

// Attribute mapping editor
const newMappingSaml = ref('')
const newMappingDir = ref('')

onMounted(async () => {
  await loadProviders()
})

async function loadProviders() {
  loading.value = true
  try {
    providers.value = await fetchSamlProviders()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

function openCreate() {
  isNew.value = true
  editingSp.value = {
    entityId: '',
    name: '',
    assertionConsumerServiceUrl: '',
    singleLogoutServiceUrl: null,
    certificate: null,
    nameIdFormat: 'urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress',
    attributeMappings: [],
    isEnabled: true,
  }
  newMappingSaml.value = ''
  newMappingDir.value = ''
  showEditDialog.value = true
}

function openEdit(sp: SamlServiceProvider) {
  isNew.value = false
  editingSp.value = {
    ...sp,
    attributeMappings: sp.attributeMappings ? sp.attributeMappings.map(m => ({ ...m })) : [],
  }
  newMappingSaml.value = ''
  newMappingDir.value = ''
  showEditDialog.value = true
}

function addAttributeMapping() {
  if (newMappingSaml.value && newMappingDir.value) {
    const mappings = editingSp.value.attributeMappings || []
    mappings.push({
      samlAttributeName: newMappingSaml.value,
      directoryAttribute: newMappingDir.value,
    })
    editingSp.value.attributeMappings = [...mappings]
    newMappingSaml.value = ''
    newMappingDir.value = ''
  }
}

function removeAttributeMapping(index: number) {
  const mappings = editingSp.value.attributeMappings || []
  mappings.splice(index, 1)
  editingSp.value.attributeMappings = [...mappings]
}

async function saveSp() {
  saving.value = true
  try {
    if (isNew.value) {
      await createSamlProvider(editingSp.value)
      toast.add({ severity: 'success', summary: 'Created', detail: 'SAML service provider created.', life: 3000 })
    } else {
      await updateSamlProvider(editingSp.value.id!, editingSp.value)
      toast.add({ severity: 'success', summary: 'Updated', detail: 'SAML service provider updated.', life: 3000 })
    }
    showEditDialog.value = false
    await loadProviders()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function confirmDelete(sp: SamlServiceProvider) {
  if (!confirm(`Delete SAML service provider "${sp.name}"?`)) return
  try {
    await deleteSamlProvider(sp.id)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'SAML service provider deleted.', life: 3000 })
    await loadProviders()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function toggleEnabled(sp: SamlServiceProvider) {
  try {
    await updateSamlProvider(sp.id, { ...sp, isEnabled: !sp.isEnabled })
    await loadProviders()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function downloadMetadata() {
  window.open(getSamlMetadataUrl(), '_blank')
}

function formatDate(d: string) {
  return new Date(d).toLocaleString()
}

function shortNameIdFormat(format: string): string {
  const found = nameIdFormats.find(f => f.value === format)
  return found ? found.label : format.split(':').pop() || format
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1><i class="pi pi-globe" style="margin-right: 0.5rem"></i>SAML 2.0 Service Providers</h1>
      <p>Manage SAML 2.0 Service Provider trust relationships.</p>
    </div>

    <div class="card">
      <div class="toolbar">
        <Button label="Refresh" icon="pi pi-refresh" severity="secondary" @click="loadProviders" :loading="loading" />
        <Button label="IdP Metadata" icon="pi pi-download" severity="info" @click="downloadMetadata" />
        <span class="toolbar-spacer" />
        <Button label="New Service Provider" icon="pi pi-plus" @click="openCreate" />
      </div>

      <DataTable :value="providers" :loading="loading" stripedRows>
        <Column field="name" header="Name" sortable>
          <template #body="{ data }">
            <strong>{{ data.name }}</strong>
          </template>
        </Column>
        <Column field="entityId" header="Entity ID">
          <template #body="{ data }">
            <span style="font-size: 0.8125rem; word-break: break-all">{{ data.entityId }}</span>
          </template>
        </Column>
        <Column field="assertionConsumerServiceUrl" header="ACS URL">
          <template #body="{ data }">
            <span style="font-size: 0.8125rem; word-break: break-all">{{ data.assertionConsumerServiceUrl }}</span>
          </template>
        </Column>
        <Column field="nameIdFormat" header="NameID Format">
          <template #body="{ data }">
            <Tag :value="shortNameIdFormat(data.nameIdFormat)" severity="info" style="font-size: 0.7rem" />
          </template>
        </Column>
        <Column field="isEnabled" header="Enabled" style="width: 6rem">
          <template #body="{ data }">
            <InputSwitch :modelValue="data.isEnabled" @update:modelValue="toggleEnabled(data)" />
          </template>
        </Column>
        <Column field="createdAt" header="Created" sortable>
          <template #body="{ data }">{{ formatDate(data.createdAt) }}</template>
        </Column>
        <Column header="Actions" style="width: 10rem">
          <template #body="{ data }">
            <Button icon="pi pi-pencil" text rounded v-tooltip="'Edit'" @click="openEdit(data)" />
            <Button icon="pi pi-trash" severity="danger" text rounded v-tooltip="'Delete'" @click="confirmDelete(data)" />
          </template>
        </Column>
      </DataTable>
    </div>

    <!-- Create/Edit Dialog -->
    <Dialog v-model:visible="showEditDialog" :header="isNew ? 'New SAML Service Provider' : 'Edit SAML Service Provider'" modal style="width: 48rem">
      <div style="display: flex; flex-direction: column; gap: 1rem">
        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Name</label>
          <InputText v-model="editingSp.name" style="width: 100%" placeholder="e.g., Salesforce" />
        </div>

        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Entity ID</label>
          <InputText v-model="editingSp.entityId" style="width: 100%" placeholder="https://sp.example.com/saml/metadata" />
        </div>

        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Assertion Consumer Service (ACS) URL</label>
          <InputText v-model="editingSp.assertionConsumerServiceUrl" style="width: 100%" placeholder="https://sp.example.com/saml/acs" />
        </div>

        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Single Logout Service URL (optional)</label>
          <InputText v-model="editingSp.singleLogoutServiceUrl" style="width: 100%" placeholder="https://sp.example.com/saml/slo" />
        </div>

        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">NameID Format</label>
          <Select
            v-model="editingSp.nameIdFormat"
            :options="nameIdFormats"
            optionLabel="label"
            optionValue="value"
            style="width: 100%"
          />
        </div>

        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">SP Certificate (Base64 DER, optional)</label>
          <Textarea v-model="editingSp.certificate" rows="3" style="width: 100%; font-family: monospace; font-size: 0.75rem" placeholder="MIICpDCCAYwCCQD..." />
          <div style="font-size: 0.75rem; color: var(--p-text-muted-color); margin-top: 0.25rem">
            Used for encrypting assertions. Leave empty if the SP does not require encrypted assertions.
          </div>
        </div>

        <!-- Attribute Mappings -->
        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.5rem">Attribute Mappings</label>
          <div style="display: flex; gap: 0.5rem; margin-bottom: 0.5rem">
            <InputText v-model="newMappingSaml" style="flex: 1" placeholder="SAML Attribute (e.g., email)" />
            <InputText v-model="newMappingDir" style="flex: 1" placeholder="Directory Attribute (e.g., userPrincipalName)" />
            <Button icon="pi pi-plus" severity="secondary" @click="addAttributeMapping" />
          </div>
          <div v-if="editingSp.attributeMappings?.length" style="display: flex; flex-direction: column; gap: 0.25rem">
            <div v-for="(mapping, idx) in editingSp.attributeMappings" :key="idx"
                 style="display: flex; align-items: center; gap: 0.5rem; background: var(--p-surface-ground); padding: 0.375rem 0.75rem; border-radius: 4px; font-size: 0.8125rem">
              <span style="flex: 1; font-family: monospace">{{ mapping.samlAttributeName }}</span>
              <i class="pi pi-arrow-right" style="color: var(--p-text-muted-color); font-size: 0.75rem"></i>
              <span style="flex: 1; font-family: monospace">{{ mapping.directoryAttribute }}</span>
              <Button icon="pi pi-times" severity="danger" text rounded size="small" @click="removeAttributeMapping(idx)" />
            </div>
          </div>
          <div v-else style="font-size: 0.75rem; color: var(--p-text-muted-color)">
            No custom mappings. Default claims (email, name, givenName, surname, UPN) will be included.
          </div>
        </div>

        <div style="display: flex; align-items: center; gap: 0.5rem">
          <InputSwitch v-model="editingSp.isEnabled" />
          <label style="font-size: 0.875rem">Enabled</label>
        </div>
      </div>

      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showEditDialog = false" />
        <Button :label="isNew ? 'Create' : 'Save'" icon="pi pi-check" @click="saveSp" :loading="saving" />
      </template>
    </Dialog>
  </div>
</template>
