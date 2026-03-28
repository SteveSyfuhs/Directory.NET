<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import RadioButton from 'primevue/radiobutton'
import Checkbox from 'primevue/checkbox'
import Select from 'primevue/select'
import InputText from 'primevue/inputtext'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import Divider from 'primevue/divider'
import DnPicker from './DnPicker.vue'
import type { AceDto } from '../types/security'
import {
  ACCESS_MASK,
  APPLIES_TO_OPTIONS,
  PERMISSION_DEFINITIONS,
  EXTENDED_RIGHTS_GUIDS,
} from '../types/security'

const props = defineProps<{
  visible: boolean
  mode: 'add' | 'edit'
  ace?: AceDto | null
}>()

const emit = defineEmits<{
  'update:visible': [val: boolean]
  save: [ace: AceDto]
}>()

// Form state
const aceType = ref<'allow' | 'deny'>('allow')
const principalDn = ref('')
const principalSid = ref('')
const useManualSid = ref(false)
const appliesToIndex = ref(1) // default: This object and all descendant objects
const activeTab = ref('permissions')

// Permission checkboxes
const permFullControl = ref(false)
const permReadProperties = ref(false)
const permWriteProperties = ref(false)
const permCreateChild = ref(false)
const permDeleteChild = ref(false)
const permReadPermissions = ref(false)
const permModifyPermissions = ref(false)
const permModifyOwner = ref(false)
const permDelete = ref(false)
const permDeleteTree = ref(false)
const permListContents = ref(false)
const permListObject = ref(false)
const permControlAccess = ref(false)
const permSelf = ref(false)

// Property-specific object type GUID (for object-specific permissions)
const specificObjectType = ref('')

const dialogTitle = computed(() => props.mode === 'add' ? 'Add Permission Entry' : 'Edit Permission Entry')

// Watch for Full Control toggle
watch(permFullControl, (val) => {
  if (val) {
    permReadProperties.value = true
    permWriteProperties.value = true
    permCreateChild.value = true
    permDeleteChild.value = true
    permReadPermissions.value = true
    permModifyPermissions.value = true
    permModifyOwner.value = true
    permDelete.value = true
    permDeleteTree.value = true
    permListContents.value = true
    permListObject.value = true
    permControlAccess.value = true
    permSelf.value = true
  }
})

// Initialize from existing ACE when editing
watch(() => props.visible, (val) => {
  if (val && props.ace && props.mode === 'edit') {
    loadFromAce(props.ace)
  } else if (val && props.mode === 'add') {
    resetForm()
  }
}, { immediate: true })

function loadFromAce(ace: AceDto) {
  aceType.value = ace.type
  principalSid.value = ace.principalSid
  useManualSid.value = true

  // Determine applies-to from flags
  const hasCI = ace.flags.includes('CONTAINER_INHERIT')
  const hasIO = ace.flags.includes('INHERIT_ONLY')
  const iot = ace.inheritedObjectType

  if (!hasCI && !hasIO) {
    appliesToIndex.value = 0 // This object only
  } else if (hasCI && !hasIO) {
    appliesToIndex.value = 1 // This object and all descendants
  } else if (hasCI && hasIO && !iot) {
    appliesToIndex.value = 2 // All descendant objects only
  } else {
    // Try to match inherited object type
    const idx = APPLIES_TO_OPTIONS.findIndex(
      o => 'inheritedObjectType' in o && o.inheritedObjectType === iot
    )
    appliesToIndex.value = idx >= 0 ? idx : 2
  }

  // Load permissions from access mask
  const mask = ace.accessMask
  permFullControl.value = (mask & ACCESS_MASK.FULL_CONTROL) === ACCESS_MASK.FULL_CONTROL
  permReadProperties.value = (mask & ACCESS_MASK.READ_PROPERTY) !== 0
  permWriteProperties.value = (mask & ACCESS_MASK.WRITE_PROPERTY) !== 0
  permCreateChild.value = (mask & ACCESS_MASK.CREATE_CHILD) !== 0
  permDeleteChild.value = (mask & ACCESS_MASK.DELETE_CHILD) !== 0
  permReadPermissions.value = (mask & ACCESS_MASK.READ_PERMISSIONS) !== 0
  permModifyPermissions.value = (mask & ACCESS_MASK.MODIFY_PERMISSIONS) !== 0
  permModifyOwner.value = (mask & ACCESS_MASK.MODIFY_OWNER) !== 0
  permDelete.value = (mask & ACCESS_MASK.DELETE) !== 0
  permDeleteTree.value = (mask & ACCESS_MASK.DELETE_TREE) !== 0
  permListContents.value = (mask & ACCESS_MASK.LIST_CONTENTS) !== 0
  permListObject.value = (mask & ACCESS_MASK.LIST_OBJECT) !== 0
  permControlAccess.value = (mask & ACCESS_MASK.CONTROL_ACCESS) !== 0
  permSelf.value = (mask & ACCESS_MASK.SELF) !== 0

  specificObjectType.value = ace.objectType || ''
}

