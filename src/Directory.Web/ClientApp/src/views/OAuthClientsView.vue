<script setup lang="ts">
import { ref, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputSwitch from 'primevue/inputswitch'
import InputNumber from 'primevue/inputnumber'
import Tag from 'primevue/tag'
import Checkbox from 'primevue/checkbox'
import { useToast } from 'primevue/usetoast'
import type { OAuthClient, OAuthClientCreateResponse } from '../types/oauth'
import {
  fetchOAuthClients,
  createOAuthClient,
  updateOAuthClient,
  deleteOAuthClient,
  regenerateClientSecret,
} from '../api/oauth'

const toast = useToast()
const loading = ref(false)
const clients = ref<OAuthClient[]>([])

const showEditDialog = ref(false)
const editingClient = ref<Partial<OAuthClient>>({})
const isNew = ref(false)
const saving = ref(false)

const showSecretDialog = ref(false)
const visibleSecret = ref('')
const visibleClientId = ref('')

const allScopes = ['openid', 'profile', 'email', 'groups', 'offline_access']
const allGrantTypes = ['authorization_code', 'client_credentials', 'refresh_token']

// Multi-value redirect URI editor
const newRedirectUri = ref('')

onMounted(async () => {
  await loadClients()
})

async function loadClients() {
  loading.value = true
  try {
    clients.value = await fetchOAuthClients()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

function openCreate() {
  isNew.value = true
  editingClient.value = {
    clientName: '',
    redirectUris: [],
    allowedScopes: ['openid', 'profile', 'email'],
    allowedGrantTypes: ['authorization_code', 'refresh_token'],
    accessTokenLifetimeMinutes: 60,
    refreshTokenLifetimeDays: 30,
    requirePkce: true,
    isEnabled: true,
  }
  newRedirectUri.value = ''
  showEditDialog.value = true
}

function openEdit(client: OAuthClient) {
  isNew.value = false
  editingClient.value = {
    ...client,
    redirectUris: [...client.redirectUris],
    allowedScopes: [...client.allowedScopes],
    allowedGrantTypes: [...client.allowedGrantTypes],
  }
  newRedirectUri.value = ''
  showEditDialog.value = true
}

function addRedirectUri() {
  if (newRedirectUri.value && !editingClient.value.redirectUris?.includes(newRedirectUri.value)) {
    editingClient.value.redirectUris = [...(editingClient.value.redirectUris || []), newRedirectUri.value]
    newRedirectUri.value = ''
  }
}

function removeRedirectUri(uri: string) {
  editingClient.value.redirectUris = (editingClient.value.redirectUris || []).filter(u => u !== uri)
}

function toggleScope(scope: string) {
  const scopes = editingClient.value.allowedScopes || []
  if (scopes.includes(scope)) {
    editingClient.value.allowedScopes = scopes.filter(s => s !== scope)
  } else {
    editingClient.value.allowedScopes = [...scopes, scope]
  }
}

function toggleGrantType(gt: string) {
  const gts = editingClient.value.allowedGrantTypes || []
  if (gts.includes(gt)) {
    editingClient.value.allowedGrantTypes = gts.filter(g => g !== gt)
  } else {
    editingClient.value.allowedGrantTypes = [...gts, gt]
  }
}

async function saveClient() {
  saving.value = true
  try {
    if (isNew.value) {
      const created = await createOAuthClient(editingClient.value) as OAuthClientCreateResponse
      toast.add({ severity: 'success', summary: 'Created', detail: 'OAuth client created.', life: 3000 })
      // Show the secret
      visibleSecret.value = created.clientSecret
      visibleClientId.value = created.clientId
      showSecretDialog.value = true
    } else {
      await updateOAuthClient(editingClient.value.clientId!, editingClient.value)
      toast.add({ severity: 'success', summary: 'Updated', detail: 'OAuth client updated.', life: 3000 })
    }
    showEditDialog.value = false
    await loadClients()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function confirmDelete(client: OAuthClient) {
  if (!confirm(`Delete OAuth client "${client.clientName}"?`)) return
  try {
    await deleteOAuthClient(client.clientId)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'OAuth client deleted.', life: 3000 })
    await loadClients()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function regenSecret(client: OAuthClient) {
  if (!confirm(`Regenerate secret for "${client.clientName}"? The old secret will stop working immediately.`)) return
  try {
    const result = await regenerateClientSecret(client.clientId)
    visibleSecret.value = result.clientSecret
    visibleClientId.value = client.clientId
    showSecretDialog.value = true
    toast.add({ severity: 'success', summary: 'Regenerated', detail: 'Client secret regenerated.', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function toggleEnabled(client: OAuthClient) {
  try {
    await updateOAuthClient(client.clientId, { ...client, isEnabled: !client.isEnabled })
    await loadClients()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function copyToClipboard(text: string, label: string) {
  navigator.clipboard.writeText(text)
  toast.add({ severity: 'info', summary: 'Copied', detail: `${label} copied to clipboard.`, life: 2000 })
}

function formatDate(d: string) {
  return new Date(d).toLocaleString()
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1><i class="pi pi-globe" style="margin-right: 0.5rem"></i>OAuth / OIDC Clients</h1>
      <p>Manage OAuth 2.0 and OpenID Connect client registrations.</p>
    </div>

    <div class="card">
      <div class="toolbar">
        <Button label="Refresh" icon="pi pi-refresh" severity="secondary" @click="loadClients" :loading="loading" />
        <span class="toolbar-spacer" />
        <Button label="New Client" icon="pi pi-plus" @click="openCreate" />
      </div>

      <DataTable :value="clients" :loading="loading" stripedRows>
        <Column field="clientName" header="Name" sortable>
          <template #body="{ data }">
            <strong>{{ data.clientName }}</strong>
          </template>
        </Column>
        <Column field="clientId" header="Client ID">
          <template #body="{ data }">
            <div style="display: flex; align-items: center; gap: 0.25rem">
              <span style="font-family: monospace; font-size: 0.8125rem">{{ data.clientId }}</span>
              <Button icon="pi pi-copy" text rounded size="small" v-tooltip="'Copy Client ID'" @click="copyToClipboard(data.clientId, 'Client ID')" />
            </div>
          </template>
        </Column>
        <Column field="allowedGrantTypes" header="Grant Types">
          <template #body="{ data }">
            <div style="display: flex; flex-wrap: wrap; gap: 0.25rem">
              <Tag v-for="gt in data.allowedGrantTypes" :key="gt" :value="gt" severity="info" style="font-size: 0.7rem" />
            </div>
          </template>
        </Column>
        <Column field="allowedScopes" header="Scopes">
          <template #body="{ data }">
            <div style="display: flex; flex-wrap: wrap; gap: 0.25rem">
              <Tag v-for="s in data.allowedScopes.slice(0, 3)" :key="s" :value="s" severity="secondary" style="font-size: 0.7rem" />
              <Tag v-if="data.allowedScopes.length > 3" :value="`+${data.allowedScopes.length - 3}`" severity="secondary" style="font-size: 0.7rem" />
            </div>
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
        <Column header="Actions" style="width: 14rem">
          <template #body="{ data }">
            <Button icon="pi pi-key" severity="warn" text rounded v-tooltip="'Regenerate Secret'" @click="regenSecret(data)" />
            <Button icon="pi pi-pencil" text rounded v-tooltip="'Edit'" @click="openEdit(data)" />
            <Button icon="pi pi-trash" severity="danger" text rounded v-tooltip="'Delete'" @click="confirmDelete(data)" />
          </template>
        </Column>
      </DataTable>
    </div>

    <!-- Create/Edit Dialog -->
    <Dialog v-model:visible="showEditDialog" :header="isNew ? 'New OAuth Client' : 'Edit OAuth Client'" modal style="width: 44rem">
      <div style="display: flex; flex-direction: column; gap: 1rem">
        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Client Name</label>
          <InputText v-model="editingClient.clientName" style="width: 100%" placeholder="e.g., My Web Application" />
        </div>

        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Redirect URIs</label>
          <div style="display: flex; gap: 0.5rem; margin-bottom: 0.5rem">
            <InputText v-model="newRedirectUri" style="flex: 1" placeholder="https://app.example.com/callback" @keyup.enter="addRedirectUri" />
            <Button icon="pi pi-plus" severity="secondary" @click="addRedirectUri" />
          </div>
          <div v-if="editingClient.redirectUris?.length" style="display: flex; flex-direction: column; gap: 0.25rem">
            <div v-for="uri in editingClient.redirectUris" :key="uri" style="display: flex; align-items: center; gap: 0.5rem; background: var(--p-surface-ground); padding: 0.375rem 0.75rem; border-radius: 4px; font-size: 0.8125rem; font-family: monospace">
              <span style="flex: 1">{{ uri }}</span>
              <Button icon="pi pi-times" severity="danger" text rounded size="small" @click="removeRedirectUri(uri)" />
            </div>
          </div>
          <div v-else style="font-size: 0.75rem; color: var(--p-text-muted-color)">No redirect URIs added.</div>
        </div>

        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.5rem">Allowed Scopes</label>
          <div style="display: flex; flex-wrap: wrap; gap: 0.75rem">
            <div v-for="scope in allScopes" :key="scope" style="display: flex; align-items: center; gap: 0.375rem">
              <Checkbox :modelValue="editingClient.allowedScopes?.includes(scope)" :binary="true" @update:modelValue="toggleScope(scope)" :inputId="'scope-' + scope" />
              <label :for="'scope-' + scope" style="font-size: 0.875rem; cursor: pointer">{{ scope }}</label>
            </div>
          </div>
        </div>

        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.5rem">Grant Types</label>
          <div style="display: flex; flex-wrap: wrap; gap: 0.75rem">
            <div v-for="gt in allGrantTypes" :key="gt" style="display: flex; align-items: center; gap: 0.375rem">
              <Checkbox :modelValue="editingClient.allowedGrantTypes?.includes(gt)" :binary="true" @update:modelValue="toggleGrantType(gt)" :inputId="'gt-' + gt" />
              <label :for="'gt-' + gt" style="font-size: 0.875rem; cursor: pointer">{{ gt }}</label>
            </div>
          </div>
        </div>

        <div style="display: flex; gap: 1.5rem">
          <div style="flex: 1">
            <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Access Token Lifetime (min)</label>
            <InputNumber v-model="editingClient.accessTokenLifetimeMinutes" :min="1" :max="1440" style="width: 100%" />
          </div>
          <div style="flex: 1">
            <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Refresh Token Lifetime (days)</label>
            <InputNumber v-model="editingClient.refreshTokenLifetimeDays" :min="1" :max="365" style="width: 100%" />
          </div>
        </div>

        <div style="display: flex; align-items: center; gap: 1.5rem">
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <InputSwitch v-model="editingClient.requirePkce" />
            <label style="font-size: 0.875rem">Require PKCE</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <InputSwitch v-model="editingClient.isEnabled" />
            <label style="font-size: 0.875rem">Enabled</label>
          </div>
        </div>

        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Logo URI (optional)</label>
          <InputText v-model="editingClient.logoUri" style="width: 100%" placeholder="https://example.com/logo.png" />
        </div>
      </div>

      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showEditDialog = false" />
        <Button :label="isNew ? 'Create' : 'Save'" icon="pi pi-check" @click="saveClient" :loading="saving" />
      </template>
    </Dialog>

    <!-- Secret Dialog -->
    <Dialog v-model:visible="showSecretDialog" header="Client Credentials" modal style="width: 36rem">
      <div style="margin-bottom: 1rem; padding: 0.75rem; background: var(--app-warn-bg); border: 1px solid var(--app-warn-border); border-radius: 6px; color: var(--app-warn-text-strong); font-size: 0.8125rem">
        Copy the client secret now. It will not be shown again.
      </div>

      <div style="margin-bottom: 1rem">
        <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Client ID</label>
        <div style="display: flex; align-items: center; gap: 0.5rem">
          <InputText :modelValue="visibleClientId" readonly style="flex: 1; font-family: monospace; font-size: 0.8125rem" />
          <Button icon="pi pi-copy" severity="secondary" v-tooltip="'Copy'" @click="copyToClipboard(visibleClientId, 'Client ID')" />
        </div>
      </div>

      <div>
        <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Client Secret</label>
        <div style="display: flex; align-items: center; gap: 0.5rem">
          <InputText :modelValue="visibleSecret" readonly style="flex: 1; font-family: monospace; font-size: 0.8125rem" />
          <Button icon="pi pi-copy" severity="secondary" v-tooltip="'Copy'" @click="copyToClipboard(visibleSecret, 'Client Secret')" />
        </div>
      </div>
    </Dialog>
  </div>
</template>
