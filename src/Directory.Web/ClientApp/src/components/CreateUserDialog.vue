<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Textarea from 'primevue/textarea'
import Checkbox from 'primevue/checkbox'
import Button from 'primevue/button'
import Panel from 'primevue/panel'
import { useToast } from 'primevue/usetoast'
import { createUser } from '../api/users'
import { useDomainStore } from '../stores/domain'
import DnPicker from './DnPicker.vue'
import {
  useFormValidation,
  required,
  minLength,
  maxLength,
  samAccountName as samAccountNameRule,
  matchesField,
} from '../composables/useFormValidation'

const props = defineProps<{
  visible: boolean
  containerDn: string
}>()

const emit = defineEmits<{
  'update:visible': [val: boolean]
  created: []
}>()

const toast = useToast()
const domainStore = useDomainStore()
const saving = ref(false)
const showAdvanced = ref(false)
const { errors, validateField, validateAll, clearErrors } = useFormValidation()

// Basic fields
const firstName = ref('')
const lastName = ref('')
const displayName = ref('')
const samAccountName = ref('')
const upnPrefix = ref('')
const password = ref('')
const confirmPassword = ref('')
const containerDnInput = ref('')
const mustChangePassword = ref(true)
const accountDisabled = ref(false)

// Advanced: Address
const streetAddress = ref('')
const city = ref('')
const state = ref('')
const postalCode = ref('')
const country = ref('')

// Advanced: Phone
const telephoneNumber = ref('')
const homePhone = ref('')
const mobile = ref('')

// Advanced: Organization
const title = ref('')
const department = ref('')
const company = ref('')
const manager = ref('')
const physicalDeliveryOfficeName = ref('')

// Advanced: Other
const mail = ref('')
const initials = ref('')
const description = ref('')

const upnSuffix = computed(() => {
  if (domainStore.config?.domainName) return `@${domainStore.config.domainName}`
  return '@domain.local'
})

const canSave = computed(() =>
  samAccountName.value.trim().length > 0 &&
  password.value.length >= 1 &&
  password.value === confirmPassword.value &&
  Object.keys(errors.value).length === 0
)

watch(() => props.visible, (v) => {
  if (v) {
    firstName.value = ''
    lastName.value = ''
    displayName.value = ''
    samAccountName.value = ''
    upnPrefix.value = ''
    password.value = ''
    confirmPassword.value = ''
    containerDnInput.value = props.containerDn || ''
    mustChangePassword.value = true
    accountDisabled.value = false
    showAdvanced.value = false
    clearErrors()
    // Advanced fields
    streetAddress.value = ''
    city.value = ''
    state.value = ''
    postalCode.value = ''
    country.value = ''
    telephoneNumber.value = ''
    homePhone.value = ''
    mobile.value = ''
    title.value = ''
    department.value = ''
    company.value = ''
    manager.value = ''
    physicalDeliveryOfficeName.value = ''
    mail.value = ''
    initials.value = ''
    description.value = ''
    if (!domainStore.config) domainStore.loadConfig()
  }
})

watch([firstName, lastName], () => {
  if (firstName.value && lastName.value) {
    displayName.value = `${firstName.value} ${lastName.value}`
    if (!samAccountName.value) {
      samAccountName.value = `${firstName.value.charAt(0)}${lastName.value}`.toLowerCase().replace(/[^a-z0-9]/g, '')
    }
    if (!upnPrefix.value) {
      upnPrefix.value = samAccountName.value
    }
  }
})

