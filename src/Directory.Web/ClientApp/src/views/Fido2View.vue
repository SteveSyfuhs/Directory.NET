<script setup lang="ts">
import { ref, computed } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import { useToast } from 'primevue/usetoast'
import {
  listCredentials,
  deleteCredential,
  renameCredential,
  registerSecurityKey,
  authenticateWithSecurityKey,
} from '../api/fido2'
import type { Fido2CredentialSummary } from '../types/fido2'

const toast = useToast()

// Search
const searchQuery = ref('')
const searching = ref(false)
const resolvedDn = ref('')

// Credentials
const credentials = ref<Fido2CredentialSummary[]>([])
const loading = ref(false)

// Register
const registering = ref(false)
const newKeyName = ref('')
const registerDialogVisible = ref(false)

// Rename
const renameDialogVisible = ref(false)
const renameTarget = ref<Fido2CredentialSummary | null>(null)
const renameName = ref('')
const renaming = ref(false)

// Delete
const deleteDialogVisible = ref(false)
const deleteTarget = ref<Fido2CredentialSummary | null>(null)
const deleting = ref(false)

// Authenticate
const authenticating = ref(false)

const hasUser = computed(() => resolvedDn.value !== '')

async function lookupUser() {
  if (!searchQuery.value.trim()) return
  searching.value = true
  resolvedDn.value = searchQuery.value.trim()
  try {
    await loadCredentials()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'User Not Found', detail: e.message, life: 5000 })
    resolvedDn.value = ''
    credentials.value = []
  } finally {
    searching.value = false
  }
}

async function loadCredentials() {
  loading.value = true
  try {
    credentials.value = await listCredentials(resolvedDn.value)
  } finally {
    loading.value = false
  }
}

function openRegisterDialog() {
  newKeyName.value = ''
  registerDialogVisible.value = true
}

async function doRegister() {
  registering.value = true
  try {
    const result = await registerSecurityKey(resolvedDn.value, newKeyName.value || undefined)
    if (result.success) {
      toast.add({ severity: 'success', summary: 'Key Registered', detail: 'Security key was registered successfully.', life: 5000 })
      registerDialogVisible.value = false
      await loadCredentials()
    } else {
      toast.add({ severity: 'error', summary: 'Registration Failed', detail: result.error || 'Unknown error', life: 5000 })
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Registration Failed', detail: e.message, life: 5000 })
  } finally {
    registering.value = false
  }
}

async function doAuthenticate() {
  authenticating.value = true
  try {
    const result = await authenticateWithSecurityKey(resolvedDn.value)
    if (result.success) {
      toast.add({ severity: 'success', summary: 'Authentication Successful', detail: `Authenticated as ${result.userDn}`, life: 5000 })
      await loadCredentials()
    } else {
      toast.add({ severity: 'error', summary: 'Authentication Failed', detail: result.error || 'Unknown error', life: 5000 })
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Authentication Failed', detail: e.message, life: 5000 })
  } finally {
    authenticating.value = false
  }
}

function openRenameDialog(cred: Fido2CredentialSummary) {
  renameTarget.value = cred
  renameName.value = cred.deviceName
  renameDialogVisible.value = true
}

async function doRename() {
  if (!renameTarget.value || !renameName.value.trim()) return
  renaming.value = true
  try {
    await renameCredential(resolvedDn.value, renameTarget.value.id, renameName.value.trim())
    toast.add({ severity: 'success', summary: 'Renamed', detail: 'Credential renamed.', life: 3000 })
    renameDialogVisible.value = false
    await loadCredentials()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    renaming.value = false
  }
}

function openDeleteDialog(cred: Fido2CredentialSummary) {
  deleteTarget.value = cred
  deleteDialogVisible.value = true
}

async function doDelete() {
  if (!deleteTarget.value) return
  deleting.value = true
  try {
    await deleteCredential(resolvedDn.value, deleteTarget.value.id)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'Credential deleted.', life: 3000 })
    deleteDialogVisible.value = false
    await loadCredentials()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    deleting.value = false
  }
}

