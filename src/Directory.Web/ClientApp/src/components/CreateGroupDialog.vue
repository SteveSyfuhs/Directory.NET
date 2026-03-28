<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import RadioButton from 'primevue/radiobutton'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import { createGroup } from '../api/groups'
import DnPicker from './DnPicker.vue'

const props = defineProps<{
  visible: boolean
  containerDn: string
}>()

const emit = defineEmits<{
  'update:visible': [val: boolean]
  created: []
}>()

const toast = useToast()
const saving = ref(false)
const showAdvanced = ref(false)

const groupName = ref('')
const samAccountName = ref('')
const description = ref('')
const groupScope = ref<'Global' | 'DomainLocal' | 'Universal'>('Global')
const groupType = ref<'Security' | 'Distribution'>('Security')
const containerDnInput = ref('')

// Advanced fields
const mail = ref('')
const notes = ref('')
const managedBy = ref('')

const canSave = computed(() => groupName.value.trim().length > 0)

watch(() => props.visible, (v) => {
  if (v) {
    groupName.value = ''
    samAccountName.value = ''
    description.value = ''
    groupScope.value = 'Global'
    groupType.value = 'Security'
    containerDnInput.value = props.containerDn || ''
    showAdvanced.value = false
    mail.value = ''
    notes.value = ''
    managedBy.value = ''
  }
})

watch(groupName, (val) => {
  if (!samAccountName.value || samAccountName.value === groupName.value.replace(/\s+/g, '')) {
    samAccountName.value = val.replace(/\s+/g, '')
  }
})

async function onSubmit() {
  if (!canSave.value) return
  saving.value = true
  try {
    const extraAttributes: Record<string, string> = {}
    if (mail.value) extraAttributes.mail = mail.value
    if (notes.value) extraAttributes.info = notes.value
    if (managedBy.value) extraAttributes.managedBy = managedBy.value

    await createGroup({
      containerDn: containerDnInput.value || props.containerDn,
      cn: groupName.value,
      samAccountName: samAccountName.value || groupName.value.replace(/\s+/g, ''),
      description: description.value || undefined,
      groupScope: groupScope.value,
      groupType: groupType.value,
      extraAttributes: Object.keys(extraAttributes).length > 0 ? extraAttributes : undefined,
    })
    toast.add({ severity: 'success', summary: 'Created', detail: 'Group created successfully', life: 3000 })
    emit('created')
    emit('update:visible', false)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

function close() {
  emit('update:visible', false)
}
</script>

<template>
  <Dialog :visible="visible" @update:visible="close" header="Create Group" modal :style="{ width: '520px' }">
    <div class="form-stack">
      <div class="form-row">
        <label>Group Name *</label>
        <InputText v-model="groupName" class="form-input" />
      </div>
      <div class="form-row">
        <label>sAMAccountName</label>
        <InputText v-model="samAccountName" class="form-input" />
      </div>
      <div class="form-row">
        <label>Description</label>
        <Textarea v-model="description" class="form-input" rows="2" autoResize />
      </div>
      <div class="form-row">
        <label>Email</label>
        <InputText v-model="mail" class="form-input" placeholder="group@example.com" type="email" />
      </div>
      <div class="form-row">
        <label>Container DN</label>
        <InputText v-model="containerDnInput" class="form-input" placeholder="CN=Users,DC=example,DC=com" />
      </div>
      <div class="form-row">
        <label>Group Scope</label>
        <div style="display: flex; gap: 1.25rem; margin-top: 0.25rem">
          <div style="display: flex; align-items: center; gap: 0.375rem">
            <RadioButton v-model="groupScope" inputId="scope-global" value="Global" />
            <label for="scope-global" style="font-weight: 400">Global</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.375rem">
            <RadioButton v-model="groupScope" inputId="scope-dl" value="DomainLocal" />
            <label for="scope-dl" style="font-weight: 400">Domain Local</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.375rem">
            <RadioButton v-model="groupScope" inputId="scope-universal" value="Universal" />
            <label for="scope-universal" style="font-weight: 400">Universal</label>
          </div>
        </div>
      </div>
      <div class="form-row">
        <label>Group Type</label>
        <div style="display: flex; gap: 1.25rem; margin-top: 0.25rem">
          <div style="display: flex; align-items: center; gap: 0.375rem">
            <RadioButton v-model="groupType" inputId="type-security" value="Security" />
            <label for="type-security" style="font-weight: 400">Security</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.375rem">
            <RadioButton v-model="groupType" inputId="type-dist" value="Distribution" />
            <label for="type-dist" style="font-weight: 400">Distribution</label>
          </div>
        </div>
      </div>
    </div>

    <!-- Advanced Section -->
    <div class="advanced-toggle" @click="showAdvanced = !showAdvanced">
      <i :class="showAdvanced ? 'pi pi-chevron-down' : 'pi pi-chevron-right'" style="font-size: 0.75rem"></i>
      <span>Advanced Attributes</span>
    </div>

    <div v-if="showAdvanced" class="advanced-section">
      <div class="form-stack">
        <div class="form-row">
          <label>Notes</label>
          <Textarea v-model="notes" class="form-input" rows="3" autoResize />
        </div>
        <div class="form-row">
          <label>Managed By</label>
          <DnPicker v-model="managedBy" label="Managed By" objectFilter="(|(objectClass=user)(objectClass=group))" />
        </div>
      </div>
    </div>

    <template #footer>
      <Button label="Cancel" severity="secondary" text @click="close" />
      <Button label="Create Group" icon="pi pi-users" @click="onSubmit" :loading="saving" :disabled="!canSave" />
    </template>
  </Dialog>
</template>

<style scoped>
.form-stack {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.form-row {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.form-row label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
}

.form-input {
  width: 100%;
}

.advanced-toggle {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-top: 1.25rem;
  padding: 0.5rem 0;
  cursor: pointer;
  color: var(--app-info-text);
  font-size: 0.875rem;
  font-weight: 500;
  user-select: none;
  border-top: 1px solid var(--app-neutral-border);
}

.advanced-toggle:hover {
  color: var(--app-info-text-strong);
}

.advanced-section {
  margin-top: 0.5rem;
}
</style>
