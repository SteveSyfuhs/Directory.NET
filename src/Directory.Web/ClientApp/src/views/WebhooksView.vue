<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputSwitch from 'primevue/inputswitch'
import Tag from 'primevue/tag'
import MultiSelect from 'primevue/multiselect'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import PageHeader from '../components/PageHeader.vue'
import type { WebhookSubscription, WebhookDeliveryRecord, WebhookEventTypes } from '../types/webhooks'
import {
  fetchWebhooks,
  createWebhook,
  updateWebhook,
  deleteWebhook,
  testWebhook,
  fetchWebhookEventTypes,
  fetchWebhookDeliveries,
} from '../api/webhooks'

const toast = useToast()
const confirm = useConfirm()
const loading = ref(false)
const subscriptions = ref<WebhookSubscription[]>([])
const eventTypes = ref<WebhookEventTypes>({})

const showEditDialog = ref(false)
const editingSub = ref<Partial<WebhookSubscription>>({})
const isNew = ref(false)
const saving = ref(false)

const showDeliveriesDialog = ref(false)
const deliveriesSub = ref<WebhookSubscription | null>(null)
const deliveries = ref<WebhookDeliveryRecord[]>([])
const deliveriesLoading = ref(false)

const showSecretDialog = ref(false)
const visibleSecret = ref('')

// Flatten event types for the multi-select with grouping
const eventOptions = computed(() => {
  const opts: { label: string; value: string; group: string }[] = []
  for (const [category, events] of Object.entries(eventTypes.value)) {
    for (const evt of events) {
      opts.push({ label: evt, value: evt, group: category })
    }
  }
  return opts
})

onMounted(async () => {
  await Promise.all([loadSubscriptions(), loadEventTypes()])
})

