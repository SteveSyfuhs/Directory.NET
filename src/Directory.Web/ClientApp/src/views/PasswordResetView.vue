<script setup lang="ts">
import { ref, computed } from 'vue'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Message from 'primevue/message'
import Stepper from 'primevue/stepper'
import StepList from 'primevue/steplist'
import StepPanels from 'primevue/steppanels'
import Step from 'primevue/step'
import StepPanel from 'primevue/steppanel'
import Select from 'primevue/select'
import { useToast } from 'primevue/usetoast'
import type { SsprInitiateResult, SecurityQuestionAnswerInput } from '../types/sspr'
import {
  initiateReset,
  verifySecurityQuestions,
  verifyMfa,
  completeReset,
  getSecurityQuestions,
  registerForSspr,
} from '../api/sspr'

const toast = useToast()

// ── Mode: 'reset' or 'register' ──
const mode = ref<'reset' | 'register'>('reset')

// ── Reset flow state ──
const step = ref(1)
const username = ref('')
const initiating = ref(false)
const resetResult = ref<SsprInitiateResult | null>(null)
const errorMessage = ref('')
const successMessage = ref('')

// Security questions answers
const questionAnswers = ref<Record<string, string>>({})
const verifyingQuestions = ref(false)

// MFA
const mfaCode = ref('')
const verifyingMfa = ref(false)

// New password
const newPassword = ref('')
const confirmPassword = ref('')
const resetting = ref(false)

// ── Registration state ──
const regDn = ref('')
const regRecoveryEmail = ref('')
const regRecoveryPhone = ref('')
const regAnswers = ref<{ question: string; answer: string }[]>([])
const availableQuestions = ref<string[]>([])
const registering = ref(false)
const regSuccess = ref(false)

const passwordsMatch = computed(() => newPassword.value === confirmPassword.value)
const passwordStrong = computed(() => {
  const p = newPassword.value
  if (p.length < 7) return false
  let cats = 0
  if (/[A-Z]/.test(p)) cats++
  if (/[a-z]/.test(p)) cats++
  if (/[0-9]/.test(p)) cats++
  if (/[^A-Za-z0-9]/.test(p)) cats++
  return cats >= 3
})
const passwordStrengthLabel = computed(() => {
  const p = newPassword.value
  if (!p) return ''
  if (p.length < 7) return 'Too short'
  let cats = 0
  if (/[A-Z]/.test(p)) cats++
  if (/[a-z]/.test(p)) cats++
  if (/[0-9]/.test(p)) cats++
  if (/[^A-Za-z0-9]/.test(p)) cats++
  if (cats < 3) return 'Weak'
  if (p.length >= 12 && cats >= 4) return 'Strong'
  return 'Good'
})
const passwordStrengthColor = computed(() => {
  switch (passwordStrengthLabel.value) {
    case 'Too short':
    case 'Weak':
      return 'var(--app-danger-text)'
    case 'Good':
      return 'var(--app-warn-text)'
    case 'Strong':
      return 'var(--app-success-text)'
    default:
      return 'var(--p-text-muted-color)'
  }
})

async function startReset() {
  errorMessage.value = ''
  if (!username.value.trim()) return

  initiating.value = true
  try {
    resetResult.value = await initiateReset(username.value.trim())

    if (resetResult.value.requireSecurityQuestions) {
      // Initialize answer map
      questionAnswers.value = {}
      for (const q of resetResult.value.securityQuestions) {
        questionAnswers.value[q] = ''
      }
      step.value = 2
    } else if (resetResult.value.requireMfa) {
      step.value = 3
    } else {
      step.value = 4
    }
  } catch (e: any) {
    errorMessage.value = e.message
  } finally {
    initiating.value = false
  }
}

async function submitSecurityAnswers() {
  if (!resetResult.value) return
  verifyingQuestions.value = true
  errorMessage.value = ''

  const answers: SecurityQuestionAnswerInput[] = Object.entries(questionAnswers.value).map(
    ([question, answer]) => ({ question, answer })
  )

  try {
    const result = await verifySecurityQuestions(resetResult.value.token, answers)
    if (!result.success) {
      errorMessage.value = result.message
      return
    }
    if (result.requireMfa) {
      step.value = 3
    } else {
      step.value = 4
    }
  } catch (e: any) {
    errorMessage.value = e.message
  } finally {
    verifyingQuestions.value = false
  }
}

