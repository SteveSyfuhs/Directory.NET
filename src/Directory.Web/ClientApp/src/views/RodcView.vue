<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import InputText from 'primevue/inputtext'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import ProgressSpinner from 'primevue/progressspinner'
import ConfirmDialog from 'primevue/confirmdialog'
import Message from 'primevue/message'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import {
  getRodcSettings,
  updateRodcSettings,
  getPasswordCache,
  addPasswordCachePrincipal,
  removePasswordCachePrincipal,
  triggerReplication,
} from '../api/rodc'
import type { RodcSettings, PasswordCacheResponse } from '../types/rodc'

const toast = useToast()
const confirm = useConfirm()

const loading = ref(true)
const settings = ref<RodcSettings | null>(null)
const passwordCache = ref<PasswordCacheResponse | null>(null)
const replicating = ref(false)
const toggling = ref(false)
const newAllowedPrincipal = ref('')
const newDeniedPrincipal = ref('')
const addingAllowed = ref(false)
const addingDenied = ref(false)
const fullDcEndpoint = ref('')
const savingEndpoint = ref(false)

onMounted(() => loadData())

async function loadData() {
  loading.value = true
  try {
    const [s, pc] = await Promise.all([getRodcSettings(), getPasswordCache()])
    settings.value = s
    passwordCache.value = pc
    fullDcEndpoint.value = s.fullDcEndpoint
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

function onToggleRodc() {
  if (!settings.value) return
  const enabling = !settings.value.isRodc
  confirm.require({
    message: enabling
      ? 'Enable RODC mode? All write operations to API endpoints will be blocked.'
      : 'Disable RODC mode? This DC will accept write operations again.',
    header: enabling ? 'Enable Read-Only Mode' : 'Disable Read-Only Mode',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: enabling ? 'p-button-danger' : 'p-button-success',
    accept: async () => {
      toggling.value = true
      try {
        settings.value = await updateRodcSettings({ isRodc: enabling })
        toast.add({
          severity: 'success',
          summary: 'RODC Mode',
          detail: `RODC mode ${enabling ? 'enabled' : 'disabled'}`,
          life: 3000,
        })
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      } finally {
        toggling.value = false
      }
    },
  })
}

async function onSaveEndpoint() {
  savingEndpoint.value = true
  try {
    settings.value = await updateRodcSettings({ fullDcEndpoint: fullDcEndpoint.value })
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Writable DC endpoint updated', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    savingEndpoint.value = false
  }
}

async function onTriggerReplication() {
  replicating.value = true
  try {
    const result = await triggerReplication()
    toast.add({
      severity: result.success ? 'success' : 'error',
      summary: 'Replication',
      detail: result.message,
      life: 5000,
    })
    await loadData()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Replication Error', detail: e.message, life: 5000 })
  } finally {
    replicating.value = false
  }
}

async function onAddAllowed() {
  if (!newAllowedPrincipal.value.trim()) return
  addingAllowed.value = true
  try {
    const result = await addPasswordCachePrincipal({ principal: newAllowedPrincipal.value.trim(), list: 'allowed' })
    if (settings.value) {
      settings.value.passwordReplicationAllowed = result.allowedPrincipals
      settings.value.passwordReplicationDenied = result.deniedPrincipals
    }
    newAllowedPrincipal.value = ''
    toast.add({ severity: 'success', summary: 'Added', detail: 'Principal added to allowed list', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    addingAllowed.value = false
  }
}

async function onRemoveAllowed(principal: string) {
  try {
    const result = await removePasswordCachePrincipal({ principal, list: 'allowed' })
    if (settings.value) {
      settings.value.passwordReplicationAllowed = result.allowedPrincipals
    }
    toast.add({ severity: 'success', summary: 'Removed', detail: 'Principal removed from allowed list', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onAddDenied() {
  if (!newDeniedPrincipal.value.trim()) return
  addingDenied.value = true
  try {
    const result = await addPasswordCachePrincipal({ principal: newDeniedPrincipal.value.trim(), list: 'denied' })
    if (settings.value) {
      settings.value.passwordReplicationAllowed = result.allowedPrincipals
      settings.value.passwordReplicationDenied = result.deniedPrincipals
    }
    newDeniedPrincipal.value = ''
    toast.add({ severity: 'success', summary: 'Added', detail: 'Principal added to denied list', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    addingDenied.value = false
  }
}

async function onRemoveDenied(principal: string) {
  try {
    const result = await removePasswordCachePrincipal({ principal, list: 'denied' })
    if (settings.value) {
      settings.value.passwordReplicationDenied = result.deniedPrincipals
    }
    toast.add({ severity: 'success', summary: 'Removed', detail: 'Principal removed from denied list', life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Read-Only Domain Controller (RODC)</h1>
      <p>Manage RODC mode, password replication policy, and replication settings</p>
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else-if="settings">
      <!-- RODC Warning Banner -->
      <Message v-if="settings.isRodc" severity="warn" :closable="false" style="margin-bottom: 1.5rem">
        This domain controller is operating in <strong>Read-Only</strong> mode. All write operations to API endpoints are blocked.
        Direct writes to the writable DC at <strong>{{ settings.fullDcEndpoint || '(not configured)' }}</strong>.
      </Message>

      <!-- Status Card -->
      <div class="stat-grid">
        <div class="stat-card">
          <div class="stat-icon" :class="settings.isRodc ? 'amber' : 'green'">
            <i :class="settings.isRodc ? 'pi pi-eye' : 'pi pi-pencil'"></i>
          </div>
          <div>
            <div class="stat-value" style="font-size: 1.25rem">{{ settings.isRodc ? 'Read-Only' : 'Read-Write' }}</div>
            <div class="stat-label">DC Mode</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon blue">
            <i class="pi pi-server"></i>
          </div>
          <div>
            <div class="stat-value" style="font-size: 1rem; word-break: break-all">{{ settings.fullDcEndpoint || 'Not configured' }}</div>
            <div class="stat-label">Writable DC</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon purple">
            <i class="pi pi-sync"></i>
          </div>
          <div>
            <div class="stat-value" style="font-size: 1rem">{{ settings.lastReplicationTime ? new Date(settings.lastReplicationTime).toLocaleString() : 'Never' }}</div>
            <div class="stat-label">Last Replication</div>
          </div>
        </div>
      </div>

      <!-- RODC Toggle -->
      <div class="card" style="margin-bottom: 1.5rem">
        <div style="display: flex; align-items: center; gap: 1rem">
          <div style="flex: 1">
            <h2 style="margin: 0; font-size: 1rem; font-weight: 600">RODC Mode</h2>
            <p style="margin: 0.25rem 0 0; font-size: 0.8125rem; color: var(--p-text-muted-color)">
              When enabled, all POST/PUT/DELETE API requests will be rejected with HTTP 403.
            </p>
          </div>
          <Tag :value="settings.isRodc ? 'Enabled' : 'Disabled'" :severity="settings.isRodc ? 'warn' : 'success'" />
          <Button
            :label="settings.isRodc ? 'Disable RODC Mode' : 'Enable RODC Mode'"
            :severity="settings.isRodc ? 'success' : 'danger'"
            :icon="settings.isRodc ? 'pi pi-pencil' : 'pi pi-eye'"
            size="small"
            :loading="toggling"
            @click="onToggleRodc"
          />
        </div>
      </div>

      <!-- Writable DC Endpoint -->
      <div class="card" style="margin-bottom: 1.5rem">
        <h2 style="margin: 0 0 0.75rem; font-size: 1rem; font-weight: 600">Writable DC Endpoint</h2>
        <p style="margin: 0 0 0.75rem; font-size: 0.8125rem; color: var(--p-text-muted-color)">
          URL of the writable domain controller to forward writes to and pull replication from.
        </p>
        <div style="display: flex; gap: 0.5rem; align-items: center">
          <InputText v-model="fullDcEndpoint" placeholder="https://dc1.example.com" style="flex: 1" />
          <Button label="Save" icon="pi pi-check" size="small" :loading="savingEndpoint" @click="onSaveEndpoint" />
        </div>
      </div>

      <!-- Replication -->
      <div class="card" style="margin-bottom: 1.5rem">
        <div style="display: flex; align-items: center; gap: 1rem">
          <div style="flex: 1">
            <h2 style="margin: 0; font-size: 1rem; font-weight: 600">Replication</h2>
            <p style="margin: 0.25rem 0 0; font-size: 0.8125rem; color: var(--p-text-muted-color)">
              Pull changes from the writable DC. In RODC mode, replication is pull-only (no outbound changes).
            </p>
          </div>
          <Button
            label="Pull Replication"
            icon="pi pi-sync"
            size="small"
            :loading="replicating"
            :disabled="!settings.isRodc || !settings.fullDcEndpoint"
            @click="onTriggerReplication"
          />
        </div>
      </div>

      <!-- Password Replication Policy -->
      <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1.5rem">
        <!-- Allowed List -->
        <div class="card">
          <h2 style="margin: 0 0 0.5rem; font-size: 1rem; font-weight: 600; color: var(--app-success-text)">
            <i class="pi pi-check-circle" style="margin-right: 0.5rem"></i>Allowed Password Replication
          </h2>
          <p style="margin: 0 0 0.75rem; font-size: 0.8125rem; color: var(--p-text-muted-color)">
            Principals whose passwords can be cached on this RODC.
          </p>
          <div style="display: flex; gap: 0.5rem; margin-bottom: 0.75rem">
            <InputText v-model="newAllowedPrincipal" placeholder="CN=User,DC=example,DC=com" style="flex: 1" size="small" @keydown.enter="onAddAllowed" />
            <Button icon="pi pi-plus" size="small" :loading="addingAllowed" @click="onAddAllowed" />
          </div>
          <DataTable :value="settings.passwordReplicationAllowed.map((p, i) => ({ id: i, principal: p }))" size="small" stripedRows>
            <Column field="principal" header="Principal DN">
              <template #body="{ data }">
                <span style="font-family: monospace; font-size: 0.8125rem">{{ data.principal }}</span>
              </template>
            </Column>
            <Column header="" style="width: 3rem">
              <template #body="{ data }">
                <Button icon="pi pi-times" size="small" severity="danger" text @click="onRemoveAllowed(data.principal)" />
              </template>
            </Column>
            <template #empty>
              <div style="text-align: center; padding: 1rem; color: var(--p-text-muted-color); font-size: 0.8125rem">
                No principals in the allowed list.
              </div>
            </template>
          </DataTable>
        </div>

        <!-- Denied List -->
        <div class="card">
          <h2 style="margin: 0 0 0.5rem; font-size: 1rem; font-weight: 600; color: var(--app-danger-text)">
            <i class="pi pi-ban" style="margin-right: 0.5rem"></i>Denied Password Replication
          </h2>
          <p style="margin: 0 0 0.75rem; font-size: 0.8125rem; color: var(--p-text-muted-color)">
            Principals whose passwords are never cached. Denied list takes priority over allowed.
          </p>
          <div style="display: flex; gap: 0.5rem; margin-bottom: 0.75rem">
            <InputText v-model="newDeniedPrincipal" placeholder="CN=Admin,DC=example,DC=com" style="flex: 1" size="small" @keydown.enter="onAddDenied" />
            <Button icon="pi pi-plus" size="small" severity="danger" :loading="addingDenied" @click="onAddDenied" />
          </div>
          <DataTable :value="settings.passwordReplicationDenied.map((p, i) => ({ id: i, principal: p }))" size="small" stripedRows>
            <Column field="principal" header="Principal DN">
              <template #body="{ data }">
                <span style="font-family: monospace; font-size: 0.8125rem">{{ data.principal }}</span>
              </template>
            </Column>
            <Column header="" style="width: 3rem">
              <template #body="{ data }">
                <Button icon="pi pi-times" size="small" severity="danger" text @click="onRemoveDenied(data.principal)" />
              </template>
            </Column>
            <template #empty>
              <div style="text-align: center; padding: 1rem; color: var(--p-text-muted-color); font-size: 0.8125rem">
                No principals in the denied list.
              </div>
            </template>
          </DataTable>
        </div>
      </div>
    </div>

    <ConfirmDialog />
  </div>
</template>
