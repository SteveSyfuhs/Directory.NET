<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import Checkbox from 'primevue/checkbox'
import ProgressSpinner from 'primevue/progressspinner'
import ProgressBar from 'primevue/progressbar'
import { useToast } from 'primevue/usetoast'
import {
  fetchAccessReviews,
  createAccessReview,
  startAccessReview,
  fetchAccessReviewDecisions,
  submitAccessReviewDecision,
  completeAccessReview,
} from '../api/accessReviews'
import type { AccessReview, AccessReviewDecision } from '../api/accessReviews'

const toast = useToast()

const loading = ref(true)
const reviews = ref<AccessReview[]>([])
const showCreateDialog = ref(false)
const showReviewDialog = ref(false)
const activeReview = ref<AccessReview | null>(null)
const decisions = ref<AccessReviewDecision[]>([])
const loadingDecisions = ref(false)
const bulkJustification = ref('')

const newReview = ref({
  name: '',
  description: '',
  scopeType: 'Group',
  scopeTargetDn: '',
  reviewerDn: '',
  frequency: 'Quarterly',
  durationDays: 14,
  autoRemoveOnDeny: false,
})

const frequencyOptions = [
  { label: 'One Time', value: 'OneTime' },
  { label: 'Monthly', value: 'Monthly' },
  { label: 'Quarterly', value: 'Quarterly' },
  { label: 'Semi-Annual', value: 'SemiAnnual' },
  { label: 'Annual', value: 'Annual' },
]

const scopeTypeOptions = [
  { label: 'Group', value: 'Group' },
  { label: 'OU', value: 'OU' },
]

onMounted(async () => {
  await loadReviews()
})

