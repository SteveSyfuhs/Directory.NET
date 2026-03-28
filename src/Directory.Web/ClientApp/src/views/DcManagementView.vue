<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import ProgressBar from 'primevue/progressbar'
import ProgressSpinner from 'primevue/progressspinner'
import Checkbox from 'primevue/checkbox'
import Dropdown from 'primevue/dropdown'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import Card from 'primevue/card'
import ConfirmDialog from 'primevue/confirmdialog'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import {
  startDemotion,
  getDemotionStatus,
  validateDemotion,
  getFsmoRoles,
  transferFsmoRole,
  seizeFsmoRole,
  getFunctionalLevels,
  raiseDomainFunctionalLevel,
  raiseForestFunctionalLevel,
} from '../api/dcManagement'
import type {
  DemotionRequest,
  DemotionValidationResult,
  DemotionStatus,
  FsmoRoleInfo,
  DcInfo,
  FunctionalLevelStatus,
  FunctionalLevelFeature,
} from '../types/dcManagement'
import { functionalLevelLabels, functionalLevelOptions } from '../types/dcManagement'

const toast = useToast()
const confirm = useConfirm()

// ── Shared state ────────────────────────────────────────────────────────────

const loading = ref(true)

// ── Tab 1: DC Status & Demotion ─────────────────────────────────────────────

const demotionForm = ref<DemotionRequest>({
  isLastDcInDomain: false,
  removeDnsRecords: true,
  forceRemoval: false,
})
const validating = ref(false)
const validationResult = ref<DemotionValidationResult | null>(null)
const demoting = ref(false)
const demotionStatus = ref<DemotionStatus | null>(null)
let demotionPollTimer: ReturnType<typeof setInterval> | null = null

// ── Tab 2: FSMO Roles ──────────────────────────────────────────────────────

const fsmoRoles = ref<FsmoRoleInfo[]>([])
const domainControllers = ref<DcInfo[]>([])
const fsmoLoading = ref(true)
const transferDialogVisible = ref(false)
const seizeDialogVisible = ref(false)
const selectedRole = ref<FsmoRoleInfo | null>(null)
const selectedTargetDc = ref<string>('')
const transferring = ref(false)

const targetDcOptions = computed(() =>
  domainControllers.value
    .filter(dc => !dc.isCurrentDc || selectedRole.value?.holderDn !== dc.ntdsSettingsDn)
    .map(dc => ({
      label: `${dc.serverName} (${dc.siteName})${dc.isCurrentDc ? ' - This DC' : ''}`,
      value: dc.ntdsSettingsDn,
    }))
)

// ── Tab 3: Functional Levels ────────────────────────────────────────────────

const flStatus = ref<FunctionalLevelStatus | null>(null)
const flLoading = ref(true)
const raiseDomainDialogVisible = ref(false)
const raiseForestDialogVisible = ref(false)
const selectedDomainLevel = ref<number | null>(null)
const selectedForestLevel = ref<number | null>(null)
const raising = ref(false)

const domainLevelLabel = computed(() =>
  flStatus.value ? functionalLevelLabels[flStatus.value.currentDomainLevel] ?? `Level ${flStatus.value.currentDomainLevel}` : ''
)

const forestLevelLabel = computed(() =>
  flStatus.value ? functionalLevelLabels[flStatus.value.currentForestLevel] ?? `Level ${flStatus.value.currentForestLevel}` : ''
)

const availableDomainLevels = computed(() =>
  flStatus.value
    ? functionalLevelOptions.filter(
        o => o.value > flStatus.value!.currentDomainLevel && o.value <= flStatus.value!.maxDomainLevel
      )
    : []
)

const availableForestLevels = computed(() =>
  flStatus.value
    ? functionalLevelOptions.filter(
        o => o.value > flStatus.value!.currentForestLevel && o.value <= flStatus.value!.maxForestLevel
      )
    : []
)

const enabledFeatures = computed(() =>
  flStatus.value?.availableFeatures.filter(f => f.isEnabled) ?? []
)

const pendingFeatures = computed(() =>
  flStatus.value?.availableFeatures.filter(f => !f.isEnabled) ?? []
)

