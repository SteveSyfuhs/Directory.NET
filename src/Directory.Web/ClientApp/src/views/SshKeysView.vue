<script setup lang="ts">
import { ref, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import { useToast } from 'primevue/usetoast'
import { listKeys, addKey, deleteKey } from '../api/sshKeys'
import type { SshPublicKey } from '../types/sshKeys'

const toast = useToast()

// ── User Search ───────────────────────────────────────────────
const userDnInput = ref('')
const currentUserDn = ref('')
const keys = ref<SshPublicKey[]>([])
const loading = ref(false)

async function searchUser() {
  const dn = userDnInput.value.trim()
  if (!dn) return
  loading.value = true
  currentUserDn.value = dn
  try {
    keys.value = await listKeys(dn)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
    keys.value = []
  } finally {
    loading.value = false
  }
}

// ── Add Key ───────────────────────────────────────────────────
const addVisible = ref(false)
const addSaving = ref(false)
const publicKeyInput = ref('')

function openAdd() {
  publicKeyInput.value = ''
  addVisible.value = true
}

async function doAddKey() {
  addSaving.value = true
  try {
    await addKey(currentUserDn.value, publicKeyInput.value)
    toast.add({ severity: 'success', summary: 'SSH key added', life: 3000 })
    addVisible.value = false
    await searchUser()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    addSaving.value = false
  }
}

// ── Delete Key ────────────────────────────────────────────────
const deleteVisible = ref(false)
const deleteTarget = ref<SshPublicKey | null>(null)
const deleting = ref(false)

function confirmDelete(key: SshPublicKey) {
  deleteTarget.value = key
  deleteVisible.value = true
}

async function doDelete() {
  if (!deleteTarget.value) return
  deleting.value = true
  try {
    await deleteKey(deleteTarget.value.id, currentUserDn.value)
    toast.add({ severity: 'success', summary: 'SSH key deleted', life: 3000 })
    deleteVisible.value = false
    await searchUser()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    deleting.value = false
  }
}

function truncateKey(data: string): string {
  if (data.length <= 20) return data
  return data.substring(0, 10) + '...' + data.substring(data.length - 10)
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>SSH Key Management</h1>
      <p>Manage SSH public keys for directory users. Integrates with OpenSSH AuthorizedKeysCommand.</p>
    </div>

    <!-- User Search -->
    <div class="card" style="margin-bottom: 1.5rem">
      <h3 style="margin-top: 0">Look Up User</h3>
      <div style="display: flex; gap: 0.5rem; align-items: flex-end">
        <div style="flex: 1">
          <label class="field-label">User DN</label>
          <InputText v-model="userDnInput" style="width: 100%" placeholder="CN=jdoe,OU=Users,DC=example,DC=com" @keyup.enter="searchUser" />
        </div>
        <Button label="Load Keys" icon="pi pi-search" @click="searchUser" :loading="loading" />
      </div>
    </div>

    <!-- Keys Table -->
    <div v-if="currentUserDn" class="card">
      <div class="toolbar">
        <h3 style="margin: 0">SSH Keys for {{ currentUserDn }}</h3>
        <span class="toolbar-spacer"></span>
        <Button label="Add Key" icon="pi pi-plus" @click="openAdd" />
      </div>
      <DataTable :value="keys" :loading="loading" stripedRows>
        <Column field="keyType" header="Type" sortable style="width: 160px">
          <template #body="{ data }">
            <Tag :value="data.keyType" />
          </template>
        </Column>
        <Column field="fingerprint" header="Fingerprint" sortable />
        <Column field="comment" header="Comment" sortable />
        <Column header="Status" style="width: 100px">
          <template #body="{ data }">
            <Tag :value="data.isEnabled ? 'Enabled' : 'Disabled'" :severity="data.isEnabled ? 'success' : 'danger'" />
          </template>
        </Column>
        <Column header="Added" style="width: 160px">
          <template #body="{ data }">{{ new Date(data.addedAt).toLocaleString() }}</template>
        </Column>
        <Column header="Actions" style="width: 80px">
          <template #body="{ data }">
            <Button icon="pi pi-trash" text rounded severity="danger" @click="confirmDelete(data)" />
          </template>
        </Column>
        <template #empty>
          <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
            No SSH keys found for this user.
          </div>
        </template>
      </DataTable>
    </div>

    <!-- Add Key Dialog -->
    <Dialog v-model:visible="addVisible" header="Add SSH Public Key" :style="{ width: '600px' }" modal>
      <div class="field" style="margin-bottom: 1rem">
        <label class="field-label">Public Key</label>
        <Textarea v-model="publicKeyInput" rows="5" style="width: 100%; font-family: monospace; font-size: 0.8125rem"
          placeholder="ssh-ed25519 AAAAC3Nza... user@host" />
        <small style="color: var(--p-text-muted-color)">Paste the full SSH public key in OpenSSH format (ssh-rsa, ssh-ed25519, ecdsa-sha2-nistp256, etc.)</small>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="addVisible = false" />
        <Button label="Add Key" icon="pi pi-plus" :loading="addSaving" :disabled="!publicKeyInput.trim()" @click="doAddKey" />
      </template>
    </Dialog>

    <!-- Delete Confirmation -->
    <Dialog v-model:visible="deleteVisible" header="Delete SSH Key" :style="{ width: '450px' }" modal>
      <p>Are you sure you want to delete this SSH key?</p>
      <div v-if="deleteTarget" style="background: var(--p-surface-ground); border-radius: 6px; padding: 0.75rem; font-size: 0.875rem">
        <div><strong>Type:</strong> {{ deleteTarget.keyType }}</div>
        <div><strong>Fingerprint:</strong> {{ deleteTarget.fingerprint }}</div>
        <div v-if="deleteTarget.comment"><strong>Comment:</strong> {{ deleteTarget.comment }}</div>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="deleteVisible = false" />
        <Button label="Delete" icon="pi pi-trash" severity="danger" :loading="deleting" @click="doDelete" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.field-label {
  display: block;
  font-weight: 600;
  margin-bottom: 0.375rem;
  font-size: 0.875rem;
}
</style>
