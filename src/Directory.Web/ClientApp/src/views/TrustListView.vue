<script setup lang="ts">
import { ref, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import Password from 'primevue/password'
import { useToast } from 'primevue/usetoast'
import { listTrusts, createTrust, deleteTrust, verifyTrust } from '../api/admin'

const toast = useToast()
const trusts = ref<any[]>([])
const loading = ref(true)
const showCreateDialog = ref(false)
const showVerifyDialog = ref(false)
const creating = ref(false)
const verifying = ref(false)
const verifyResult = ref<any>(null)

const newTrust = ref({
  trustPartner: '',
  flatName: '',
  trustDirection: 3,
  trustType: 2,
  trustAttributes: 0,
  securityIdentifier: '',
  sharedSecret: '',
})

const directionOptions = [
  { label: 'Disabled', value: 0 },
  { label: 'Inbound', value: 1 },
  { label: 'Outbound', value: 2 },
  { label: 'Bidirectional', value: 3 },
]

const typeOptions = [
  { label: 'Downlevel', value: 1 },
  { label: 'Uplevel', value: 2 },
  { label: 'MIT', value: 3 },
  { label: 'DCE', value: 4 },
  { label: 'External', value: 5 },
  { label: 'Forest', value: 6 },
  { label: 'ParentChild', value: 7 },
  { label: 'CrossLink', value: 8 },
  { label: 'Realm', value: 9 },
]

const attributeOptions = [
  { label: 'None', value: 0 },
  { label: 'Non-Transitive', value: 1 },
  { label: 'Uplevel Only', value: 2 },
  { label: 'Forest Transitive', value: 8 },
  { label: 'Cross-Organization', value: 16 },
  { label: 'Within Forest', value: 32 },
]

onMounted(() => loadTrusts())

async function loadTrusts() {
  loading.value = true
  try {
    trusts.value = await listTrusts()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

function openCreateDialog() {
  newTrust.value = {
    trustPartner: '',
    flatName: '',
    trustDirection: 3,
    trustType: 2,
    trustAttributes: 0,
    securityIdentifier: '',
    sharedSecret: '',
  }
  showCreateDialog.value = true
}

async function submitCreate() {
  if (!newTrust.value.trustPartner.trim()) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Trust partner domain is required', life: 3000 })
    return
  }
  creating.value = true
  try {
    await createTrust({
      trustPartner: newTrust.value.trustPartner.trim(),
      flatName: newTrust.value.flatName || undefined,
      trustDirection: newTrust.value.trustDirection,
      trustType: newTrust.value.trustType,
      trustAttributes: newTrust.value.trustAttributes,
      securityIdentifier: newTrust.value.securityIdentifier || undefined,
      sharedSecret: newTrust.value.sharedSecret || undefined,
    })
    showCreateDialog.value = false
    toast.add({ severity: 'success', summary: 'Created', detail: `Trust with ${newTrust.value.trustPartner} created`, life: 3000 })
    await loadTrusts()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    creating.value = false
  }
}

async function confirmDelete(trust: any) {
  if (!confirm(`Remove trust relationship with ${trust.trustPartner}?`)) return
  try {
    await deleteTrust(trust.objectGuid)
    toast.add({ severity: 'success', summary: 'Deleted', detail: `Trust with ${trust.trustPartner} removed`, life: 3000 })
    await loadTrusts()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function onVerify(trust: any) {
  verifying.value = true
  verifyResult.value = null
  showVerifyDialog.value = true
  try {
    verifyResult.value = await verifyTrust(trust.objectGuid)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
    showVerifyDialog.value = false
  } finally {
    verifying.value = false
  }
}

function directionSeverity(direction: number): string {
  switch (direction) {
    case 3: return 'success'
    case 2: return 'info'
    case 1: return 'warn'
    default: return 'danger'
  }
}

function directionIcon(direction: number): string {
  switch (direction) {
    case 3: return 'pi pi-arrows-h'
    case 2: return 'pi pi-arrow-right'
    case 1: return 'pi pi-arrow-left'
    default: return 'pi pi-ban'
  }
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Trust Relationships</h1>
      <p>Manage cross-realm Kerberos trust relationships with partner domains</p>
    </div>

    <div class="toolbar">
      <Button label="New Trust" icon="pi pi-plus" @click="openCreateDialog" />
      <div class="toolbar-spacer" />
      <Button label="Refresh" icon="pi pi-refresh" severity="secondary" @click="loadTrusts" />
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else class="card" style="padding: 0">
      <DataTable :value="trusts" stripedRows size="small" dataKey="objectGuid">
        <template #header>
          <div style="font-weight: 600; padding: 0.25rem">Configured Trusts</div>
        </template>

        <Column header="Partner Domain" sortable sortField="trustPartner">
          <template #body="{ data }">
            <div style="display: flex; align-items: center; gap: 0.5rem">
              <i class="pi pi-link" style="color: var(--app-accent-color)"></i>
              <div>
                <div style="font-weight: 600">{{ data.trustPartner }}</div>
                <div v-if="data.flatName" style="font-size: 0.8125rem; color: var(--p-text-muted-color)">
                  {{ data.flatName }}
                </div>
              </div>
            </div>
          </template>
        </Column>

        <Column header="Direction" sortable sortField="trustDirection" style="width: 10rem">
          <template #body="{ data }">
            <Tag :severity="directionSeverity(data.trustDirection)">
              <i :class="directionIcon(data.trustDirection)" style="margin-right: 0.375rem; font-size: 0.75rem"></i>
              {{ data.trustDirectionName }}
            </Tag>
          </template>
        </Column>

        <Column header="Type" sortable sortField="trustType" style="width: 8rem">
          <template #body="{ data }">
            <span>{{ data.trustTypeName }}</span>
          </template>
        </Column>

        <Column header="Key" style="width: 5rem; text-align: center">
          <template #body="{ data }">
            <i v-if="data.hasInterRealmKey" class="pi pi-lock" style="color: var(--app-success-text)" title="Inter-realm key configured"></i>
            <i v-else class="pi pi-lock-open" style="color: var(--app-danger-text)" title="No inter-realm key"></i>
          </template>
        </Column>

        <Column header="SID" style="width: 14rem">
          <template #body="{ data }">
            <span style="font-size: 0.8125rem; font-family: monospace; color: var(--p-text-muted-color)">
              {{ data.securityIdentifier || '-' }}
            </span>
          </template>
        </Column>

        <Column header="Created" sortable sortField="whenCreated" style="width: 10rem">
          <template #body="{ data }">
            <span style="font-size: 0.8125rem">{{ new Date(data.whenCreated).toLocaleDateString() }}</span>
          </template>
        </Column>

        <Column header="Actions" style="width: 10rem">
          <template #body="{ data }">
            <div style="display: flex; gap: 0.25rem">
              <Button icon="pi pi-check-circle" severity="success" text size="small"
                      title="Verify trust" @click="onVerify(data)" />
              <Button icon="pi pi-trash" severity="danger" text size="small"
                      title="Delete trust" @click="confirmDelete(data)" />
            </div>
          </template>
        </Column>

        <template #empty>
          <div style="text-align: center; padding: 3rem; color: var(--p-text-muted-color)">
            <i class="pi pi-link" style="font-size: 2rem; margin-bottom: 0.5rem; display: block; opacity: 0.4"></i>
            No trust relationships configured
            <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0.5rem 0 0 0">Trusts enable authentication and resource access between this domain and partner domains. Create a trust to allow users from another domain to authenticate here, or vice versa.</p>
          </div>
        </template>
      </DataTable>
    </div>

    <!-- Create Trust Dialog -->
    <Dialog v-model:visible="showCreateDialog" header="Create Trust Relationship" modal
            :style="{ width: '32rem' }" :closable="!creating">
      <div style="display: flex; flex-direction: column; gap: 1rem; padding-top: 0.5rem">
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Partner Domain (DNS)*</label>
          <InputText v-model="newTrust.trustPartner" placeholder="e.g. PARTNER.EXAMPLE.COM"
                     style="width: 100%" :disabled="creating" />
        </div>
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">NetBIOS Name</label>
          <InputText v-model="newTrust.flatName" placeholder="e.g. PARTNER"
                     style="width: 100%" :disabled="creating" />
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem">
          <div>
            <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Direction</label>
            <Select v-model="newTrust.trustDirection" :options="directionOptions"
                    optionLabel="label" optionValue="value" style="width: 100%" :disabled="creating" />
          </div>
          <div>
            <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Type</label>
            <Select v-model="newTrust.trustType" :options="typeOptions"
                    optionLabel="label" optionValue="value" style="width: 100%" :disabled="creating" />
          </div>
        </div>
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Trust Attributes</label>
          <Select v-model="newTrust.trustAttributes" :options="attributeOptions"
                  optionLabel="label" optionValue="value" style="width: 100%" :disabled="creating" />
        </div>
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Partner Domain SID</label>
          <InputText v-model="newTrust.securityIdentifier" placeholder="S-1-5-21-..."
                     style="width: 100%" :disabled="creating" />
        </div>
        <div>
          <label style="display: block; font-weight: 600; margin-bottom: 0.25rem; font-size: 0.875rem">Shared Secret (for inter-realm key)</label>
          <Password v-model="newTrust.sharedSecret" :feedback="false" toggleMask
                    placeholder="Shared secret for key derivation"
                    style="width: 100%" :disabled="creating" inputStyle="width: 100%" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showCreateDialog = false" :disabled="creating" />
        <Button label="Create Trust" icon="pi pi-plus" @click="submitCreate" :loading="creating" />
      </template>
    </Dialog>

    <!-- Verify Trust Dialog -->
    <Dialog v-model:visible="showVerifyDialog" header="Trust Verification" modal
            :style="{ width: '28rem' }">
      <div v-if="verifying" style="text-align: center; padding: 2rem">
        <ProgressSpinner />
        <p style="margin-top: 1rem; color: var(--p-text-muted-color)">Verifying trust...</p>
      </div>
      <div v-else-if="verifyResult" style="padding: 0.5rem 0">
        <div style="display: flex; align-items: center; gap: 0.75rem; margin-bottom: 1.5rem">
          <div :style="{
            width: '3rem', height: '3rem', borderRadius: '50%',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            background: verifyResult.isOperational ? 'var(--app-success-bg)' : 'var(--app-danger-bg)',
            color: verifyResult.isOperational ? 'var(--app-success-text)' : 'var(--app-danger-text)',
            fontSize: '1.25rem'
          }">
            <i :class="verifyResult.isOperational ? 'pi pi-check' : 'pi pi-times'"></i>
          </div>
          <div>
            <div style="font-weight: 600; font-size: 1.0625rem">
              {{ verifyResult.isOperational ? 'Trust is Operational' : 'Trust has Issues' }}
            </div>
            <div style="font-size: 0.875rem; color: var(--p-text-muted-color)">
              {{ verifyResult.trustPartner }}
            </div>
          </div>
        </div>

        <div v-if="verifyResult.issues && verifyResult.issues.length > 0"
             style="background: var(--app-danger-bg); border: 1px solid var(--app-danger-border); border-radius: 0.5rem; padding: 0.75rem 1rem; margin-bottom: 1rem">
          <div v-for="issue in verifyResult.issues" :key="issue"
               style="display: flex; align-items: flex-start; gap: 0.5rem; padding: 0.25rem 0; color: var(--app-danger-text-strong); font-size: 0.875rem">
            <i class="pi pi-exclamation-triangle" style="margin-top: 0.125rem; flex-shrink: 0"></i>
            <span>{{ issue }}</span>
          </div>
        </div>

        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; font-size: 0.875rem">
          <div>
            <div style="color: var(--p-text-muted-color)">Has Inter-Realm Key</div>
            <div style="font-weight: 600">{{ verifyResult.hasInterRealmKey ? 'Yes' : 'No' }}</div>
          </div>
          <div>
            <div style="color: var(--p-text-muted-color)">Verified At</div>
            <div style="font-weight: 600">{{ new Date(verifyResult.verifiedAt).toLocaleTimeString() }}</div>
          </div>
        </div>
      </div>
      <template #footer>
        <Button label="Close" severity="secondary" @click="showVerifyDialog = false" />
      </template>
    </Dialog>
  </div>
</template>