async function submitMfa() {
  if (!resetResult.value) return
  verifyingMfa.value = true
  errorMessage.value = ''

  try {
    const result = await verifyMfa(resetResult.value.token, mfaCode.value.trim())
    if (!result.success) {
      errorMessage.value = result.message
      return
    }
    step.value = 4
  } catch (e: any) {
    errorMessage.value = e.message
  } finally {
    verifyingMfa.value = false
  }
}

async function submitNewPassword() {
  if (!resetResult.value) return
  if (!passwordsMatch.value) {
    errorMessage.value = 'Passwords do not match.'
    return
  }
  if (!passwordStrong.value) {
    errorMessage.value = 'Password does not meet complexity requirements.'
    return
  }

  resetting.value = true
  errorMessage.value = ''

  try {
    const result = await completeReset(resetResult.value.token, newPassword.value)
    if (result.success) {
      successMessage.value = result.message
      step.value = 5
    } else {
      errorMessage.value = result.message
    }
  } catch (e: any) {
    errorMessage.value = e.message
  } finally {
    resetting.value = false
  }
}

function resetFlow() {
  step.value = 1
  username.value = ''
  resetResult.value = null
  errorMessage.value = ''
  successMessage.value = ''
  questionAnswers.value = {}
  mfaCode.value = ''
  newPassword.value = ''
  confirmPassword.value = ''
}