async function loadReviews() {
  loading.value = true
  try {
    reviews.value = await fetchAccessReviews()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function handleCreate() {
  try {
    await createAccessReview({
      name: newReview.value.name,
      description: newReview.value.description,
      scope: {
        type: newReview.value.scopeType,
        targetDn: newReview.value.scopeTargetDn,
      },
      reviewerDn: newReview.value.reviewerDn,
      frequency: newReview.value.frequency,
      durationDays: newReview.value.durationDays,
      autoRemoveOnDeny: newReview.value.autoRemoveOnDeny,
    })
    showCreateDialog.value = false
    newReview.value = { name: '', description: '', scopeType: 'Group', scopeTargetDn: '', reviewerDn: '', frequency: 'Quarterly', durationDays: 14, autoRemoveOnDeny: false }
    toast.add({ severity: 'success', summary: 'Created', detail: 'Access review created', life: 3000 })
    await loadReviews()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function handleStart(review: AccessReview) {
  try {
    await startAccessReview(review.id)
    toast.add({ severity: 'success', summary: 'Started', detail: `Review "${review.name}" started`, life: 3000 })
    await loadReviews()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function openReview(review: AccessReview) {
  activeReview.value = review
  showReviewDialog.value = true
  loadingDecisions.value = true
  try {
    decisions.value = await fetchAccessReviewDecisions(review.id)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loadingDecisions.value = false
  }
}

async function handleDecision(decision: AccessReviewDecision, action: string) {
  if (!activeReview.value) return
  try {
    await submitAccessReviewDecision(activeReview.value.id, {
      userDn: decision.userDn,
      decision: action,
      justification: decision.justification || '',
      reviewerDn: activeReview.value.reviewerDn,
    })
    decision.decision = action
    decision.decidedAt = new Date().toISOString()
    toast.add({ severity: 'success', summary: 'Decision Recorded', detail: `${action} for ${decision.userDisplayName}`, life: 2000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function handleBulkApprove() {
  if (!activeReview.value) return
  for (const d of decisions.value.filter(d => d.decision === 'NotReviewed')) {
    await handleDecision(d, 'Approve')
  }
}

async function handleComplete() {
  if (!activeReview.value) return
  try {
    await completeAccessReview(activeReview.value.id)
    showReviewDialog.value = false
    toast.add({ severity: 'success', summary: 'Completed', detail: 'Access review completed', life: 3000 })
    await loadReviews()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function statusSeverity(status: string): string {
  switch (status) {
    case 'Completed': return 'success'
    case 'InProgress': return 'warn'
    case 'Expired': return 'danger'
    default: return 'info'
  }
}

function decisionSeverity(decision: string): string {
  switch (decision) {
    case 'Approve': return 'success'
    case 'Deny': return 'danger'
    default: return 'secondary'
  }
}

const reviewProgress = computed(() => {
  if (decisions.value.length === 0) return 0
  const decided = decisions.value.filter(d => d.decision !== 'NotReviewed').length
  return Math.round((decided / decisions.value.length) * 100)
})
</script>

<template>
  <div class="access-reviews-view">
    <div class="page-header">
      <h1><i class="pi pi-check-square"></i> Access Reviews</h1>
      <Button label="Create Review" icon="pi pi-plus" @click="showCreateDialog = true" />
    </div>

    <ProgressSpinner v-if="loading" strokeWidth="3" />

    <template v-else>
      <DataTable :value="reviews" stripedRows responsiveLayout="scroll" :rowHover="true">
        <Column field="name" header="Name" sortable />
        <Column header="Scope">
          <template #body="{ data }">
            <span>{{ data.scope.type }}: {{ data.scope.targetDn }}</span>
          </template>
        </Column>
        <Column field="reviewerDn" header="Reviewer" sortable>
          <template #body="{ data }">
            <span class="dn-short">{{ data.reviewerDn }}</span>
          </template>
        </Column>
        <Column field="frequency" header="Frequency" sortable />
        <Column field="status" header="Status" sortable>
          <template #body="{ data }">
            <Tag :value="data.status" :severity="statusSeverity(data.status)" />
          </template>
        </Column>
        <Column field="dueDate" header="Due Date" sortable>
          <template #body="{ data }">
            {{ data.dueDate ? new Date(data.dueDate).toLocaleDateString() : '--' }}
          </template>
        </Column>
        <Column header="Actions" :style="{ width: '220px' }">
          <template #body="{ data }">
            <div class="action-buttons">
              <Button
                v-if="data.status === 'NotStarted'"
                icon="pi pi-play"
                size="small"
                severity="success"
                v-tooltip="'Start Review'"
                @click="handleStart(data)"
              />
              <Button
                v-if="data.status === 'InProgress'"
                icon="pi pi-pencil"
                size="small"
                severity="warn"
                v-tooltip="'Review Decisions'"
                @click="openReview(data)"
              />
              <Button
                v-if="data.status === 'Completed' || data.status === 'InProgress'"
                icon="pi pi-eye"
                size="small"
                severity="info"
                v-tooltip="'View Details'"
                @click="openReview(data)"
              />
            </div>
          </template>
        </Column>
      </DataTable>
    </template>

    <!-- Create Dialog -->
    <Dialog
      v-model:visible="showCreateDialog"
      header="Create Access Review"
      :modal="true"
      :style="{ width: '550px' }"
    >
      <div class="form-grid">
        <div class="form-field">
          <label>Name</label>
          <InputText v-model="newReview.name" class="w-full" />
        </div>
        <div class="form-field">
          <label>Description</label>
          <Textarea v-model="newReview.description" rows="2" class="w-full" />
        </div>
        <div class="form-row">
          <div class="form-field">
            <label>Scope Type</label>
            <Select v-model="newReview.scopeType" :options="scopeTypeOptions" optionLabel="label" optionValue="value" class="w-full" />
          </div>
          <div class="form-field" style="flex: 2">
            <label>Target DN</label>
            <InputText v-model="newReview.scopeTargetDn" class="w-full" placeholder="CN=Domain Admins,CN=Users,DC=..." />
          </div>
        </div>
        <div class="form-field">
          <label>Reviewer DN</label>
          <InputText v-model="newReview.reviewerDn" class="w-full" placeholder="CN=Admin,CN=Users,DC=..." />
        </div>
        <div class="form-row">
          <div class="form-field">
            <label>Frequency</label>
            <Select v-model="newReview.frequency" :options="frequencyOptions" optionLabel="label" optionValue="value" class="w-full" />
          </div>
          <div class="form-field">
            <label>Duration (Days)</label>
            <InputNumber v-model="newReview.durationDays" :min="1" :max="365" class="w-full" />
          </div>
        </div>
        <div class="form-field checkbox-field">
          <Checkbox v-model="newReview.autoRemoveOnDeny" :binary="true" inputId="autoRemove" />
          <label for="autoRemove">Auto-remove denied users from group on completion</label>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showCreateDialog = false" />
        <Button label="Create" icon="pi pi-check" :disabled="!newReview.name || !newReview.scopeTargetDn" @click="handleCreate" />
      </template>
    </Dialog>

    <!-- Review Execution Dialog -->
    <Dialog
      v-model:visible="showReviewDialog"
      :header="activeReview?.name ?? 'Review'"
      :modal="true"
      :style="{ width: '90vw', maxWidth: '1000px' }"
      :maximizable="true"
    >
      <template v-if="activeReview">
        <div class="review-info">
          <Tag :value="activeReview.status" :severity="statusSeverity(activeReview.status)" />
          <span>Scope: {{ activeReview.scope.type }} - {{ activeReview.scope.targetDn }}</span>
          <span v-if="activeReview.dueDate">Due: {{ new Date(activeReview.dueDate).toLocaleDateString() }}</span>
        </div>

        <div class="review-progress">
          <span>Progress: {{ reviewProgress }}%</span>
          <ProgressBar :value="reviewProgress" :showValue="false" style="height: 8px" />
        </div>

        <ProgressSpinner v-if="loadingDecisions" strokeWidth="3" />

        <template v-else>
          <div v-if="activeReview.status === 'InProgress'" class="bulk-actions">
            <Button label="Bulk Approve All Pending" icon="pi pi-check-circle" size="small" severity="success" @click="handleBulkApprove" />
          </div>

          <DataTable :value="decisions" stripedRows :paginator="decisions.length > 20" :rows="20" responsiveLayout="scroll">
            <Column field="userDisplayName" header="User" sortable />
            <Column field="userDn" header="DN" sortable>
              <template #body="{ data }">
                <span class="dn-short">{{ data.userDn }}</span>
              </template>
            </Column>
            <Column field="decision" header="Decision" sortable>
              <template #body="{ data }">
                <Tag :value="data.decision" :severity="decisionSeverity(data.decision)" />
              </template>
            </Column>
            <Column field="justification" header="Justification">
              <template #body="{ data }">
                <InputText
                  v-if="activeReview?.status === 'InProgress' && data.decision === 'NotReviewed'"
                  v-model="data.justification"
                  placeholder="Justification..."
                  size="small"
                  class="w-full"
                />
                <span v-else>{{ data.justification || '--' }}</span>
              </template>
            </Column>
            <Column v-if="activeReview?.status === 'InProgress'" header="Actions" :style="{ width: '160px' }">
              <template #body="{ data }">
                <div v-if="data.decision === 'NotReviewed'" class="action-buttons">
                  <Button icon="pi pi-check" size="small" severity="success" v-tooltip="'Approve'" @click="handleDecision(data, 'Approve')" />
                  <Button icon="pi pi-times" size="small" severity="danger" v-tooltip="'Deny'" @click="handleDecision(data, 'Deny')" />
                </div>
                <span v-else class="text-muted">{{ data.decidedAt ? new Date(data.decidedAt).toLocaleString() : '' }}</span>
              </template>
            </Column>
          </DataTable>

          <div v-if="activeReview.status === 'InProgress'" class="complete-section">
            <Button label="Complete Review" icon="pi pi-flag" severity="success" @click="handleComplete" :disabled="reviewProgress < 100" />
            <span v-if="reviewProgress < 100" class="text-muted">All decisions must be made before completing</span>
          </div>
        </template>
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.access-reviews-view {
  padding: 1.5rem;
}
.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1.5rem;
}
.page-header h1 {
  margin: 0;
  font-size: 1.5rem;
}
.action-buttons {
  display: flex;
  gap: 0.5rem;
}
.form-grid {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.form-row {
  display: flex;
  gap: 1rem;
}
.form-field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  flex: 1;
}
.form-field label {
  font-weight: 600;
  font-size: 0.85rem;
}
.checkbox-field {
  flex-direction: row;
  align-items: center;
  gap: 0.5rem;
}
.review-info {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 1rem;
  flex-wrap: wrap;
}
.review-progress {
  margin-bottom: 1rem;
}
.review-progress span {
  display: block;
  margin-bottom: 0.25rem;
  font-size: 0.85rem;
  font-weight: 600;
}
.bulk-actions {
  margin-bottom: 1rem;
}
.complete-section {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-top: 1rem;
  padding-top: 1rem;
  border-top: 1px solid var(--p-surface-200);
}
.dn-short {
  font-size: 0.85rem;
  word-break: break-all;
}
.text-muted {
  color: var(--p-text-muted-color);
  font-size: 0.85rem;
  font-style: italic;
}
</style>