// ── Lifecycle ───────────────────────────────────────────────────────────────

onMounted(async () => {
  loading.value = true
  await Promise.all([loadFsmoRoles(), loadFunctionalLevels()])
  loading.value = false
})

// ── Demotion methods ────────────────────────────────────────────────────────

async function onValidateDemotion() {
  validating.value = true
  validationResult.value = null
  try {
    validationResult.value = await validateDemotion(demotionForm.value)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Validation Error', detail: e.message, life: 5000 })
  } finally {
    validating.value = false
  }
}

function onStartDemotion() {
  confirm.require({
    message: demotionForm.value.isLastDcInDomain
      ? 'This will PERMANENTLY remove the ENTIRE DOMAIN. All directory data will be deleted. This action cannot be undone. Are you absolutely sure?'
      : 'This will demote this server from a domain controller. FSMO roles will be transferred and DNS records will be cleaned up. Continue?',
    header: demotionForm.value.isLastDcInDomain ? 'Remove Last Domain Controller' : 'Demote Domain Controller',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      demoting.value = true
      try {
        await startDemotion(demotionForm.value)
        toast.add({ severity: 'info', summary: 'Demotion Started', detail: 'DC demotion is in progress...', life: 3000 })
        startDemotionPolling()
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Demotion Error', detail: e.message, life: 5000 })
        demoting.value = false
      }
    },
  })
}

function startDemotionPolling() {
  demotionPollTimer = setInterval(async () => {
    try {
      demotionStatus.value = await getDemotionStatus()
      if (demotionStatus.value.isComplete || demotionStatus.value.error) {
        stopDemotionPolling()
        demoting.value = false
        if (demotionStatus.value.error) {
          toast.add({ severity: 'error', summary: 'Demotion Failed', detail: demotionStatus.value.error, life: 8000 })
        } else {
          toast.add({ severity: 'success', summary: 'Demotion Complete', detail: 'DC has been successfully demoted.', life: 5000 })
        }
      }
    } catch {
      // Ignore polling errors
    }
  }, 1500)
}

function stopDemotionPolling() {
  if (demotionPollTimer) {
    clearInterval(demotionPollTimer)
    demotionPollTimer = null
  }
}

// ── FSMO methods ────────────────────────────────────────────────────────────

async function loadFsmoRoles() {
  fsmoLoading.value = true
  try {
    const data = await getFsmoRoles()
    fsmoRoles.value = data.roles
    domainControllers.value = data.domainControllers
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    fsmoLoading.value = false
  }
}

function openTransferDialog(role: FsmoRoleInfo) {
  selectedRole.value = role
  selectedTargetDc.value = ''
  transferDialogVisible.value = true
}

function openSeizeDialog(role: FsmoRoleInfo) {
  selectedRole.value = role
  selectedTargetDc.value = ''
  seizeDialogVisible.value = true
}

async function onTransferRole() {
  if (!selectedRole.value || !selectedTargetDc.value) return
  transferring.value = true
  try {
    const result = await transferFsmoRole({
      role: selectedRole.value.role,
      targetNtdsSettingsDn: selectedTargetDc.value,
    })
    if (result.success) {
      toast.add({ severity: 'success', summary: 'Role Transferred', detail: `${selectedRole.value.role} transferred successfully.`, life: 3000 })
      transferDialogVisible.value = false
      await loadFsmoRoles()
    } else {
      toast.add({ severity: 'error', summary: 'Transfer Failed', detail: result.errorMessage, life: 5000 })
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    transferring.value = false
  }
}

async function onSeizeRole() {
  if (!selectedRole.value || !selectedTargetDc.value) return
  confirm.require({
    message: `Seizing the ${selectedRole.value.role} role is a FORCED operation that should only be used when the current holder is permanently offline. The previous holder MUST NOT be brought back online after seizure. Continue?`,
    header: 'Seize FSMO Role',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      transferring.value = true
      try {
        const result = await seizeFsmoRole({
          role: selectedRole.value!.role,
          targetNtdsSettingsDn: selectedTargetDc.value,
        })
        if (result.success) {
          toast.add({ severity: 'warn', summary: 'Role Seized', detail: `${selectedRole.value!.role} seized successfully.`, life: 3000 })
          seizeDialogVisible.value = false
          await loadFsmoRoles()
        } else {
          toast.add({ severity: 'error', summary: 'Seize Failed', detail: result.errorMessage, life: 5000 })
        }
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      } finally {
        transferring.value = false
      }
    },
  })
}