async function loadSubscriptions() {
  loading.value = true
  try {
    subscriptions.value = await fetchWebhooks()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function loadEventTypes() {
  try {
    eventTypes.value = await fetchWebhookEventTypes()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function openCreate() {
  isNew.value = true
  editingSub.value = {
    name: '',
    url: '',
    secret: '',
    events: [],
    isEnabled: true,
  }
  showEditDialog.value = true
}

function openEdit(sub: WebhookSubscription) {
  isNew.value = false
  editingSub.value = { ...sub, events: [...sub.events] }
  showEditDialog.value = true
}

async function saveSub() {
  saving.value = true
  try {
    if (isNew.value) {
      await createWebhook(editingSub.value)
      toast.add({ severity: 'success', summary: 'Created', detail: 'Webhook subscription created.', life: 3000 })
    } else {
      await updateWebhook(editingSub.value.id!, editingSub.value)
      toast.add({ severity: 'success', summary: 'Updated', detail: 'Webhook subscription updated.', life: 3000 })
    }
    showEditDialog.value = false
    await loadSubscriptions()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function confirmDelete(sub: WebhookSubscription) {
  confirm.require({
    message: `Are you sure you want to delete the webhook "${sub.name}"?`,
    header: 'Delete Webhook',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete',
    acceptProps: { severity: 'danger' },
    accept: async () => {
      try {
        await deleteWebhook(sub.id)
        toast.add({ severity: 'success', summary: 'Deleted', detail: 'Webhook deleted.', life: 3000 })
        await loadSubscriptions()
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      }
    },
  })
}

async function sendTest(sub: WebhookSubscription) {
  try {
    const record = await testWebhook(sub.id)
    toast.add({
      severity: record.status === 'Success' ? 'success' : 'warn',
      summary: 'Test Delivery',
      detail: record.status === 'Success'
        ? `Test event delivered successfully (HTTP ${record.statusCode}).`
        : `Delivery failed: ${record.errorMessage}`,
      life: 5000,
    })
    await loadSubscriptions()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function toggleEnabled(sub: WebhookSubscription) {
  try {
    await updateWebhook(sub.id, { ...sub, isEnabled: !sub.isEnabled })
    await loadSubscriptions()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function openDeliveries(sub: WebhookSubscription) {
  deliveriesSub.value = sub
  deliveriesLoading.value = true
  showDeliveriesDialog.value = true
  try {
    deliveries.value = await fetchWebhookDeliveries(sub.id)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    deliveriesLoading.value = false
  }
}

function showSecret(sub: WebhookSubscription) {
  visibleSecret.value = sub.secret
  showSecretDialog.value = true
}

function copySecret() {
  navigator.clipboard.writeText(visibleSecret.value)
  toast.add({ severity: 'info', summary: 'Copied', detail: 'Secret copied to clipboard.', life: 2000 })
}

function generateSecret() {
  const bytes = new Uint8Array(32)
  crypto.getRandomValues(bytes)
  editingSub.value.secret = btoa(String.fromCharCode(...bytes))
}

function formatDate(d: string | null) {
  if (!d) return '-'
  return new Date(d).toLocaleString()
}

function deliveryStatusSeverity(status: string): "success" | "danger" | "info" | "secondary" | "warn" | undefined {
  return status === 'Success' ? 'success' : 'danger'
}
</script>

<template>
  <div>
    <PageHeader title="Webhook Notifications" subtitle="Configure webhook subscriptions to receive event notifications." />

    <div class="card">
      <div class="toolbar">
        <Button label="Refresh" icon="pi pi-refresh" severity="secondary" @click="loadSubscriptions" :loading="loading" />
        <span class="toolbar-spacer" />
        <Button label="New Webhook" icon="pi pi-plus" @click="openCreate" />
      </div>

      <DataTable :value="subscriptions" :loading="loading" stripedRows>
        <Column field="name" header="Name" sortable>
          <template #body="{ data }">
            <strong>{{ data.name }}</strong>
          </template>
        </Column>
        <Column field="url" header="URL">
          <template #body="{ data }">
            <span style="font-size: 0.8125rem; word-break: break-all">{{ data.url }}</span>
          </template>
        </Column>
        <Column field="events" header="Events">
          <template #body="{ data }">
            <div style="display: flex; flex-wrap: wrap; gap: 0.25rem">
              <Tag v-for="evt in data.events.slice(0, 3)" :key="evt" :value="evt" severity="info" style="font-size: 0.7rem" />
              <Tag v-if="data.events.length > 3" :value="`+${data.events.length - 3}`" severity="secondary" style="font-size: 0.7rem" />
            </div>
          </template>
        </Column>
        <Column field="isEnabled" header="Enabled" style="width: 6rem">
          <template #body="{ data }">
            <InputSwitch :modelValue="data.isEnabled" @update:modelValue="toggleEnabled(data)" />
          </template>
        </Column>
        <Column field="lastDeliveryAt" header="Last Delivery" sortable>
          <template #body="{ data }">{{ formatDate(data.lastDeliveryAt) }}</template>
        </Column>
        <Column field="lastDeliveryStatus" header="Status" style="width: 7rem">
          <template #body="{ data }">
            <Tag v-if="data.lastDeliveryStatus" :value="data.lastDeliveryStatus" :severity="deliveryStatusSeverity(data.lastDeliveryStatus)" />
            <span v-else style="color: var(--p-text-muted-color)">-</span>
          </template>
        </Column>
        <Column header="Actions" style="width: 16rem">
          <template #body="{ data }">
            <Button icon="pi pi-send" severity="success" text rounded v-tooltip="'Send Test'" @click="sendTest(data)" />
            <Button icon="pi pi-list" severity="info" text rounded v-tooltip="'Deliveries'" @click="openDeliveries(data)" />
            <Button icon="pi pi-key" severity="warn" text rounded v-tooltip="'View Secret'" @click="showSecret(data)" />
            <Button icon="pi pi-pencil" text rounded v-tooltip="'Edit'" @click="openEdit(data)" />
            <Button icon="pi pi-trash" severity="danger" text rounded v-tooltip="'Delete'" @click="confirmDelete(data)" />
          </template>
        </Column>
      </DataTable>
    </div>

    <!-- Create/Edit Dialog -->
    <Dialog v-model:visible="showEditDialog" :header="isNew ? 'New Webhook Subscription' : 'Edit Webhook Subscription'" modal style="width: 40rem">
      <div style="display: flex; flex-direction: column; gap: 1rem">
        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Name</label>
          <InputText v-model="editingSub.name" style="width: 100%" placeholder="e.g., Slack notifications" />
        </div>
        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">URL</label>
          <InputText v-model="editingSub.url" style="width: 100%" placeholder="https://example.com/webhook" />
        </div>
        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Secret (for HMAC-SHA256 signature)</label>
          <div style="display: flex; gap: 0.5rem">
            <InputText v-model="editingSub.secret" style="flex: 1" placeholder="Leave empty to auto-generate" />
            <Button label="Generate" icon="pi pi-refresh" severity="secondary" @click="generateSecret" />
          </div>
          <div style="font-size: 0.75rem; color: var(--p-text-muted-color); margin-top: 0.25rem">
            Used to compute the X-Webhook-Signature header for verifying delivery authenticity.
          </div>
        </div>
        <div>
          <label style="font-weight: 600; font-size: 0.875rem; display: block; margin-bottom: 0.25rem">Events</label>
          <MultiSelect
            v-model="editingSub.events"
            :options="eventOptions"
            optionLabel="label"
            optionValue="value"
            optionGroupLabel="group"
            :optionGroupChildren="undefined"
            placeholder="Select events"
            display="chip"
            style="width: 100%"
            :maxSelectedLabels="5"
          />
        </div>
        <div style="display: flex; align-items: center; gap: 0.5rem">
          <InputSwitch v-model="editingSub.isEnabled" />
          <label style="font-size: 0.875rem">Enabled</label>
        </div>
      </div>

      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showEditDialog = false" />
        <Button :label="isNew ? 'Create' : 'Save'" icon="pi pi-check" @click="saveSub" :loading="saving" />
      </template>
    </Dialog>

    <!-- Deliveries Dialog -->
    <Dialog v-model:visible="showDeliveriesDialog" :header="`Delivery Log: ${deliveriesSub?.name ?? ''}`" modal style="width: 55rem">
      <DataTable :value="deliveries" :loading="deliveriesLoading" stripedRows>
        <Column field="timestamp" header="Time" sortable>
          <template #body="{ data }">{{ formatDate(data.timestamp) }}</template>
        </Column>
        <Column field="eventType" header="Event" />
        <Column field="attempt" header="Attempt" style="width: 5rem" />
        <Column field="statusCode" header="HTTP" style="width: 5rem">
          <template #body="{ data }">{{ data.statusCode || '-' }}</template>
        </Column>
        <Column field="status" header="Status" style="width: 7rem">
          <template #body="{ data }">
            <Tag :value="data.status" :severity="deliveryStatusSeverity(data.status)" />
          </template>
        </Column>
        <Column field="errorMessage" header="Error">
          <template #body="{ data }">
            <span style="font-size: 0.8125rem; color: var(--p-text-muted-color)">{{ data.errorMessage || '-' }}</span>
          </template>
        </Column>
      </DataTable>
    </Dialog>

    <!-- Secret Dialog -->
    <Dialog v-model:visible="showSecretDialog" header="Webhook Secret" modal style="width: 30rem">
      <div style="display: flex; align-items: center; gap: 0.5rem">
        <InputText :modelValue="visibleSecret" readonly style="flex: 1; font-family: monospace; font-size: 0.8125rem" />
        <Button icon="pi pi-copy" severity="secondary" v-tooltip="'Copy'" @click="copySecret" />
      </div>
      <div style="font-size: 0.75rem; color: var(--p-text-muted-color); margin-top: 0.5rem">
        Use this secret to verify the X-Webhook-Signature header on incoming webhook deliveries.
      </div>
    </Dialog>
  </div>
</template>
