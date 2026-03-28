<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import Stepper from 'primevue/stepper'
import StepList from 'primevue/steplist'
import StepPanels from 'primevue/steppanels'
import Step from 'primevue/step'
import StepPanel from 'primevue/steppanel'
import Checkbox from 'primevue/checkbox'
import RadioButton from 'primevue/radiobutton'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import DnPicker from './DnPicker.vue'
import { addAce } from '../api/security'
import { get } from '../api/client'
import { DELEGATION_TASKS, ACCESS_MASK } from '../types/security'
import type { AceDto, DelegationTask } from '../types/security'

const props = defineProps<{
  visible: boolean
  containerDn: string
}>()

const emit = defineEmits<{
  'update:visible': [val: boolean]
  delegated: []
}>()

const toast = useToast()

// Step state
const activeStep = ref('1')
const delegating = ref(false)

// Step 1: Select principals
const selectedPrincipalDns = ref<string[]>([''])
function addPrincipalSlot() {
  selectedPrincipalDns.value.push('')
}
function removePrincipalSlot(index: number) {
  selectedPrincipalDns.value.splice(index, 1)
}
function updatePrincipal(index: number, val: string) {
  selectedPrincipalDns.value[index] = val
}

const validPrincipals = computed(() => selectedPrincipalDns.value.filter(dn => dn.trim() !== ''))

// Step 2: Select tasks
const delegationMode = ref<'common' | 'custom'>('common')
const selectedTasks = ref<number[]>([])

// Custom task state
const customObjectTypes = ref<string[]>([])
const customPermissions = ref(0)

const objectTypeOptions = [
  { label: 'User objects', value: 'user' },
  { label: 'Group objects', value: 'group' },
  { label: 'Computer objects', value: 'computer' },
  { label: 'Organizational Unit objects', value: 'organizationalUnit' },
  { label: 'inetOrgPerson objects', value: 'inetOrgPerson' },
  { label: 'Contact objects', value: 'contact' },
]

const customPermissionOptions = [
  { label: 'Full Control', mask: ACCESS_MASK.FULL_CONTROL },
  { label: 'Read', mask: ACCESS_MASK.READ_PROPERTY | ACCESS_MASK.LIST_CONTENTS | ACCESS_MASK.READ_PERMISSIONS | ACCESS_MASK.LIST_OBJECT },
  { label: 'Write', mask: ACCESS_MASK.WRITE_PROPERTY },
  { label: 'Create All Child Objects', mask: ACCESS_MASK.CREATE_CHILD },
  { label: 'Delete All Child Objects', mask: ACCESS_MASK.DELETE_CHILD },
  { label: 'Read Permissions', mask: ACCESS_MASK.READ_PERMISSIONS },
  { label: 'Modify Permissions', mask: ACCESS_MASK.MODIFY_PERMISSIONS },
  { label: 'Modify Owner', mask: ACCESS_MASK.MODIFY_OWNER },
]

// Step 3: Review
interface ReviewEntry {
  principal: string
  task: string
  permission: string
}

const reviewEntries = computed<ReviewEntry[]>(() => {
  const entries: ReviewEntry[] = []
  for (const dn of validPrincipals.value) {
    if (delegationMode.value === 'common') {
      for (const idx of selectedTasks.value) {
        const task = DELEGATION_TASKS[idx]
        entries.push({
          principal: dn.split(',')[0]?.replace(/^CN=/i, '') || dn,
          task: task.name,
          permission: `0x${task.permissions.toString(16).toUpperCase().padStart(8, '0')}`,
        })
      }
    } else {
      entries.push({
        principal: dn.split(',')[0]?.replace(/^CN=/i, '') || dn,
        task: `Custom: ${customObjectTypes.value.join(', ') || 'All objects'}`,
        permission: `0x${customPermissions.value.toString(16).toUpperCase().padStart(8, '0')}`,
      })
    }
  }
  return entries
})

const canProceedStep1 = computed(() => validPrincipals.value.length > 0)
const canProceedStep2 = computed(() => {
  if (delegationMode.value === 'common') return selectedTasks.value.length > 0
  return customPermissions.value > 0
})

// Reset when opening
watch(() => props.visible, (val) => {
  if (val) {
    activeStep.value = '1'
    selectedPrincipalDns.value = ['']
    delegationMode.value = 'common'
    selectedTasks.value = []
    customObjectTypes.value = []
    customPermissions.value = 0
    delegating.value = false
  }
})

async function resolveSid(dn: string): Promise<string | null> {
  try {
    const detail: any = await get(`/objects/by-dn?dn=${encodeURIComponent(dn)}`)
    return detail?.objectSid || null
  } catch {
    return null
  }
}

