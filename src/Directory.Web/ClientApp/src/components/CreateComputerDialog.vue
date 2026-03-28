<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Checkbox from 'primevue/checkbox'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import { post } from '../api/client'
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

const computerName = ref('')
const samAccountName = ref('')
const description = ref('')
const containerDnInput = ref('')
const managedBy = ref('')
const operatingSystem = ref('')
const operatingSystemVersion = ref('')
const dnsHostName = ref('')
const allowDomainJoin = ref(true)

const canSave = computed(() => computerName.value.trim().length > 0)

/** Auto-append $ to sAMAccountName for computer accounts */
const effectiveSamAccountName = computed(() => {
  const name = samAccountName.value || computerName.value
  return name.endsWith('$') ? name : `${name}$`
})

watch(() => props.visible, (v) => {
  if (v) {
    computerName.value = ''
    samAccountName.value = ''
    description.value = ''
    containerDnInput.value = props.containerDn || ''
    managedBy.value = ''
    operatingSystem.value = ''
    operatingSystemVersion.value = ''
    dnsHostName.value = ''
    allowDomainJoin.value = true
    showAdvanced.value = false
  }
})

watch(computerName, (val) => {
  if (!samAccountName.value || samAccountName.value === computerName.value.toUpperCase()) {
    samAccountName.value = val.toUpperCase()
  }
})

async function onSubmit() {
  if (!canSave.value) return
  saving.value = true
  try {
    await post('/computers', {
      containerDn: containerDnInput.value || props.containerDn,
      cn: computerName.value,
      samAccountName: effectiveSamAccountName.value,
      description: description.value || undefined,
      managedBy: managedBy.value || undefined,
      operatingSystem: operatingSystem.value || undefined,
      operatingSystemVersion: operatingSystemVersion.value || undefined,
      dnsHostName: dnsHostName.value || undefined,
      userAccountControl: allowDomainJoin.value ? 4096 : 4128, // WORKSTATION_TRUST_ACCOUNT | optional PASSWD_NOTREQD
    })
    toast.add({ severity: 'success', summary: 'Created', detail: 'Computer account created successfully', life: 3000 })
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
  <Dialog :visible="visible" @update:visible="close" header="Create Computer" modal :style="{ width: '520px' }">
    <div class="form-stack">
      <div class="form-row">
        <label>Computer Name *</label>
        <InputText v-model="computerName" class="form-input" placeholder="WORKSTATION01" />
      </div>
      <div class="form-row">
        <label>Pre-Windows 2000 Name (sAMAccountName)</label>
        <div style="display: flex; align-items: center; gap: 0">
          <InputText v-model="samAccountName" style="flex: 1; border-top-right-radius: 0; border-bottom-right-radius: 0" />
          <span style="background: var(--app-neutral-bg); border: 1px solid var(--p-surface-border); border-left: none; padding: 0.5rem 0.75rem; font-size: 0.875rem; color: var(--p-text-color); border-top-right-radius: 6px; border-bottom-right-radius: 6px">$</span>
        </div>
      </div>
      <div class="form-row">
        <label>Description</label>
        <Textarea v-model="description" class="form-input" rows="2" autoResize />
      </div>
      <div class="form-row">
        <label>Container DN</label>
        <InputText v-model="containerDnInput" class="form-input" placeholder="CN=Computers,DC=example,DC=com" />
      </div>
      <div class="form-row">
        <div style="display: flex; align-items: center; gap: 0.5rem; margin-top: 0.25rem">
          <Checkbox v-model="allowDomainJoin" :binary="true" inputId="chk-domain-join" />
          <label for="chk-domain-join" style="font-weight: 400">Allow this computer to join the domain</label>
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
          <label>DNS Host Name</label>
          <InputText v-model="dnsHostName" class="form-input" placeholder="workstation01.domain.com" />
        </div>
        <div class="form-row">
          <label>Operating System</label>
          <InputText v-model="operatingSystem" class="form-input" placeholder="Windows 11 Enterprise" />
        </div>
        <div class="form-row">
          <label>Operating System Version</label>
          <InputText v-model="operatingSystemVersion" class="form-input" placeholder="10.0 (22631)" />
        </div>
        <div class="form-row">
          <label>Managed By</label>
          <DnPicker v-model="managedBy" label="Managed By" objectFilter="(|(objectClass=user)(objectClass=group))" />
        </div>
      </div>
    </div>

    <template #footer>
      <Button label="Cancel" severity="secondary" text @click="close" />
      <Button label="Create Computer" icon="pi pi-desktop" @click="onSubmit" :loading="saving" :disabled="!canSave" />
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