async function onSubmit() {
  const valid = validateAll({
    samAccountName: [samAccountName.value, [required('Login'), samAccountNameRule(), maxLength('Login', 20)]],
    password: [password.value, [required('Password'), minLength('Password', 1)]],
    confirmPassword: [confirmPassword.value, [required('Confirm Password'), matchesField('Confirm Password', () => password.value)]],
  })
  if (!valid) return
  saving.value = true
  try {
    const extraAttributes: Record<string, string> = {}
    if (streetAddress.value) extraAttributes.streetAddress = streetAddress.value
    if (city.value) extraAttributes.l = city.value
    if (state.value) extraAttributes.st = state.value
    if (postalCode.value) extraAttributes.postalCode = postalCode.value
    if (country.value) extraAttributes.co = country.value
    if (telephoneNumber.value) extraAttributes.telephoneNumber = telephoneNumber.value
    if (homePhone.value) extraAttributes.homePhone = homePhone.value
    if (mobile.value) extraAttributes.mobile = mobile.value
    if (title.value) extraAttributes.title = title.value
    if (department.value) extraAttributes.department = department.value
    if (company.value) extraAttributes.company = company.value
    if (manager.value) extraAttributes.manager = manager.value
    if (physicalDeliveryOfficeName.value) extraAttributes.physicalDeliveryOfficeName = physicalDeliveryOfficeName.value
    if (mail.value) extraAttributes.mail = mail.value
    if (initials.value) extraAttributes.initials = initials.value
    if (description.value) extraAttributes.description = description.value

    await createUser({
      containerDn: containerDnInput.value || props.containerDn,
      cn: displayName.value || samAccountName.value,
      samAccountName: samAccountName.value,
      userPrincipalName: `${upnPrefix.value || samAccountName.value}${upnSuffix.value}`,
      givenName: firstName.value || undefined,
      sn: lastName.value || undefined,
      displayName: displayName.value || undefined,
      password: password.value,
      mustChangePasswordAtNextLogon: mustChangePassword.value,
      accountDisabled: accountDisabled.value,
      extraAttributes: Object.keys(extraAttributes).length > 0 ? extraAttributes : undefined,
    })
    toast.add({ severity: 'success', summary: 'Created', detail: 'User created successfully', life: 3000 })
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
  <Dialog :visible="visible" @update:visible="close" header="Create User" modal :style="{ width: '600px' }">
    <div class="form-grid">
      <div class="form-row">
        <label>First Name</label>
        <InputText v-model="firstName" class="form-input" placeholder="John" />
      </div>
      <div class="form-row">
        <label>Last Name</label>
        <InputText v-model="lastName" class="form-input" placeholder="Doe" />
      </div>
      <div class="form-row">
        <label>Initials</label>
        <InputText v-model="initials" class="form-input" placeholder="J.D." maxlength="6" />
      </div>
      <div class="form-row">
        <label>Display Name</label>
        <InputText v-model="displayName" class="form-input" />
      </div>
      <div class="form-row">
        <label>Login (sAMAccountName) *</label>
        <InputText
          v-model="samAccountName"
          class="form-input"
          :class="{ 'p-invalid': errors.samAccountName }"
          @blur="validateField('samAccountName', samAccountName, [required('Login'), samAccountNameRule(), maxLength('Login', 20)])"
        />
        <small v-if="errors.samAccountName" class="field-error">{{ errors.samAccountName }}</small>
      </div>
      <div class="form-row">
        <label>UPN Prefix</label>
        <div style="display: flex; align-items: center; gap: 0">
          <InputText v-model="upnPrefix" style="flex: 1; border-top-right-radius: 0; border-bottom-right-radius: 0" />
          <span style="background: var(--app-neutral-bg); border: 1px solid var(--p-surface-border); border-left: none; padding: 0.5rem 0.75rem; font-size: 0.875rem; color: var(--p-text-color); border-top-right-radius: 6px; border-bottom-right-radius: 6px; white-space: nowrap">
            {{ upnSuffix }}
          </span>
        </div>
      </div>
      <div class="form-row full-width">
        <label>Email</label>
        <InputText v-model="mail" class="form-input" placeholder="user@example.com" type="email" />
      </div>
      <div class="form-row full-width">
        <label>Description</label>
        <Textarea v-model="description" class="form-input" rows="2" autoResize />
      </div>
      <div class="form-row full-width">
        <label>Container DN</label>
        <InputText v-model="containerDnInput" class="form-input" placeholder="CN=Users,DC=example,DC=com" />
      </div>
      <div class="form-row full-width">
        <label>Password *</label>
        <Password
          v-model="password"
          :feedback="true"
          toggleMask
          class="form-input password-field"
          inputClass="form-input"
          :class="{ 'p-invalid': errors.password }"
          @blur="validateField('password', password, [required('Password'), minLength('Password', 1)])"
        />
        <small v-if="errors.password" class="field-error">{{ errors.password }}</small>
      </div>
      <div class="form-row full-width">
        <label>Confirm Password *</label>
        <Password
          v-model="confirmPassword"
          :feedback="false"
          toggleMask
          class="form-input password-field"
          inputClass="form-input"
          :class="{ 'p-invalid': errors.confirmPassword }"
          @blur="validateField('confirmPassword', confirmPassword, [required('Confirm Password'), matchesField('Confirm Password', () => password)])"
        />
        <small v-if="errors.confirmPassword" class="field-error">{{ errors.confirmPassword }}</small>
      </div>
      <div class="form-row full-width">
        <div style="display: flex; flex-direction: column; gap: 0.75rem; margin-top: 0.25rem">
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <Checkbox v-model="mustChangePassword" :binary="true" inputId="chk-change-pwd" />
            <label for="chk-change-pwd" style="font-weight: 400">User must change password at next logon</label>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <Checkbox v-model="accountDisabled" :binary="true" inputId="chk-disabled" />
            <label for="chk-disabled" style="font-weight: 400">Account is disabled</label>
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
      <Panel header="Address" :toggleable="true" :collapsed="true">
        <div class="form-grid">
          <div class="form-row full-width">
            <label>Street Address</label>
            <Textarea v-model="streetAddress" class="form-input" rows="2" autoResize />
          </div>
          <div class="form-row">
            <label>City</label>
            <InputText v-model="city" class="form-input" />
          </div>
          <div class="form-row">
            <label>State/Province</label>
            <InputText v-model="state" class="form-input" />
          </div>
          <div class="form-row">
            <label>Postal Code</label>
            <InputText v-model="postalCode" class="form-input" />
          </div>
          <div class="form-row">
            <label>Country</label>
            <InputText v-model="country" class="form-input" />
          </div>
        </div>
      </Panel>

      <Panel header="Phone Numbers" :toggleable="true" :collapsed="true">
        <div class="form-grid">
          <div class="form-row">
            <label>Office Phone</label>
            <InputText v-model="telephoneNumber" class="form-input" />
          </div>
          <div class="form-row">
            <label>Home Phone</label>
            <InputText v-model="homePhone" class="form-input" />
          </div>
          <div class="form-row">
            <label>Mobile</label>
            <InputText v-model="mobile" class="form-input" />
          </div>
        </div>
      </Panel>

      <Panel header="Organization" :toggleable="true" :collapsed="true">
        <div class="form-grid">
          <div class="form-row">
            <label>Title</label>
            <InputText v-model="title" class="form-input" />
          </div>
          <div class="form-row">
            <label>Department</label>
            <InputText v-model="department" class="form-input" />
          </div>
          <div class="form-row">
            <label>Company</label>
            <InputText v-model="company" class="form-input" />
          </div>
          <div class="form-row">
            <label>Office</label>
            <InputText v-model="physicalDeliveryOfficeName" class="form-input" />
          </div>
          <div class="form-row full-width">
            <label>Manager</label>
            <DnPicker v-model="manager" label="Manager" objectFilter="(objectClass=user)" />
          </div>
        </div>
      </Panel>
    </div>

    <template #footer>
      <Button label="Cancel" severity="secondary" text @click="close" />
      <Button label="Create User" icon="pi pi-user-plus" @click="onSubmit" :loading="saving" :disabled="!canSave" />
    </template>
  </Dialog>
</template>

<style scoped>
.form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}

.form-row {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.form-row.full-width {
  grid-column: 1 / -1;
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
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  margin-top: 0.5rem;
}

.field-error {
  color: var(--app-danger-text);
  font-size: 0.75rem;
  margin-top: 0.125rem;
}

.password-field {
  display: flex;
  flex-direction: column;
}

.password-field :deep(.p-password) {
  width: 100%;
}

.password-field :deep(input) {
  width: 100%;
}
</style>