// ── Functional Level methods ────────────────────────────────────────────────

async function loadFunctionalLevels() {
  flLoading.value = true
  try {
    flStatus.value = await getFunctionalLevels()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    flLoading.value = false
  }
}

function openRaiseDomainDialog() {
  selectedDomainLevel.value = null
  raiseDomainDialogVisible.value = true
}

function openRaiseForestDialog() {
  selectedForestLevel.value = null
  raiseForestDialogVisible.value = true
}

async function onRaiseDomainLevel() {
  if (selectedDomainLevel.value == null) return
  const targetLabel = functionalLevelLabels[selectedDomainLevel.value] ?? `Level ${selectedDomainLevel.value}`
  confirm.require({
    message: `Raising the domain functional level to ${targetLabel} is IRREVERSIBLE. You cannot lower it after this operation. Continue?`,
    header: 'Raise Domain Functional Level',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-warning',
    accept: async () => {
      raising.value = true
      try {
        const result = await raiseDomainFunctionalLevel(selectedDomainLevel.value!)
        if (result.success) {
          toast.add({ severity: 'success', summary: 'Level Raised', detail: `Domain functional level raised to ${targetLabel}.`, life: 3000 })
          raiseDomainDialogVisible.value = false
          await loadFunctionalLevels()
        } else {
          toast.add({ severity: 'error', summary: 'Failed', detail: result.errorMessage, life: 5000 })
        }
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      } finally {
        raising.value = false
      }
    },
  })
}

async function onRaiseForestLevel() {
  if (selectedForestLevel.value == null) return
  const targetLabel = functionalLevelLabels[selectedForestLevel.value] ?? `Level ${selectedForestLevel.value}`
  confirm.require({
    message: `Raising the forest functional level to ${targetLabel} is IRREVERSIBLE. All domains in the forest must already be at this level or higher. Continue?`,
    header: 'Raise Forest Functional Level',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-warning',
    accept: async () => {
      raising.value = true
      try {
        const result = await raiseForestFunctionalLevel(selectedForestLevel.value!)
        if (result.success) {
          toast.add({ severity: 'success', summary: 'Level Raised', detail: `Forest functional level raised to ${targetLabel}.`, life: 3000 })
          raiseForestDialogVisible.value = false
          await loadFunctionalLevels()
        } else {
          toast.add({ severity: 'error', summary: 'Failed', detail: result.errorMessage, life: 5000 })
        }
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      } finally {
        raising.value = false
      }
    },
  })
}

function featureLevelLabel(level: number) {
  return functionalLevelLabels[level] ?? `Level ${level}`
}
</script>

