<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Checkbox from 'primevue/checkbox'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import { resetPassword } from '../api/users'

const props = defineProps<{
  visible: boolean
  userGuid: string
}>()

const emit = defineEmits<{
  'update:visible': [val: boolean]
  reset: []
}>()

const toast = useToast()
const saving = ref(false)
const password = ref('')
const confirmPassword = ref('')
const mustChangeAtNextLogon = ref(true)

const canSave = computed(() =>
  password.value.length >= 1 &&
  password.value === confirmPassword.value
)

const passwordMismatch = computed(() =>
  confirmPassword.value.length > 0 && password.value !== confirmPassword.value
)

watch(() => props.visible, (v) => {
  if (v) {
    password.value = ''
    confirmPassword.value = ''
    mustChangeAtNextLogon.value = true
  }
})

async function onSubmit() {
  if (!canSave.value || !props.userGuid) return
  saving.value = true
  try {
    await resetPassword(props.userGuid, password.value, mustChangeAtNextLogon.value)
    toast.add({ severity: 'success', summary: 'Password Reset', detail: 'Password has been reset successfully', life: 3000 })
    emit('reset')
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
  <Dialog :visible="visible" @update:visible="close" header="Reset Password" modal :style="{ width: '400px' }">
    <div class="form-stack">
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">Enter a new password for this account. The password must meet the domain's password policy requirements.</p>
      <div class="form-row">
        <label>New Password</label>
        <InputText v-model="password" type="password" class="form-input" />
      </div>
      <div class="form-row">
        <label>Confirm Password</label>
        <InputText v-model="confirmPassword" type="password" class="form-input"
                   :class="{ 'p-invalid': passwordMismatch }" />
        <small v-if="passwordMismatch" style="color: var(--app-danger-text)">Passwords do not match</small>
      </div>
      <div style="display: flex; align-items: center; gap: 0.5rem; margin-top: 0.25rem">
        <Checkbox v-model="mustChangeAtNextLogon" :binary="true" inputId="chk-must-change" />
        <label for="chk-must-change" style="font-weight: 400; font-size: 0.875rem">User must change password at next logon</label>
      </div>
    </div>

    <template #footer>
      <Button label="Cancel" severity="secondary" text @click="close" />
      <Button label="Reset Password" icon="pi pi-key" @click="onSubmit" :loading="saving" :disabled="!canSave" />
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
</style>