// ── Registration ──
async function loadQuestions() {
  try {
    availableQuestions.value = await getSecurityQuestions()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function switchToRegister() {
  mode.value = 'register'
  loadQuestions()
  regAnswers.value = [{ question: '', answer: '' }]
  regSuccess.value = false
  errorMessage.value = ''
}

function switchToReset() {
  mode.value = 'reset'
  errorMessage.value = ''
  resetFlow()
}

function addRegAnswer() {
  regAnswers.value.push({ question: '', answer: '' })
}

function removeRegAnswer(index: number) {
  regAnswers.value.splice(index, 1)
}

const usedQuestions = computed(() => regAnswers.value.map(a => a.question).filter(Boolean))

function availableQuestionsFor(index: number) {
  const current = regAnswers.value[index].question
  return availableQuestions.value.filter(
    q => q === current || !usedQuestions.value.includes(q)
  )
}

async function submitRegistration() {
  errorMessage.value = ''
  if (!regDn.value.trim()) {
    errorMessage.value = 'Please enter your username or DN.'
    return
  }

  const answers = regAnswers.value
    .filter(a => a.question && a.answer.trim())
    .map(a => ({ question: a.question, answer: a.answer.trim() }))

  registering.value = true
  try {
    await registerForSspr(
      regDn.value.trim(),
      answers,
      regRecoveryEmail.value.trim() || undefined,
      regRecoveryPhone.value.trim() || undefined,
    )
    regSuccess.value = true
    toast.add({ severity: 'success', summary: 'Registered', detail: 'You are now registered for self-service password reset.', life: 5000 })
  } catch (e: any) {
    errorMessage.value = e.message
  } finally {
    registering.value = false
  }
}
</script>

<template>
  <div class="sspr-page">
    <div class="sspr-container">
      <div class="sspr-header">
        <i class="pi pi-lock" style="font-size: 2rem; color: var(--app-accent-color)"></i>
        <h1>Password Self-Service</h1>
      </div>

      <!-- Mode Tabs -->
      <div class="sspr-tabs">
        <button
          :class="['sspr-tab', { active: mode === 'reset' }]"
          @click="switchToReset"
        >
          Reset Password
        </button>
        <button
          :class="['sspr-tab', { active: mode === 'register' }]"
          @click="switchToRegister"
        >
          Register for SSPR
        </button>
      </div>

      <!-- Error Message -->
      <Message v-if="errorMessage" severity="error" :closable="true" @close="errorMessage = ''">
        {{ errorMessage }}
      </Message>

      <!-- ═══════════ RESET FLOW ═══════════ -->
      <div v-if="mode === 'reset'">
        <!-- Step 1: Enter Username -->
        <div v-if="step === 1" class="sspr-step">
          <p class="step-description">Enter your username or email address to reset your password.</p>
          <div class="form-field">
            <label>Username or Email</label>
            <InputText
              v-model="username"
              placeholder="e.g. jdoe or jdoe@example.com"
              class="w-full"
              @keyup.enter="startReset"
            />
          </div>
          <Button
            label="Reset Password"
            icon="pi pi-arrow-right"
            iconPos="right"
            :loading="initiating"
            @click="startReset"
            :disabled="!username.trim()"
            class="w-full"
          />
        </div>

        <!-- Step 2: Security Questions -->
        <div v-if="step === 2 && resetResult" class="sspr-step">
          <p class="step-description">Answer your security questions to verify your identity.</p>
          <div
            v-for="q in resetResult.securityQuestions"
            :key="q"
            class="form-field"
          >
            <label>{{ q }}</label>
            <InputText
              v-model="questionAnswers[q]"
              class="w-full"
              placeholder="Your answer"
            />
          </div>
          <Button
            label="Verify Answers"
            icon="pi pi-check"
            :loading="verifyingQuestions"
            @click="submitSecurityAnswers"
            class="w-full"
          />
        </div>

        <!-- Step 3: MFA Verification -->
        <div v-if="step === 3 && resetResult" class="sspr-step">
          <p class="step-description">Enter the verification code from your authenticator app.</p>
          <div class="form-field">
            <label>MFA Code</label>
            <InputText
              v-model="mfaCode"
              placeholder="6-digit code"
              class="w-full"
              maxlength="6"
              @keyup.enter="submitMfa"
            />
          </div>
          <Button
            label="Verify Code"
            icon="pi pi-check"
            :loading="verifyingMfa"
            @click="submitMfa"
            :disabled="mfaCode.trim().length < 6"
            class="w-full"
          />
        </div>

        <!-- Step 4: New Password -->
        <div v-if="step === 4" class="sspr-step">
          <p class="step-description">Choose a new password. It must be at least 7 characters with 3 of: uppercase, lowercase, digit, special character.</p>
          <div class="form-field">
            <label>New Password</label>
            <Password
              v-model="newPassword"
              toggleMask
              :feedback="false"
              class="w-full"
              inputClass="w-full"
            />
            <small v-if="newPassword" :style="{ color: passwordStrengthColor, fontWeight: 600 }">
              {{ passwordStrengthLabel }}
            </small>
          </div>
          <div class="form-field">
            <label>Confirm Password</label>
            <Password
              v-model="confirmPassword"
              toggleMask
              :feedback="false"
              class="w-full"
              inputClass="w-full"
            />
            <small v-if="confirmPassword && !passwordsMatch" style="color: var(--app-danger-text)">
              Passwords do not match.
            </small>
          </div>
          <Button
            label="Set New Password"
            icon="pi pi-lock"
            :loading="resetting"
            @click="submitNewPassword"
            :disabled="!passwordStrong || !passwordsMatch || !newPassword"
            class="w-full"
          />
        </div>

        <!-- Step 5: Success -->
        <div v-if="step === 5" class="sspr-step sspr-success">
          <i class="pi pi-check-circle" style="font-size: 3rem; color: var(--app-success-text)"></i>
          <h2>Password Reset Successfully</h2>
          <p>{{ successMessage }}</p>
          <p class="step-description">You can now log in with your new password.</p>
          <Button
            label="Start Over"
            icon="pi pi-refresh"
            severity="secondary"
            @click="resetFlow"
          />
        </div>
      </div>

      <!-- ═══════════ REGISTRATION FLOW ═══════════ -->
      <div v-if="mode === 'register'">
        <div v-if="regSuccess" class="sspr-step sspr-success">
          <i class="pi pi-check-circle" style="font-size: 3rem; color: var(--app-success-text)"></i>
          <h2>Registration Complete</h2>
          <p>You are now registered for self-service password reset.</p>
          <Button
            label="Go to Password Reset"
            icon="pi pi-arrow-right"
            iconPos="right"
            @click="switchToReset"
          />
        </div>

        <div v-else class="sspr-step">
          <p class="step-description">Register your recovery methods so you can reset your password in the future.</p>

          <div class="form-field">
            <label>Username or DN</label>
            <InputText
              v-model="regDn"
              placeholder="e.g. jdoe or CN=John Doe,OU=Users,DC=example,DC=com"
              class="w-full"
            />
          </div>

          <div class="form-field">
            <label>Recovery Email (optional)</label>
            <InputText
              v-model="regRecoveryEmail"
              placeholder="your.email@example.com"
              class="w-full"
              type="email"
            />
          </div>

          <div class="form-field">
            <label>Recovery Phone (optional)</label>
            <InputText
              v-model="regRecoveryPhone"
              placeholder="+1 555-0100"
              class="w-full"
            />
          </div>

          <div class="form-section-title">Security Questions</div>

          <div
            v-for="(qa, i) in regAnswers"
            :key="i"
            class="question-entry"
          >
            <div class="form-field" style="flex: 1">
              <Select
                v-model="qa.question"
                :options="availableQuestionsFor(i)"
                placeholder="Select a question"
                class="w-full"
              />
            </div>
            <div class="form-field" style="flex: 1">
              <InputText
                v-model="qa.answer"
                placeholder="Your answer"
                class="w-full"
                :disabled="!qa.question"
              />
            </div>
            <Button
              icon="pi pi-trash"
              text
              rounded
              severity="danger"
              size="small"
              @click="removeRegAnswer(i)"
              :disabled="regAnswers.length <= 1"
              v-tooltip="'Remove'"
            />
          </div>

          <Button
            icon="pi pi-plus"
            label="Add Question"
            text
            size="small"
            @click="addRegAnswer"
            :disabled="regAnswers.length >= availableQuestions.length"
            style="margin-bottom: 1rem"
          />

          <Button
            label="Register"
            icon="pi pi-check"
            :loading="registering"
            @click="submitRegistration"
            :disabled="!regDn.trim()"
            class="w-full"
          />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.sspr-page {
  display: flex;
  justify-content: center;
  align-items: flex-start;
  min-height: 100%;
  padding: 2rem 1rem;
}

.sspr-container {
  width: 100%;
  max-width: 520px;
  background: var(--p-surface-card);
  border: 1px solid var(--p-surface-border);
  border-radius: 0.75rem;
  padding: 2rem;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
}

.sspr-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 1.5rem;
}

.sspr-header h1 {
  font-size: 1.375rem;
  font-weight: 700;
  color: var(--p-text-color);
  margin: 0;
}

.sspr-tabs {
  display: flex;
  gap: 0;
  border: 1px solid var(--p-surface-border);
  border-radius: 0.5rem;
  overflow: hidden;
  margin-bottom: 1.5rem;
}

.sspr-tab {
  flex: 1;
  padding: 0.625rem 1rem;
  border: none;
  background: var(--p-surface-ground);
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s ease;
}

.sspr-tab:not(:last-child) {
  border-right: 1px solid var(--p-surface-border);
}

.sspr-tab.active {
  background: var(--app-accent-color);
  color: #fff;
}

.sspr-tab:hover:not(.active) {
  background: var(--app-neutral-bg);
  color: var(--p-text-color);
}

.sspr-step {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.sspr-success {
  align-items: center;
  text-align: center;
  padding: 1.5rem 0;
}

.sspr-success h2 {
  font-size: 1.25rem;
  font-weight: 700;
  color: var(--p-text-color);
  margin: 0;
}

.sspr-success p {
  color: var(--p-text-muted-color);
  margin: 0;
}

.step-description {
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
  margin: 0;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.form-field label {
  font-weight: 600;
  font-size: 0.875rem;
  color: var(--p-text-color);
}

.form-section-title {
  font-weight: 700;
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--p-text-muted-color);
  margin-top: 0.5rem;
}

.question-entry {
  display: flex;
  gap: 0.5rem;
  align-items: flex-start;
  margin-bottom: 0.5rem;
}

.w-full {
  width: 100%;
}
</style>