function resetForm() {
  aceType.value = 'allow'
  principalDn.value = ''
  principalSid.value = ''
  useManualSid.value = false
  appliesToIndex.value = 1
  activeTab.value = 'permissions'
  permFullControl.value = false
  permReadProperties.value = false
  permWriteProperties.value = false
  permCreateChild.value = false
  permDeleteChild.value = false
  permReadPermissions.value = false
  permModifyPermissions.value = false
  permModifyOwner.value = false
  permDelete.value = false
  permDeleteTree.value = false
  permListContents.value = false
  permListObject.value = false
  permControlAccess.value = false
  permSelf.value = false
  specificObjectType.value = ''
}

const computedAccessMask = computed(() => {
  let mask = 0
  if (permFullControl.value) return ACCESS_MASK.FULL_CONTROL
  if (permReadProperties.value) mask |= ACCESS_MASK.READ_PROPERTY
  if (permWriteProperties.value) mask |= ACCESS_MASK.WRITE_PROPERTY
  if (permCreateChild.value) mask |= ACCESS_MASK.CREATE_CHILD
  if (permDeleteChild.value) mask |= ACCESS_MASK.DELETE_CHILD
  if (permReadPermissions.value) mask |= ACCESS_MASK.READ_PERMISSIONS
  if (permModifyPermissions.value) mask |= ACCESS_MASK.MODIFY_PERMISSIONS
  if (permModifyOwner.value) mask |= ACCESS_MASK.MODIFY_OWNER
  if (permDelete.value) mask |= ACCESS_MASK.DELETE
  if (permDeleteTree.value) mask |= ACCESS_MASK.DELETE_TREE
  if (permListContents.value) mask |= ACCESS_MASK.LIST_CONTENTS
  if (permListObject.value) mask |= ACCESS_MASK.LIST_OBJECT
  if (permControlAccess.value) mask |= ACCESS_MASK.CONTROL_ACCESS
  if (permSelf.value) mask |= ACCESS_MASK.SELF
  return mask
})

const canSave = computed(() => {
  const hasPrincipal = useManualSid.value ? principalSid.value.trim() !== '' : principalDn.value !== ''
  return hasPrincipal && computedAccessMask.value !== 0
})

const appliesToOptions = APPLIES_TO_OPTIONS.map((o, i) => ({ label: o.label, value: i }))

const extendedRightsOptions = Object.entries(EXTENDED_RIGHTS_GUIDS).map(([guid, name]) => ({
  label: name,
  value: guid,
}))

function onSave() {
  const appliesTo = APPLIES_TO_OPTIONS[appliesToIndex.value]
  const flags: string[] = [...appliesTo.flags]
  const inheritedObjectType = 'inheritedObjectType' in appliesTo ? appliesTo.inheritedObjectType : undefined

  const ace: AceDto = {
    type: aceType.value,
    principalSid: useManualSid.value ? principalSid.value.trim() : principalSid.value,
    accessMask: computedAccessMask.value,
    flags,
    objectType: specificObjectType.value || undefined,
    inheritedObjectType: inheritedObjectType || undefined,
    isObjectAce: !!(specificObjectType.value || inheritedObjectType),
  }

  emit('save', ace)
  emit('update:visible', false)
}

function onCancel() {
  emit('update:visible', false)
}
</script>