async function applyDelegation() {
  delegating.value = true
  try {
    let successCount = 0
    let errorCount = 0

    for (const principalDn of validPrincipals.value) {
      const sid = await resolveSid(principalDn)
      if (!sid) {
        toast.add({ severity: 'error', summary: 'Error', detail: `Could not resolve SID for ${principalDn}`, life: 5000 })
        errorCount++
        continue
      }

      if (delegationMode.value === 'common') {
        for (const idx of selectedTasks.value) {
          const task = DELEGATION_TASKS[idx]
          const ace: AceDto = {
            type: 'allow',
            principalSid: sid,
            accessMask: task.permissions,
            flags: ['CONTAINER_INHERIT'],
            objectType: task.objectTypeGuid || undefined,
            inheritedObjectType: task.objectTypeGuid || undefined,
            isObjectAce: !!task.objectTypeGuid,
          }
          try {
            await addAce(props.containerDn, ace)
            successCount++
          } catch (e: any) {
            errorCount++
            toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
          }
        }
      } else {
        const ace: AceDto = {
          type: 'allow',
          principalSid: sid,
          accessMask: customPermissions.value,
          flags: ['CONTAINER_INHERIT'],
        }
        try {
          await addAce(props.containerDn, ace)
          successCount++
        } catch (e: any) {
          errorCount++
          toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
        }
      }
    }

    if (successCount > 0) {
      toast.add({
        severity: 'success',
        summary: 'Delegation Applied',
        detail: `Successfully applied ${successCount} permission(s)${errorCount > 0 ? `, ${errorCount} failed` : ''}`,
        life: 3000,
      })
      emit('delegated')
      emit('update:visible', false)
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    delegating.value = false
  }
}

function toggleCustomPermission(mask: number) {
  if ((customPermissions.value & mask) === mask) {
    customPermissions.value &= ~mask
  } else {
    customPermissions.value |= mask
  }
}
</script>

<template>
  <Dialog
    :visible="visible"
    @update:visible="emit('update:visible', $event)"
    header="Delegation of Control Wizard"
    modal
    :style="{ width: '700px' }"
    :closable="true"
  >
    <div class="delegate-wizard">
      <div class="delegate-target">
        <span class="target-label">Container:</span>
        <code class="target-dn">{{ containerDn }}</code>
      </div>

      <Stepper :value="activeStep" linear>
        <StepList>
          <Step value="1">Select Users or Groups</Step>
          <Step value="2">Tasks to Delegate</Step>
          <Step value="3">Review and Apply</Step>
        </StepList>
        <StepPanels>
          <!-- Step 1: Select Principals -->
          <StepPanel value="1" v-slot="{ activateCallback }">
            <div class="step-content">
              <p class="step-description">
                Select the users or groups to whom you want to delegate control:
              </p>
              <div class="principal-list">
                <div v-for="(dn, i) in selectedPrincipalDns" :key="i" class="principal-row">
                  <DnPicker
                    :modelValue="dn"
                    @update:modelValue="(v: string) => updatePrincipal(i, v)"
                    label=""
                    objectFilter="(|(objectClass=user)(objectClass=group))"
                  />
                  <Button
                    v-if="selectedPrincipalDns.length > 1"
                    icon="pi pi-times"
                    severity="danger"
                    text
                    size="small"
                    @click="removePrincipalSlot(i)"
                  />
                </div>
                <Button
                  label="Add another..."
                  icon="pi pi-plus"
                  text
                  size="small"
                  @click="addPrincipalSlot"
                />
              </div>
            </div>
            <div class="step-actions">
              <Button label="Next" icon="pi pi-arrow-right" iconPos="right" :disabled="!canProceedStep1" @click="activateCallback('2')" />
            </div>
          </StepPanel>

          <!-- Step 2: Select Tasks -->
          <StepPanel value="2" v-slot="{ activateCallback }">
            <div class="step-content">
              <div class="mode-selection">
                <div class="radio-option">
                  <RadioButton v-model="delegationMode" value="common" inputId="mode-common" />
                  <label for="mode-common">Delegate the following common tasks:</label>
                </div>
                <div class="radio-option">
                  <RadioButton v-model="delegationMode" value="custom" inputId="mode-custom" />
                  <label for="mode-custom">Create a custom task to delegate</label>
                </div>
              </div>

              <!-- Common Tasks -->
              <div v-if="delegationMode === 'common'" class="task-list">
                <div
                  v-for="(task, i) in DELEGATION_TASKS"
                  :key="i"
                  class="task-row"
                >
                  <Checkbox
                    :modelValue="selectedTasks.includes(i)"
                    :binary="true"
                    :inputId="`task-${i}`"
                    @update:modelValue="(v: boolean) => {
                      if (v) selectedTasks.push(i)
                      else selectedTasks = selectedTasks.filter(x => x !== i)
                    }"
                  />
                  <div class="task-info">
                    <label :for="`task-${i}`" class="task-name">{{ task.name }}</label>
                    <span class="task-desc">{{ task.description }}</span>
                  </div>
                </div>
              </div>

              <!-- Custom Tasks -->
              <div v-else class="custom-task-section">
                <div class="custom-group">
                  <div class="custom-group-header">Object types (optional filter):</div>
                  <div class="custom-object-types">
                    <div v-for="opt in objectTypeOptions" :key="opt.value" class="check-row">
                      <Checkbox
                        :modelValue="customObjectTypes.includes(opt.value)"
                        :binary="true"
                        :inputId="`ot-${opt.value}`"
                        @update:modelValue="(v: boolean) => {
                          if (v) customObjectTypes.push(opt.value)
                          else customObjectTypes = customObjectTypes.filter(x => x !== opt.value)
                        }"
                      />
                      <label :for="`ot-${opt.value}`">{{ opt.label }}</label>
                    </div>
                  </div>
                </div>
                <div class="custom-group">
                  <div class="custom-group-header">Permissions:</div>
                  <div class="custom-permissions">
                    <div v-for="opt in customPermissionOptions" :key="opt.mask" class="check-row">
                      <Checkbox
                        :modelValue="(customPermissions & opt.mask) === opt.mask"
                        :binary="true"
                        :inputId="`cp-${opt.mask}`"
                        @update:modelValue="() => toggleCustomPermission(opt.mask)"
                      />
                      <label :for="`cp-${opt.mask}`">{{ opt.label }}</label>
                    </div>
                  </div>
                </div>
              </div>
            </div>
            <div class="step-actions">
              <Button label="Back" icon="pi pi-arrow-left" severity="secondary" @click="activateCallback('1')" />
              <Button label="Next" icon="pi pi-arrow-right" iconPos="right" :disabled="!canProceedStep2" @click="activateCallback('3')" />
            </div>
          </StepPanel>

          <!-- Step 3: Review -->
          <StepPanel value="3" v-slot="{ activateCallback }">
            <div class="step-content">
              <p class="step-description">
                Review the delegations to be applied:
              </p>
              <DataTable :value="reviewEntries" size="small" stripedRows class="review-table">
                <Column field="principal" header="Principal" />
                <Column field="task" header="Task" />
                <Column field="permission" header="Access Mask" style="width: 140px">
                  <template #body="{ data }">
                    <code>{{ data.permission }}</code>
                  </template>
                </Column>
              </DataTable>
            </div>
            <div class="step-actions">
              <Button label="Back" icon="pi pi-arrow-left" severity="secondary" @click="activateCallback('2')" />
              <Button label="Finish" icon="pi pi-check" :loading="delegating" @click="applyDelegation" />
            </div>
          </StepPanel>
        </StepPanels>
      </Stepper>
    </div>
  </Dialog>