function formatDate(dateStr: string | null) {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString()
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1><i class="pi pi-key" style="margin-right: 0.5rem;"></i>FIDO2 / WebAuthn</h1>
      <p>Manage FIDO2 security keys and passkeys for passwordless authentication.</p>
    </div>

    <!-- User Search -->
    <div class="card" style="margin-bottom: 1.5rem;">
      <div class="card-title">Find User</div>
      <div class="toolbar">
        <InputText
          v-model="searchQuery"
          placeholder="Enter DN, UPN, or sAMAccountName..."
          style="flex: 1; min-width: 300px;"
          @keyup.enter="lookupUser"
        />
        <Button
          label="Lookup"
          icon="pi pi-search"
          :loading="searching"
          @click="lookupUser"
        />
      </div>
    </div>

    <template v-if="hasUser">
      <!-- Credentials Table -->
      <div class="card" style="margin-bottom: 1.5rem;">
        <div class="toolbar">
          <div class="card-title" style="margin-bottom: 0;">Security Keys</div>
          <span style="flex: 1;"></span>
          <Button
            label="Test Authentication"
            icon="pi pi-sign-in"
            severity="info"
            outlined
            :loading="authenticating"
            :disabled="credentials.length === 0"
            @click="doAuthenticate"
          />
          <Button
            label="Register New Key"
            icon="pi pi-plus"
            @click="openRegisterDialog"
          />
        </div>

        <DataTable :value="credentials" :loading="loading" stripedRows>
          <template #empty>No security keys registered.</template>
          <Column field="deviceName" header="Name" />
          <Column field="attestationType" header="Attestation">
            <template #body="{ data }">
              <Tag :value="data.attestationType" severity="secondary" />
            </template>
          </Column>
          <Column field="transports" header="Transports">
            <template #body="{ data }">
              <Tag
                v-for="t in data.transports"
                :key="t"
                :value="t"
                severity="info"
                style="margin-right: 0.25rem;"
              />
              <span v-if="data.transports.length === 0">-</span>
            </template>
          </Column>
          <Column field="signCount" header="Sign Count" />
          <Column field="registeredAt" header="Registered">
            <template #body="{ data }">{{ formatDate(data.registeredAt) }}</template>
          </Column>
          <Column field="lastUsedAt" header="Last Used">
            <template #body="{ data }">{{ formatDate(data.lastUsedAt) }}</template>
          </Column>
          <Column field="isEnabled" header="Status">
            <template #body="{ data }">
              <Tag :value="data.isEnabled ? 'Enabled' : 'Disabled'" :severity="data.isEnabled ? 'success' : 'warn'" />
            </template>
          </Column>
          <Column header="Actions" style="width: 160px;">
            <template #body="{ data }">
              <div style="display: flex; gap: 0.25rem;">
                <Button icon="pi pi-pencil" text rounded size="small" @click="openRenameDialog(data)" v-tooltip="'Rename'" />
                <Button icon="pi pi-trash" text rounded size="small" severity="danger" @click="openDeleteDialog(data)" v-tooltip="'Delete'" />
              </div>
            </template>
          </Column>
        </DataTable>
      </div>
    </template>

    <!-- Register Dialog -->
    <Dialog
      v-model:visible="registerDialogVisible"
      header="Register Security Key"
      :modal="true"
      style="width: 450px;"
    >
      <p>Enter an optional name for this security key, then click Register. Your browser will prompt you to insert and activate your security key.</p>
      <InputText
        v-model="newKeyName"
        placeholder="e.g., YubiKey 5 NFC"
        style="width: 100%; margin-top: 0.5rem;"
      />
      <div style="display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 1rem;">
        <Button label="Cancel" severity="secondary" outlined @click="registerDialogVisible = false" />
        <Button label="Register" icon="pi pi-key" :loading="registering" @click="doRegister" />
      </div>
    </Dialog>

    <!-- Rename Dialog -->
    <Dialog
      v-model:visible="renameDialogVisible"
      header="Rename Security Key"
      :modal="true"
      style="width: 400px;"
    >
      <InputText
        v-model="renameName"
        placeholder="New name..."
        style="width: 100%;"
        @keyup.enter="doRename"
      />
      <div style="display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 1rem;">
        <Button label="Cancel" severity="secondary" outlined @click="renameDialogVisible = false" />
        <Button label="Rename" icon="pi pi-check" :loading="renaming" @click="doRename" />
      </div>
    </Dialog>

    <!-- Delete Confirmation -->
    <Dialog
      v-model:visible="deleteDialogVisible"
      header="Delete Security Key"
      :modal="true"
      style="width: 400px;"
    >
      <p>Are you sure you want to delete the security key "{{ deleteTarget?.deviceName }}"? This action cannot be undone.</p>
      <div style="display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 1rem;">
        <Button label="Cancel" severity="secondary" outlined @click="deleteDialogVisible = false" />
        <Button label="Delete" severity="danger" :loading="deleting" @click="doDelete" />
      </div>
    </Dialog>
  </div>
</template>