<template>
  <Dialog
    :visible="visible"
    @update:visible="emit('update:visible', $event)"
    :header="dialogTitle"
    modal
    :style="{ width: '680px' }"
    :closable="true"
  >
    <div class="ace-editor">
      <!-- Principal Selection -->
      <div class="ace-section">
        <div class="ace-section-title">Principal</div>
        <div class="principal-toggle">
          <Button
            :label="useManualSid ? 'Use Directory Picker' : 'Enter SID Manually'"
            text
            size="small"
            @click="useManualSid = !useManualSid"
          />
        </div>
        <div v-if="!useManualSid">
          <DnPicker
            v-model="principalDn"
            label="Select user or group"
            objectFilter="(|(objectClass=user)(objectClass=group))"
          />
        </div>
        <div v-else class="manual-sid-input">
          <label class="field-label">Security Identifier (SID)</label>
          <InputText
            v-model="principalSid"
            placeholder="S-1-5-21-..."
            style="width: 100%"
          />
        </div>
      </div>

      <!-- Type: Allow / Deny -->
      <div class="ace-section">
        <div class="ace-section-title">Type</div>
        <div class="type-radios">
          <div class="radio-option">
            <RadioButton v-model="aceType" value="allow" inputId="ace-allow" />
            <label for="ace-allow" class="radio-label">Allow</label>
          </div>
          <div class="radio-option">
            <RadioButton v-model="aceType" value="deny" inputId="ace-deny" />
            <label for="ace-deny" class="radio-label">Deny</label>
          </div>
        </div>
      </div>

      <!-- Applies To -->
      <div class="ace-section">
        <div class="ace-section-title">Applies to</div>
        <Select
          v-model="appliesToIndex"
          :options="appliesToOptions"
          optionLabel="label"
          optionValue="value"
          style="width: 100%"
        />
      </div>

      <Divider />

      <!-- Permissions -->
      <Tabs :value="activeTab">
        <TabList>
          <Tab value="permissions">Permissions</Tab>
          <Tab value="extended">Extended Rights</Tab>
        </TabList>
        <TabPanels>
          <TabPanel value="permissions">
            <div class="permissions-grid">
              <!-- Full Control -->
              <div class="perm-row perm-full-control">
                <Checkbox v-model="permFullControl" :binary="true" inputId="perm-fc" />
                <label for="perm-fc" class="perm-label perm-label-bold">Full Control</label>
              </div>

              <Divider />

              <!-- Properties -->
              <div class="perm-group-header">Properties</div>
              <div class="perm-row">
                <Checkbox v-model="permReadProperties" :binary="true" inputId="perm-rp" />
                <label for="perm-rp" class="perm-label">Read All Properties</label>
              </div>
              <div class="perm-row">
                <Checkbox v-model="permWriteProperties" :binary="true" inputId="perm-wp" />
                <label for="perm-wp" class="perm-label">Write All Properties</label>
              </div>

              <!-- Child Objects -->
              <div class="perm-group-header">Child Objects</div>
              <div class="perm-row">
                <Checkbox v-model="permCreateChild" :binary="true" inputId="perm-cc" />
                <label for="perm-cc" class="perm-label">Create All Child Objects</label>
              </div>
              <div class="perm-row">
                <Checkbox v-model="permDeleteChild" :binary="true" inputId="perm-dc" />
                <label for="perm-dc" class="perm-label">Delete All Child Objects</label>
              </div>

              <!-- Standard Rights -->
              <div class="perm-group-header">Standard Rights</div>
              <div class="perm-row">
                <Checkbox v-model="permReadPermissions" :binary="true" inputId="perm-rperm" />
                <label for="perm-rperm" class="perm-label">Read Permissions</label>
              </div>
              <div class="perm-row">
                <Checkbox v-model="permModifyPermissions" :binary="true" inputId="perm-mperm" />
                <label for="perm-mperm" class="perm-label">Modify Permissions</label>
              </div>
              <div class="perm-row">
                <Checkbox v-model="permModifyOwner" :binary="true" inputId="perm-mo" />
                <label for="perm-mo" class="perm-label">Modify Owner</label>
              </div>
              <div class="perm-row">
                <Checkbox v-model="permDelete" :binary="true" inputId="perm-del" />
                <label for="perm-del" class="perm-label">Delete</label>
              </div>
              <div class="perm-row">
                <Checkbox v-model="permDeleteTree" :binary="true" inputId="perm-dt" />
                <label for="perm-dt" class="perm-label">Delete Subtree</label>
              </div>
              <div class="perm-row">
                <Checkbox v-model="permListContents" :binary="true" inputId="perm-lc" />
                <label for="perm-lc" class="perm-label">List Contents</label>
              </div>
              <div class="perm-row">
                <Checkbox v-model="permListObject" :binary="true" inputId="perm-lo" />
                <label for="perm-lo" class="perm-label">List Object</label>
              </div>
            </div>
          </TabPanel>

          <TabPanel value="extended">
            <div class="permissions-grid">
              <div class="perm-row">
                <Checkbox v-model="permControlAccess" :binary="true" inputId="perm-ca" />
                <label for="perm-ca" class="perm-label perm-label-bold">All Extended Rights</label>
              </div>
              <div class="perm-row">
                <Checkbox v-model="permSelf" :binary="true" inputId="perm-self" />
                <label for="perm-self" class="perm-label perm-label-bold">All Validated Writes</label>
              </div>

              <Divider />

              <div class="perm-group-header">Specific Extended Right / Property Set</div>
              <div class="specific-right-selector">
                <Select
                  v-model="specificObjectType"
                  :options="extendedRightsOptions"
                  optionLabel="label"
                  optionValue="value"
                  placeholder="(None - applies to all)"
                  showClear
                  style="width: 100%"
                />
                <small class="field-hint">
                  Select a specific extended right or property set GUID to scope this ACE, or leave empty for all.
                </small>
              </div>
            </div>
          </TabPanel>
        </TabPanels>
      </Tabs>

      <!-- Access Mask Display -->
      <div class="mask-display">
        <span class="mask-label">Access Mask:</span>
        <code class="mask-value">0x{{ computedAccessMask.toString(16).toUpperCase().padStart(8, '0') }}</code>
      </div>
    </div>

    <template #footer>
      <div class="ace-footer">
        <Button label="Cancel" severity="secondary" text @click="onCancel" />
        <Button label="OK" icon="pi pi-check" @click="onSave" :disabled="!canSave" />
      </div>
    </template>
  </Dialog>