</template>

<style scoped>
.delegate-wizard {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.delegate-target {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 0.75rem;
  background: var(--p-surface-ground);
  border: 1px solid var(--app-neutral-border);
  border-radius: 0.375rem;
  font-size: 0.8125rem;
}

.target-label {
  font-weight: 600;
  white-space: nowrap;
}

.target-dn {
  font-family: 'Cascadia Code', 'Fira Code', Consolas, monospace;
  color: var(--p-text-color);
  word-break: break-all;
}

.step-content {
  min-height: 250px;
  padding: 0.5rem 0;
}

.step-description {
  font-size: 0.875rem;
  color: var(--p-text-color);
  margin-bottom: 0.75rem;
}

.step-actions {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  padding-top: 0.75rem;
  border-top: 1px solid var(--app-neutral-border);
}

.principal-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.principal-row {
  display: flex;
  align-items: flex-end;
  gap: 0.5rem;
}

.principal-row > :first-child {
  flex: 1;
}

.mode-selection {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.radio-option {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.radio-option label {
  font-size: 0.875rem;
  cursor: pointer;
}

.task-list {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  max-height: 280px;
  overflow-y: auto;
}

.task-row {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
  padding: 0.375rem 0;
}

.task-info {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
}

.task-name {
  font-size: 0.8125rem;
  font-weight: 500;
  cursor: pointer;
}

.task-desc {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
}

.custom-task-section {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.custom-group {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.custom-group-header {
  font-weight: 600;
  font-size: 0.8125rem;
  color: var(--p-text-color);
}

.custom-object-types,
.custom-permissions {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  padding-left: 0.25rem;
}

.check-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.check-row label {
  font-size: 0.8125rem;
  cursor: pointer;
}

.review-table {
  margin-top: 0.5rem;
}
</style>