<template>
  <div class="dc-management-view">
    <h2><i class="pi pi-server"></i> DC Management</h2>

    <ConfirmDialog />

    <div v-if="loading" class="loading-container">
      <ProgressSpinner strokeWidth="3" />
      <p>Loading DC management data...</p>
    </div>

    <TabView v-else>
      <!-- ── Tab 1: DC Status & Demotion ──────────────────────────────────── -->
      <TabPanel header="DC Status & Demotion" value="dc-status-&-demotion">
        <div class="tab-content">
          <!-- Demotion in progress -->
          <div v-if="demoting || demotionStatus?.isInProgress" class="demotion-progress">
            <Card>
              <template #title>Demotion In Progress</template>
              <template #content>
                <ProgressBar :value="demotionStatus?.progress ?? 0" />
                <p class="phase-text">{{ demotionStatus?.phase ?? 'Starting...' }}</p>
                <Message v-if="demotionStatus?.error" severity="error">{{ demotionStatus.error }}</Message>
              </template>
            </Card>
          </div>

          <!-- Demotion form -->
          <div v-else>
            <Card class="mb-3">
              <template #title>Demotion Configuration</template>
              <template #content>
                <div class="form-grid">
                  <div class="form-field">
                    <div class="field-checkbox">
                      <Checkbox v-model="demotionForm.isLastDcInDomain" :binary="true" inputId="isLastDc" />
                      <label for="isLastDc" class="ml-2">Last DC in domain</label>
                    </div>
                    <Message v-if="demotionForm.isLastDcInDomain" severity="warn" class="mt-2">
                      This will permanently remove the entire domain and all its data. This action cannot be undone.
                    </Message>
                  </div>

                  <div class="form-field">
                    <div class="field-checkbox">
                      <Checkbox v-model="demotionForm.removeDnsRecords" :binary="true" inputId="removeDns" />
                      <label for="removeDns" class="ml-2">Remove DNS records</label>
                    </div>
                  </div>

                  <div class="form-field">
                    <div class="field-checkbox">
                      <Checkbox v-model="demotionForm.forceRemoval" :binary="true" inputId="forceRemoval" />
                      <label for="forceRemoval" class="ml-2">Force removal (skip replication drain)</label>
                    </div>
                  </div>
                </div>

                <!-- Validation results -->
                <div v-if="validationResult" class="mt-3">
                  <Message v-for="err in validationResult.errors" :key="err" severity="error">{{ err }}</Message>
                  <Message v-for="warn in validationResult.warnings" :key="warn" severity="warn">{{ warn }}</Message>
                  <div v-if="validationResult.heldFsmoRoles.length > 0" class="mt-2">
                    <strong>FSMO roles held by this DC:</strong>
                    <Tag v-for="role in validationResult.heldFsmoRoles" :key="role" :value="role" severity="info" class="ml-1" />
                  </div>
                  <Message v-if="validationResult.isValid" severity="success" class="mt-2">
                    Pre-flight validation passed. Ready to demote.
                  </Message>
                </div>

                <div class="button-row mt-3">
                  <Button
                    label="Validate"
                    icon="pi pi-check-circle"
                    @click="onValidateDemotion"
                    :loading="validating"
                    severity="info"
                  />
                  <Button
                    label="Demote DC"
                    icon="pi pi-power-off"
                    @click="onStartDemotion"
                    :disabled="!validationResult?.isValid"
                    severity="danger"
                  />
                </div>
              </template>
            </Card>
          </div>
        </div>
      </TabPanel>

      <!-- ── Tab 2: FSMO Roles ────────────────────────────────────────────── -->
      <TabPanel header="FSMO Roles" value="fsmo-roles">
        <div class="tab-content">
          <div v-if="fsmoLoading" class="loading-container">
            <ProgressSpinner strokeWidth="3" />
          </div>
          <div v-else>
            <DataTable :value="fsmoRoles" stripedRows>
              <Column header="Role">
                <template #body="{ data }">
                  <div>
                    <strong>{{ data.role }}</strong>
                    <div class="text-sm text-color-secondary">{{ data.description }}</div>
                  </div>
                </template>
              </Column>
              <Column header="Scope">
                <template #body="{ data }">
                  <Tag :value="data.scope" :severity="data.scope === 'Forest' ? 'danger' : 'info'" />
                </template>
              </Column>
              <Column header="Current Holder">
                <template #body="{ data }">
                  <span>{{ data.holderServerName }}</span>
                </template>
              </Column>
              <Column header="Actions" style="width: 200px">
                <template #body="{ data }">
                  <div class="button-row">
                    <Button label="Transfer" icon="pi pi-arrow-right" size="small" @click="openTransferDialog(data)" severity="info" />
                    <Button label="Seize" icon="pi pi-bolt" size="small" @click="openSeizeDialog(data)" severity="danger" outlined />
                  </div>
                </template>
              </Column>
            </DataTable>

            <Button label="Refresh" icon="pi pi-refresh" class="mt-3" @click="loadFsmoRoles" :loading="fsmoLoading" text />
          </div>

          <!-- Transfer Dialog -->
          <Dialog v-model:visible="transferDialogVisible" header="Transfer FSMO Role" :modal="true" :style="{ width: '500px' }">
            <div v-if="selectedRole">
              <p>Transfer <strong>{{ selectedRole.role }}</strong> to another domain controller.</p>
              <div class="form-field mt-3">
                <label>Target DC</label>
                <Dropdown
                  v-model="selectedTargetDc"
                  :options="targetDcOptions"
                  optionLabel="label"
                  optionValue="value"
                  placeholder="Select target DC"
                  class="w-full"
                />
              </div>
            </div>
            <template #footer>
              <Button label="Cancel" @click="transferDialogVisible = false" text />
              <Button label="Transfer" icon="pi pi-arrow-right" @click="onTransferRole" :loading="transferring" :disabled="!selectedTargetDc" />
            </template>
          </Dialog>

          <!-- Seize Dialog -->
          <Dialog v-model:visible="seizeDialogVisible" header="Seize FSMO Role" :modal="true" :style="{ width: '500px' }">
            <div v-if="selectedRole">
              <Message severity="warn">
                Role seizure is a forced operation. Only use this when the current holder is permanently offline
                and will NEVER be brought back online.
              </Message>
              <p class="mt-2">Seize <strong>{{ selectedRole.role }}</strong> from <strong>{{ selectedRole.holderServerName }}</strong>.</p>
              <div class="form-field mt-3">
                <label>Target DC</label>
                <Dropdown
                  v-model="selectedTargetDc"
                  :options="targetDcOptions"
                  optionLabel="label"
                  optionValue="value"
                  placeholder="Select target DC"
                  class="w-full"
                />
              </div>
            </div>
            <template #footer>
              <Button label="Cancel" @click="seizeDialogVisible = false" text />
              <Button label="Seize Role" icon="pi pi-bolt" @click="onSeizeRole" :loading="transferring" :disabled="!selectedTargetDc" severity="danger" />
            </template>
          </Dialog>
        </div>
      </TabPanel>

      <!-- ── Tab 3: Functional Levels ─────────────────────────────────────── -->
      <TabPanel header="Functional Levels" value="functional-levels">
        <div class="tab-content">
          <div v-if="flLoading" class="loading-container">
            <ProgressSpinner strokeWidth="3" />
          </div>
          <div v-else-if="flStatus">
            <!-- Current Levels -->
            <div class="level-cards">
              <Card class="level-card">
                <template #title>Domain Functional Level</template>
                <template #content>
                  <Tag :value="domainLevelLabel" severity="info" class="level-tag" />
                  <div class="mt-2 text-sm text-color-secondary">
                    Max achievable: {{ functionalLevelLabels[flStatus.maxDomainLevel] ?? `Level ${flStatus.maxDomainLevel}` }}
                  </div>
                  <Button
                    label="Raise Level"
                    icon="pi pi-arrow-up"
                    class="mt-2"
                    @click="openRaiseDomainDialog"
                    :disabled="availableDomainLevels.length === 0"
                    severity="warning"
                    size="small"
                  />
                </template>
              </Card>

              <Card class="level-card">
                <template #title>Forest Functional Level</template>
                <template #content>
                  <Tag :value="forestLevelLabel" severity="success" class="level-tag" />
                  <div class="mt-2 text-sm text-color-secondary">
                    Max achievable: {{ functionalLevelLabels[flStatus.maxForestLevel] ?? `Level ${flStatus.maxForestLevel}` }}
                  </div>
                  <Button
                    label="Raise Level"
                    icon="pi pi-arrow-up"
                    class="mt-2"
                    @click="openRaiseForestDialog"
                    :disabled="availableForestLevels.length === 0"
                    severity="warning"
                    size="small"
                  />
                </template>
              </Card>
            </div>

            <!-- Blocking DCs -->
            <Message v-if="flStatus.blockingDcs.length > 0" severity="warn" class="mt-3">
              The following DCs are preventing a functional level upgrade:
              <strong>{{ flStatus.blockingDcs.join(', ') }}</strong>.
              Upgrade or remove these DCs to raise the level.
            </Message>

            <!-- Features Table -->
            <h3 class="mt-4">Features</h3>
            <DataTable :value="flStatus.availableFeatures" stripedRows>
              <Column header="Status" style="width: 80px">
                <template #body="{ data }">
                  <i v-if="data.isEnabled" class="pi pi-check-circle" style="color: var(--green-500); font-size: 1.2rem" />
                  <i v-else class="pi pi-circle" style="color: var(--surface-400); font-size: 1.2rem" />
                </template>
              </Column>
              <Column header="Feature">
                <template #body="{ data }">
                  <strong>{{ data.name }}</strong>
                  <div class="text-sm text-color-secondary">{{ data.description }}</div>
                </template>
              </Column>
              <Column header="Required Level">
                <template #body="{ data }">
                  <Tag :value="featureLevelLabel(data.requiredDomainLevel)" :severity="data.isEnabled ? 'success' : 'secondary'" />
                </template>
              </Column>
            </DataTable>

            <Button label="Refresh" icon="pi pi-refresh" class="mt-3" @click="loadFunctionalLevels" :loading="flLoading" text />
          </div>

          <!-- Raise Domain Level Dialog -->
          <Dialog v-model:visible="raiseDomainDialogVisible" header="Raise Domain Functional Level" :modal="true" :style="{ width: '500px' }">
            <Message severity="warn">
              Raising the domain functional level is an IRREVERSIBLE operation.
              Once raised, it cannot be lowered.
            </Message>
            <div class="form-field mt-3">
              <label>Target Level</label>
              <Dropdown
                v-model="selectedDomainLevel"
                :options="availableDomainLevels"
                optionLabel="label"
                optionValue="value"
                placeholder="Select target level"
                class="w-full"
              />
            </div>
            <template #footer>
              <Button label="Cancel" @click="raiseDomainDialogVisible = false" text />
              <Button label="Raise Level" icon="pi pi-arrow-up" @click="onRaiseDomainLevel" :loading="raising" :disabled="selectedDomainLevel == null" severity="warning" />
            </template>
          </Dialog>

          <!-- Raise Forest Level Dialog -->
          <Dialog v-model:visible="raiseForestDialogVisible" header="Raise Forest Functional Level" :modal="true" :style="{ width: '500px' }">
            <Message severity="warn">
              Raising the forest functional level is an IRREVERSIBLE operation.
              All domains in the forest must already be at this level or higher.
            </Message>
            <div class="form-field mt-3">
              <label>Target Level</label>
              <Dropdown
                v-model="selectedForestLevel"
                :options="availableForestLevels"
                optionLabel="label"
                optionValue="value"
                placeholder="Select target level"
                class="w-full"
              />
            </div>
            <template #footer>
              <Button label="Cancel" @click="raiseForestDialogVisible = false" text />
              <Button label="Raise Level" icon="pi pi-arrow-up" @click="onRaiseForestLevel" :loading="raising" :disabled="selectedForestLevel == null" severity="warning" />
            </template>
          </Dialog>
        </div>
      </TabPanel>
    </TabView>
  </div>
</template>

<style scoped>
.dc-management-view {
  padding: 1.5rem;
}

.dc-management-view h2 {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.loading-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 2rem;
  gap: 1rem;
}

.tab-content {
  padding: 1rem 0;
}

.form-grid {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.field-checkbox {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.button-row {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.mb-3 {
  margin-bottom: 1rem;
}

.mt-2 {
  margin-top: 0.5rem;
}

.mt-3 {
  margin-top: 1rem;
}

.mt-4 {
  margin-top: 1.5rem;
}

.ml-1 {
  margin-left: 0.25rem;
}

.ml-2 {
  margin-left: 0.5rem;
}

.text-sm {
  font-size: 0.875rem;
}

.text-color-secondary {
  color: var(--text-color-secondary);
}

.w-full {
  width: 100%;
}

.level-cards {
  display: flex;
  gap: 1rem;
  flex-wrap: wrap;
}

.level-card {
  flex: 1;
  min-width: 280px;
}

.level-tag {
  font-size: 1rem;
}

.demotion-progress {
  max-width: 600px;
}

.phase-text {
  margin-top: 0.75rem;
  color: var(--text-color-secondary);
  font-style: italic;
}
</style>