</template>

<style scoped>
.ace-editor {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.ace-section {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.ace-section-title {
  font-weight: 600;
  font-size: 0.8125rem;
  color: var(--p-text-color);
}

.principal-toggle {
  display: flex;
  justify-content: flex-end;
}

.manual-sid-input {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.field-label {
  font-size: 0.8125rem;
  font-weight: 500;
  color: var(--p-text-muted-color);
}

.field-hint {
  color: var(--p-text-muted-color);
  font-size: 0.75rem;
}

.type-radios {
  display: flex;
  gap: 1.5rem;
  align-items: center;
}

.radio-option {
  display: flex;
  align-items: center;
  gap: 0.375rem;
}

.radio-label {
  font-size: 0.875rem;
  cursor: pointer;
}

.permissions-grid {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  max-height: 320px;
  overflow-y: auto;
  padding: 0.5rem 0;
}

.perm-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.125rem 0;
}

.perm-full-control {
  padding: 0.25rem 0;
}

.perm-label {
  font-size: 0.8125rem;
  cursor: pointer;
}

.perm-label-bold {
  font-weight: 600;
}

.perm-group-header {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-top: 0.5rem;
  margin-bottom: 0.125rem;
}

.specific-right-selector {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  margin-top: 0.25rem;
}

.mask-display {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 0.75rem;
  background: var(--p-surface-ground);
  border: 1px solid var(--app-neutral-border);
  border-radius: 0.375rem;
  font-size: 0.8125rem;
}

.mask-label {
  font-weight: 500;
  color: var(--p-text-color);
}

.mask-value {
  font-family: 'Cascadia Code', 'Fira Code', Consolas, monospace;
  color: var(--p-text-color);
}

.ace-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
}
</style>
