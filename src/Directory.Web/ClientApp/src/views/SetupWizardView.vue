<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import ProgressBar from 'primevue/progressbar'
import ProgressSpinner from 'primevue/progressspinner'
import Message from 'primevue/message'
import { useSetupStore } from '../stores/setup'
import {
  validateConnection,
  configureDatabase,
  validateDomain,
  validatePassword as apiValidatePassword,
  startProvisioning,
  validateSourceDc,
  startReplicaProvisioning,
  fetchReplicationProgress,
  discoverDomain,
  type ValidateConnectionResult,
  type ValidateDomainResult,
  type ValidateSourceDcResult,
  type DiscoverDomainResult,
  type ReplicationProgress,
} from '../api/setup'

const router = useRouter()
const setupStore = useSetupStore()

// ─── Mode & Step management ───
type SetupMode = 'new' | 'windows' | 'modern' | null
const setupMode = ref<SetupMode>(null)
const currentStep = ref(0)

const newSteps = ['Welcome', 'Database', 'Domain', 'Administrator', 'Review', 'Provisioning']
const windowsSteps = ['Welcome', 'Database', 'Domain & Credentials', 'Local Config', 'Review', 'Provisioning']
const modernSteps = ['Welcome', 'Database', 'Source DC', 'Local Config', 'Review', 'Provisioning']

const steps = computed(() => {
  if (setupMode.value === 'windows') return windowsSteps
  if (setupMode.value === 'modern') return modernSteps
  return newSteps
})

const lastContentStep = computed(() => steps.value.length - 2) // Review step index
const provisioningStepIndex = computed(() => steps.value.length - 1)

function selectMode(mode: 'new' | 'windows' | 'modern') {
  setupMode.value = mode
  currentStep.value = 1
}

// ─── Step 2: Database ───
const connectionString = ref('')
const databaseName = ref('DirectoryService')
const connectionTested = ref(false)
const connectionTesting = ref(false)
const connectionResult = ref<ValidateConnectionResult | null>(null)
const databaseConfiguring = ref(false)
const databaseConfigured = ref(setupStore.isDatabaseConfigured)
const databaseConfigureError = ref('')

async function testConnection() {
  connectionTesting.value = true
  connectionResult.value = null
  databaseConfigureError.value = ''
  try {
    connectionResult.value = await validateConnection(connectionString.value, databaseName.value)
    connectionTested.value = connectionResult.value.success
  } catch {
    connectionResult.value = { success: false, error: 'Failed to reach the server. Please check your connection.' }
    connectionTested.value = false
  } finally {
    connectionTesting.value = false
  }
}

async function configureDatabaseConnection() {
  databaseConfiguring.value = true
  databaseConfigureError.value = ''
  try {
    const result = await configureDatabase(connectionString.value, databaseName.value)
    if (result.success) {
      databaseConfigured.value = true
      setupStore.markDatabaseConfigured()
    } else {
      databaseConfigureError.value = result.error ?? 'Failed to configure database.'
    }
  } catch {
    databaseConfigureError.value = 'Failed to reach the server.'
  } finally {
    databaseConfiguring.value = false
  }
}

// Reset validation when inputs change
watch([connectionString, databaseName], () => {
  connectionTested.value = false
  connectionResult.value = null
  databaseConfigured.value = false
  databaseConfigureError.value = ''
})

const canProceedFromDatabase = computed(() => databaseConfigured.value)

// ─── Step 3 (new mode): Domain ───
const domainName = ref('')
const netBiosName = ref('')
const siteName = ref('Default-First-Site-Name')
const domainDn = ref('')
const domainValidating = ref(false)
const domainResult = ref<ValidateDomainResult | null>(null)

async function onDomainBlur() {
  if (!domainName.value.trim() || !domainName.value.includes('.')) {
    domainResult.value = null
    domainDn.value = ''
    return
  }
  domainValidating.value = true
  try {
    const result = await validateDomain(domainName.value)
    domainResult.value = result
    if (result.valid) {
      domainDn.value = result.domainDn ?? ''
      if (result.suggestedNetBios && !netBiosName.value) {
        netBiosName.value = result.suggestedNetBios
      }
    }
  } catch {
    domainResult.value = { valid: false, error: 'Failed to validate domain name.' }
  } finally {
    domainValidating.value = false
  }
}

watch(domainName, () => {
  domainResult.value = null
  domainDn.value = ''
})

const canProceedFromDomain = computed(() => {
  return domainName.value.trim().length > 0
    && domainName.value.includes('.')
    && netBiosName.value.trim().length > 0
    && siteName.value.trim().length > 0
    && domainResult.value?.valid === true
})

// ─── Step 4 (new mode): Administrator ───
const adminUsername = ref('Administrator')
const adminPassword = ref('')
const adminPasswordConfirm = ref('')
const passwordValidating = ref(false)
const passwordValidResult = ref<{ valid: boolean; reason?: string } | null>(null)
const showConnectionString = ref(false)

async function onPasswordBlur() {
  if (!adminPassword.value) {
    passwordValidResult.value = null
    return
  }
  passwordValidating.value = true
  try {
    passwordValidResult.value = await apiValidatePassword(adminPassword.value)
  } catch {
    passwordValidResult.value = null
  } finally {
    passwordValidating.value = false
  }
}

watch(adminPassword, () => {
  passwordValidResult.value = null
})

const passwordsMatch = computed(() => {
  if (!adminPasswordConfirm.value) return null
  return adminPassword.value === adminPasswordConfirm.value
})

const passwordLength = computed(() => adminPassword.value.length)
const hasUpper = computed(() => /[A-Z]/.test(adminPassword.value))
const hasLower = computed(() => /[a-z]/.test(adminPassword.value))
const hasDigit = computed(() => /[0-9]/.test(adminPassword.value))
const hasSymbol = computed(() => /[^A-Za-z0-9]/.test(adminPassword.value))
const charTypesCount = computed(() => [hasUpper.value, hasLower.value, hasDigit.value, hasSymbol.value].filter(Boolean).length)

const passwordStrength = computed(() => {
  if (passwordLength.value === 0) return 0
  if (passwordLength.value < 7) return 1
  if (charTypesCount.value < 3) return 2
  if (passwordLength.value >= 12 && charTypesCount.value >= 3) return 4
  return 3
})

const passwordStrengthLabel = computed(() => {
  const labels = ['', 'Weak', 'Fair', 'Good', 'Strong']
  return labels[passwordStrength.value]
})

const passwordStrengthColor = computed(() => {
  const colors = ['', 'var(--app-danger-text)', 'var(--app-warn-text)', 'var(--app-success-text)', 'var(--app-success-text-strong)']
  return colors[passwordStrength.value]
})

const adminUpn = computed(() => {
  if (adminUsername.value && domainName.value) {
    return `${adminUsername.value}@${domainName.value}`
  }
  return ''
})

const canProceedFromAdmin = computed(() => {
  return adminUsername.value.trim().length > 0
    && adminPassword.value.length >= 7
    && charTypesCount.value >= 3
    && passwordsMatch.value === true
})

// ─── Modern mode: Source DC ───
const sourceDcUrl = ref('')
const sourceDcTesting = ref(false)
const sourceDcResult = ref<ValidateSourceDcResult | null>(null)
const replicaAdminUpn = ref('')
const replicaAdminPassword = ref('')

async function testSourceDc() {
  sourceDcTesting.value = true
  sourceDcResult.value = null
  try {
    sourceDcResult.value = await validateSourceDc(sourceDcUrl.value)
  } catch {
    sourceDcResult.value = { success: false, error: 'Failed to reach the source domain controller.' }
  } finally {
    sourceDcTesting.value = false
  }
}

watch(sourceDcUrl, () => {
  sourceDcResult.value = null
})

const canProceedFromSourceDc = computed(() => {
  return sourceDcResult.value?.success === true
    && replicaAdminUpn.value.trim().length > 0
    && replicaAdminPassword.value.length > 0
})

const functionalLevelLabel = computed(() => {
  const levels: Record<number, string> = {
    0: 'Windows 2000',
    1: 'Windows Server 2003 Interim',
    2: 'Windows Server 2003',
    3: 'Windows Server 2008',
    4: 'Windows Server 2008 R2',
    5: 'Windows Server 2012',
    6: 'Windows Server 2012 R2',
    7: 'Windows Server 2016',
  }
  const fl = sourceDcResult.value?.functionalLevel
  if (fl === undefined || fl === null) return 'Unknown'
  return levels[fl] ?? `Level ${fl}`
})

// ─── Windows mode: Domain Discovery ───
const windowsDomainName = ref('')
const windowsDomainDiscovering = ref(false)
const windowsDomainResult = ref<DiscoverDomainResult | null>(null)
const windowsAdminUpn = ref('')
const windowsAdminPassword = ref('')

async function discoverWindowsDomain() {
  windowsDomainDiscovering.value = true
  windowsDomainResult.value = null
  try {
    windowsDomainResult.value = await discoverDomain(windowsDomainName.value)
    if (windowsDomainResult.value.success && !windowsAdminUpn.value) {
      windowsAdminUpn.value = `administrator@${windowsDomainName.value}`
    }
  } catch {
    windowsDomainResult.value = { success: false, error: 'Failed to discover domain. Check the domain name and network connectivity.' }
  } finally {
    windowsDomainDiscovering.value = false
  }
}

watch(windowsDomainName, () => {
  windowsDomainResult.value = null
})

const windowsFunctionalLevelLabel = computed(() => {
  const levels: Record<number, string> = {
    0: 'Windows 2000',
    1: 'Windows Server 2003 Interim',
    2: 'Windows Server 2003',
    3: 'Windows Server 2008',
    4: 'Windows Server 2008 R2',
    5: 'Windows Server 2012',
    6: 'Windows Server 2012 R2',
    7: 'Windows Server 2016',
  }
  const fl = windowsDomainResult.value?.functionalLevel
  if (fl === undefined || fl === null) return 'Unknown'
  return levels[fl] ?? `Level ${fl}`
})

const canProceedFromWindowsDomain = computed(() => {
  return windowsDomainResult.value?.success === true
    && windowsAdminUpn.value.trim().length > 0
    && windowsAdminPassword.value.length > 0
})

// ─── Shared replica/windows: Local Config ───
const replicaSiteName = ref('Default-First-Site-Name')
const replicaHostname = ref('')

const canProceedFromLocalConfig = computed(() => {
  return replicaSiteName.value.trim().length > 0
})

// ─── Provisioning ───
const provisioningStarted = ref(false)
const provisioningError = ref('')
let pollInterval: ReturnType<typeof setInterval> | null = null

// Replica replication progress
const replicationProgress = ref<ReplicationProgress | null>(null)
const replicaNcStatuses = ref<{
  schema: { done: boolean; objects: number; bytes: number }
  configuration: { done: boolean; objects: number; bytes: number }
  domain: { done: boolean; objects: number; bytes: number }
}>({
  schema: { done: false, objects: 0, bytes: 0 },
  configuration: { done: false, objects: 0, bytes: 0 },
  domain: { done: false, objects: 0, bytes: 0 },
})

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`
}

async function beginProvisioning() {
  provisioningError.value = ''
  try {
    if (setupMode.value === 'windows') {
      const result = await startReplicaProvisioning({
        domainName: windowsDomainName.value,
        adminUpn: windowsAdminUpn.value,
        adminPassword: windowsAdminPassword.value,
        siteName: replicaSiteName.value,
        hostname: replicaHostname.value || undefined,
        transport: 'rpc',
      })
      if (result.started) {
        provisioningStarted.value = true
        startPolling()
      } else {
        provisioningError.value = result.error ?? 'Failed to start replica provisioning.'
      }
    } else if (setupMode.value === 'modern') {
      const result = await startReplicaProvisioning({
        sourceDcUrl: sourceDcUrl.value,
        adminUpn: replicaAdminUpn.value,
        adminPassword: replicaAdminPassword.value,
        siteName: replicaSiteName.value,
        hostname: replicaHostname.value || undefined,
        transport: 'http',
      })
      if (result.started) {
        provisioningStarted.value = true
        startPolling()
      } else {
        provisioningError.value = result.error ?? 'Failed to start replica provisioning.'
      }
    } else {
      const result = await startProvisioning({
        domainName: domainName.value,
        netBiosName: netBiosName.value,
        adminPassword: adminPassword.value,
        adminUsername: adminUsername.value,
        siteName: siteName.value,
        cosmosConnectionString: connectionString.value || undefined,
        cosmosDatabaseName: databaseName.value || undefined,
      })
      if (result.started) {
        provisioningStarted.value = true
        startPolling()
      } else {
        provisioningError.value = result.error ?? 'Failed to start provisioning.'
      }
    }
  } catch {
    provisioningError.value = 'Failed to reach the server.'
  }
}

function startPolling() {
  if (pollInterval) return
  pollInterval = setInterval(async () => {
    if (setupMode.value === 'windows' || setupMode.value === 'modern') {
      try {
        const progress = await fetchReplicationProgress()
        // Update store status fields
        if (setupStore.status) {
          setupStore.status = {
            ...setupStore.status,
            isProvisioned: progress.isProvisioned,
            isProvisioning: progress.isProvisioning,
            provisioningProgress: progress.provisioningProgress,
            provisioningPhase: progress.provisioningPhase,
            provisioningError: progress.provisioningError,
          }
        }
        if (progress.replicationProgress) {
          replicationProgress.value = progress.replicationProgress
          updateNcStatuses(progress.replicationProgress)
        }
        if (progress.isProvisioned || progress.provisioningError) {
          stopPolling()
        }
      } catch {
        // Ignore polling errors
      }
    } else {
      await setupStore.pollProgress()
      if (setupStore.status?.isProvisioned || setupStore.status?.provisioningError) {
        stopPolling()
      }
    }
  }, 500)
}

function updateNcStatuses(rp: ReplicationProgress) {
  const nc = rp.namingContext.toLowerCase()
  const entry = { done: false, objects: rp.objectsProcessed, bytes: rp.bytesTransferred }

  if (nc.includes('schema')) {
    replicaNcStatuses.value.schema = entry
  } else if (nc.includes('configuration')) {
    // Schema is done if we moved past it
    replicaNcStatuses.value.schema = { ...replicaNcStatuses.value.schema, done: true }
    replicaNcStatuses.value.configuration = entry
  } else {
    // Both schema and config are done
    replicaNcStatuses.value.schema = { ...replicaNcStatuses.value.schema, done: true }
    replicaNcStatuses.value.configuration = { ...replicaNcStatuses.value.configuration, done: true }
    replicaNcStatuses.value.domain = entry
  }
}

function stopPolling() {
  if (pollInterval) {
    clearInterval(pollInterval)
    pollInterval = null
  }
}

onUnmounted(() => {
  stopPolling()
})

const provisioningComplete = computed(() => setupStore.status?.isProvisioned === true)
const provisioningPhase = computed(() => setupStore.status?.provisioningPhase ?? 'Initializing...')
const provisioningProgress = computed(() => setupStore.status?.provisioningProgress ?? 0)
const provisioningFailed = computed(() => !!setupStore.status?.provisioningError)

function launchDashboard() {
  // Force re-check which will flip to provisioned state
  setupStore.checkStatus()
}

function retryProvisioning() {
  provisioningError.value = ''
  provisioningStarted.value = false
}

// ─── Navigation ───
function nextStep() {
  if (currentStep.value === lastContentStep.value) {
    // Move to provisioning and start
    currentStep.value = provisioningStepIndex.value
    beginProvisioning()
  } else {
    currentStep.value++
  }
}

function prevStep() {
  if (currentStep.value === 1) {
    // Go back to welcome / mode selection
    currentStep.value = 0
    setupMode.value = null
  } else if (currentStep.value > 0) {
    currentStep.value--
  }
}

const canProceed = computed(() => {
  if (setupMode.value === 'new') {
    switch (currentStep.value) {
      case 0: return true
      case 1: return canProceedFromDatabase.value
      case 2: return canProceedFromDomain.value
      case 3: return canProceedFromAdmin.value
      case 4: return true // Review
      default: return false
    }
  } else if (setupMode.value === 'windows') {
    switch (currentStep.value) {
      case 0: return true
      case 1: return canProceedFromDatabase.value
      case 2: return canProceedFromWindowsDomain.value
      case 3: return canProceedFromLocalConfig.value
      case 4: return true // Review
      default: return false
    }
  } else if (setupMode.value === 'modern') {
    switch (currentStep.value) {
      case 0: return true
      case 1: return canProceedFromDatabase.value
      case 2: return canProceedFromSourceDc.value
      case 3: return canProceedFromLocalConfig.value
      case 4: return true // Review
      default: return false
    }
  }
  return true // Welcome before mode selection
})

// Effective domain name for display (works for all modes)
const effectiveDomainName = computed(() => {
  if (setupMode.value === 'modern') return sourceDcResult.value?.domainName ?? ''
  if (setupMode.value === 'windows') return windowsDomainName.value
  return domainName.value
})
</script>

<template>
  <div class="setup-wizard">
    <!-- Header -->
    <header class="setup-header">
      <div class="setup-header-inner">
        <i class="pi pi-server setup-header-icon"></i>
        <span class="setup-header-title">Active Directory Domain Services Setup</span>
      </div>
    </header>

    <!-- Step Indicator -->
    <div class="step-indicator" v-if="currentStep > 0 && currentStep < provisioningStepIndex">
      <div class="step-indicator-inner">
        <template v-for="(step, index) in steps.slice(1, steps.length - 1)" :key="index">
          <div
            class="step-dot"
            :class="{
              active: index + 1 === currentStep,
              completed: index + 1 < currentStep,
            }"
          >
            <div class="step-circle">
              <i v-if="index + 1 < currentStep" class="pi pi-check"></i>
              <span v-else>{{ index + 1 }}</span>
            </div>
            <span class="step-label">{{ step }}</span>
          </div>
          <div v-if="index < steps.length - 3" class="step-line" :class="{ filled: index + 1 < currentStep }"></div>
        </template>
      </div>
    </div>

    <!-- Content Area -->
    <div class="setup-content">
      <div class="setup-panel">

        <!-- Step 0: Welcome / Mode Selection -->
        <div v-if="currentStep === 0" class="step-panel">
          <div class="welcome-icon">
            <i class="pi pi-server"></i>
          </div>
          <h1 class="step-title welcome-title">Welcome to Active Directory Domain Services</h1>
          <p class="step-subtitle" style="text-align: center;">
            Choose how you want to configure this server.
          </p>

          <div class="mode-selection">
            <div
              class="mode-card mode-card-new"
              :class="{ selected: setupMode === 'new' }"
              @click="selectMode('new')"
              role="button"
              tabindex="0"
              @keydown.enter="selectMode('new')"
            >
              <div class="mode-icon-wrap mode-icon-blue">
                <i class="pi pi-shield"></i>
              </div>
              <h3 class="mode-card-title">Create New Domain</h3>
              <p class="mode-card-desc">Set up a brand new Active Directory domain from scratch</p>
            </div>

            <div
              class="mode-card mode-card-windows"
              :class="{ selected: setupMode === 'windows' }"
              @click="selectMode('windows')"
              role="button"
              tabindex="0"
              @keydown.enter="selectMode('windows')"
            >
              <div class="mode-icon-wrap mode-icon-amber">
                <i class="pi pi-building"></i>
              </div>
              <h3 class="mode-card-title">Join Windows Domain</h3>
              <p class="mode-card-desc">Connect to an existing Windows Active Directory domain via RPC</p>
            </div>

            <div
              class="mode-card mode-card-modern"
              :class="{ selected: setupMode === 'modern' }"
              @click="selectMode('modern')"
              role="button"
              tabindex="0"
              @keydown.enter="selectMode('modern')"
            >
              <div class="mode-icon-wrap mode-icon-green">
                <i class="pi pi-sync"></i>
              </div>
              <h3 class="mode-card-title">Join Modern DC</h3>
              <p class="mode-card-desc">Add this server as a replica to another modern domain controller via HTTP</p>
            </div>
          </div>
        </div>

        <!-- Step 1: Database Connection (shared by all modes) -->
        <div v-if="currentStep === 1" class="step-panel">
          <h2 class="step-title">Database Configuration</h2>
          <p class="step-subtitle">Configure the connection to your Azure Cosmos DB instance.</p>

          <div class="form-group">
            <label class="form-label" for="connStr">Connection String</label>
            <div class="password-toggle-wrap">
              <InputText
                id="connStr"
                v-model="connectionString"
                :type="showConnectionString ? 'text' : 'password'"
                placeholder="AccountEndpoint=https://..."
                class="form-input"
                fluid
              />
              <button
                type="button"
                class="toggle-visibility"
                @click="showConnectionString = !showConnectionString"
                tabindex="-1"
              >
                <i :class="showConnectionString ? 'pi pi-eye-slash' : 'pi pi-eye'"></i>
              </button>
            </div>
            <small class="form-help">Your Azure Cosmos DB connection string.</small>
          </div>

          <div class="form-group">
            <label class="form-label" for="dbName">Database Name</label>
            <InputText id="dbName" v-model="databaseName" placeholder="DirectoryService" class="form-input" fluid />
            <small class="form-help">The database will be created if it does not exist.</small>
          </div>

          <div class="form-group">
            <Button
              label="Test Connection"
              icon="pi pi-bolt"
              :loading="connectionTesting"
              @click="testConnection"
              :disabled="!connectionString.trim()"
              outlined
            />
          </div>

          <Message v-if="connectionResult?.success && !databaseConfigured" severity="success" :closable="false">
            <i class="pi pi-check-circle"></i>&nbsp; Connection verified successfully
          </Message>
          <Message v-if="connectionResult && !connectionResult.success" severity="error" :closable="false">
            <i class="pi pi-times-circle"></i>&nbsp; {{ connectionResult.error || 'Connection failed' }}
          </Message>

          <!-- Configure button appears after successful test -->
          <div v-if="connectionTested && connectionResult?.success && !databaseConfigured" class="form-group">
            <Button
              label="Configure Database"
              icon="pi pi-cog"
              :loading="databaseConfiguring"
              @click="configureDatabaseConnection"
              severity="success"
            />
            <small class="form-help">This will create the database and containers in your Cosmos DB account.</small>
          </div>

          <Message v-if="databaseConfigured" severity="success" :closable="false">
            <i class="pi pi-check-circle"></i>&nbsp; Database configured and initialized successfully
          </Message>
          <Message v-if="databaseConfigureError" severity="error" :closable="false">
            <i class="pi pi-times-circle"></i>&nbsp; {{ databaseConfigureError }}
          </Message>

          <div class="step-actions">
            <Button label="Back" icon="pi pi-arrow-left" severity="secondary" text @click="prevStep" />
            <Button label="Next" icon="pi pi-arrow-right" iconPos="right" @click="nextStep" :disabled="!canProceed" />
          </div>
        </div>

        <!-- ════════════════════════════════════════════ -->
        <!-- NEW DOMAIN MODE: Steps 2-4                  -->
        <!-- ════════════════════════════════════════════ -->

        <!-- Step 2 (new): Domain Configuration -->
        <div v-if="currentStep === 2 && setupMode === 'new'" class="step-panel">
          <h2 class="step-title">Domain Settings</h2>
          <p class="step-subtitle">Configure your Active Directory domain.</p>

          <div class="form-group">
            <label class="form-label" for="domainName">Domain Name</label>
            <InputText
              id="domainName"
              v-model="domainName"
              placeholder="contoso.com"
              class="form-input"
              @blur="onDomainBlur"
              fluid
            />
            <div v-if="domainValidating" class="form-feedback info">
              <ProgressSpinner style="width: 16px; height: 16px;" strokeWidth="4" /> Validating...
            </div>
            <div v-if="domainResult?.valid && domainDn" class="form-feedback success">
              <i class="pi pi-check-circle"></i> DN: {{ domainDn }}
            </div>
            <div v-if="domainResult && !domainResult.valid" class="form-feedback error">
              <i class="pi pi-times-circle"></i> {{ domainResult.error || 'Invalid domain name' }}
            </div>
            <small class="form-help">Enter a fully qualified domain name (e.g., contoso.com).</small>
          </div>

          <div class="form-group">
            <label class="form-label" for="netbios">NetBIOS Name</label>
            <InputText id="netbios" v-model="netBiosName" placeholder="CONTOSO" class="form-input" fluid />
            <small class="form-help">Short name for legacy compatibility. Typically uppercase.</small>
          </div>

          <div class="form-group">
            <label class="form-label" for="site">Site Name</label>
            <InputText id="site" v-model="siteName" placeholder="Default-First-Site-Name" class="form-input" fluid />
            <small class="form-help">Active Directory site name for this domain controller.</small>
          </div>

          <div class="step-actions">
            <Button label="Back" icon="pi pi-arrow-left" severity="secondary" text @click="prevStep" />
            <Button label="Next" icon="pi pi-arrow-right" iconPos="right" @click="nextStep" :disabled="!canProceed" />
          </div>
        </div>

        <!-- Step 3 (new): Administrator Account -->
        <div v-if="currentStep === 3 && setupMode === 'new'" class="step-panel">
          <h2 class="step-title">Administrator Account</h2>
          <p class="step-subtitle">Set up the first domain administrator account.</p>

          <div class="form-group">
            <label class="form-label" for="adminUser">Username</label>
            <InputText id="adminUser" v-model="adminUsername" placeholder="Administrator" class="form-input" fluid />
            <div v-if="adminUpn" class="form-feedback info">
              <i class="pi pi-user"></i> UPN: {{ adminUpn }}
            </div>
          </div>

          <div class="form-group">
            <label class="form-label" for="adminPass">Password</label>
            <Password
              id="adminPass"
              v-model="adminPassword"
              :feedback="false"
              toggleMask
              class="form-input"
              inputClass="form-input-inner"
              @blur="onPasswordBlur"
              fluid
            />
            <!-- Strength Bar -->
            <div v-if="adminPassword" class="password-strength">
              <div class="strength-bars">
                <div
                  v-for="i in 4"
                  :key="i"
                  class="strength-bar"
                  :style="{ background: i <= passwordStrength ? passwordStrengthColor : 'var(--app-neutral-border)' }"
                ></div>
              </div>
              <span class="strength-label" :style="{ color: passwordStrengthColor }">{{ passwordStrengthLabel }}</span>
            </div>
            <!-- Requirements checklist -->
            <div v-if="adminPassword" class="password-requirements">
              <div :class="['req', passwordLength >= 7 ? 'met' : 'unmet']">
                <i :class="passwordLength >= 7 ? 'pi pi-check' : 'pi pi-times'"></i>
                At least 7 characters
              </div>
              <div :class="['req', charTypesCount >= 3 ? 'met' : 'unmet']">
                <i :class="charTypesCount >= 3 ? 'pi pi-check' : 'pi pi-times'"></i>
                3+ character types (upper, lower, digit, symbol)
              </div>
            </div>
            <div v-if="passwordValidResult && !passwordValidResult.valid" class="form-feedback error">
              <i class="pi pi-times-circle"></i> {{ passwordValidResult.reason }}
            </div>
          </div>

          <div class="form-group">
            <label class="form-label" for="adminPassConfirm">Confirm Password</label>
            <Password
              id="adminPassConfirm"
              v-model="adminPasswordConfirm"
              :feedback="false"
              toggleMask
              class="form-input"
              inputClass="form-input-inner"
              fluid
            />
            <div v-if="passwordsMatch === true" class="form-feedback success">
              <i class="pi pi-check-circle"></i> Passwords match
            </div>
            <div v-if="passwordsMatch === false" class="form-feedback error">
              <i class="pi pi-times-circle"></i> Passwords do not match
            </div>
          </div>

          <div class="step-actions">
            <Button label="Back" icon="pi pi-arrow-left" severity="secondary" text @click="prevStep" />
            <Button label="Next" icon="pi pi-arrow-right" iconPos="right" @click="nextStep" :disabled="!canProceed" />
          </div>
        </div>

        <!-- Step 4 (new): Review -->
        <div v-if="currentStep === 4 && setupMode === 'new'" class="step-panel">
          <h2 class="step-title">Review Configuration</h2>
          <p class="step-subtitle">Please review your settings before provisioning.</p>

          <div class="review-card">
            <div class="review-section">
              <h3 class="review-heading">Domain</h3>
              <div class="review-grid">
                <div class="review-label">Domain Name</div>
                <div class="review-value">{{ domainName }}</div>
                <div class="review-label">NetBIOS Name</div>
                <div class="review-value">{{ netBiosName }}</div>
                <div class="review-label">Domain DN</div>
                <div class="review-value">{{ domainDn || 'N/A' }}</div>
                <div class="review-label">Site</div>
                <div class="review-value">{{ siteName }}</div>
              </div>
            </div>

            <div class="review-divider"></div>

            <div class="review-section">
              <h3 class="review-heading">Administrator</h3>
              <div class="review-grid">
                <div class="review-label">Username</div>
                <div class="review-value">{{ adminUsername }}</div>
                <div class="review-label">UPN</div>
                <div class="review-value">{{ adminUpn }}</div>
              </div>
            </div>

            <div class="review-divider"></div>

            <div class="review-section">
              <h3 class="review-heading">Database</h3>
              <div class="review-grid">
                <div class="review-label">Database Name</div>
                <div class="review-value">{{ databaseName }}</div>
                <div class="review-label">Connection</div>
                <div class="review-value connected"><i class="pi pi-check-circle"></i> Verified</div>
              </div>
            </div>
          </div>

          <Message severity="warn" :closable="false" class="review-warning">
            This will initialize the domain and create all required objects. This operation cannot be undone.
          </Message>

          <div class="step-actions">
            <Button label="Back" icon="pi pi-arrow-left" severity="secondary" text @click="prevStep" />
            <Button label="Provision Domain" icon="pi pi-play" @click="nextStep" severity="success" size="large" />
          </div>
        </div>

        <!-- ════════════════════════════════════════════ -->
        <!-- WINDOWS MODE: Steps 2-4                     -->
        <!-- ════════════════════════════════════════════ -->

        <!-- Step 2 (windows): Domain & Credentials -->
        <div v-if="currentStep === 2 && setupMode === 'windows'" class="step-panel">
          <h2 class="step-title">Domain &amp; Credentials</h2>
          <p class="step-subtitle">Discover a Windows Active Directory domain and provide administrator credentials.</p>

          <div class="form-group">
            <label class="form-label" for="winDomainName">Domain Name</label>
            <div class="discover-row">
              <InputText
                id="winDomainName"
                v-model="windowsDomainName"
                placeholder="contoso.com"
                class="form-input"
                fluid
              />
              <Button
                label="Discover Domain"
                icon="pi pi-search"
                :loading="windowsDomainDiscovering"
                @click="discoverWindowsDomain"
                :disabled="!windowsDomainName.trim() || !windowsDomainName.includes('.')"
                outlined
              />
            </div>
            <small class="form-help">Enter the DNS name of the Windows Active Directory domain to join.</small>
          </div>

          <Message v-if="windowsDomainResult && !windowsDomainResult.success" severity="error" :closable="false">
            <i class="pi pi-times-circle"></i>&nbsp; {{ windowsDomainResult.error || 'Failed to discover domain' }}
          </Message>

          <!-- Discovered domain info card -->
          <div v-if="windowsDomainResult?.success" class="review-card source-dc-info">
            <div class="source-dc-header">
              <i class="pi pi-check-circle source-dc-check"></i>
              <span class="source-dc-header-text">Windows Domain Discovered</span>
            </div>
            <div class="review-grid">
              <div class="review-label">DC Hostname</div>
              <div class="review-value">{{ windowsDomainResult.dcHostname }}</div>
              <div class="review-label">DC IP Address</div>
              <div class="review-value">{{ windowsDomainResult.dcIpAddress }}</div>
              <div class="review-label">RPC Port</div>
              <div class="review-value">{{ windowsDomainResult.dcRpcPort }}</div>
              <div class="review-label">Domain DN</div>
              <div class="review-value">{{ windowsDomainResult.domainDn }}</div>
              <div v-if="windowsDomainResult.functionalLevel !== undefined" class="review-label">Functional Level</div>
              <div v-if="windowsDomainResult.functionalLevel !== undefined" class="review-value">{{ windowsFunctionalLevelLabel }}</div>
            </div>
          </div>

          <div v-if="windowsDomainResult?.success" class="form-group" style="margin-top: 1.5rem;">
            <label class="form-label" for="winAdminUpn">Administrator UPN</label>
            <InputText
              id="winAdminUpn"
              v-model="windowsAdminUpn"
              placeholder="administrator@contoso.com"
              class="form-input"
              fluid
            />
            <small class="form-help">A domain administrator account authorized to add domain controllers.</small>
          </div>

          <div v-if="windowsDomainResult?.success" class="form-group">
            <label class="form-label" for="winAdminPass">Administrator Password</label>
            <Password
              id="winAdminPass"
              v-model="windowsAdminPassword"
              :feedback="false"
              toggleMask
              class="form-input"
              inputClass="form-input-inner"
              fluid
            />
          </div>

          <div class="step-actions">
            <Button label="Back" icon="pi pi-arrow-left" severity="secondary" text @click="prevStep" />
            <Button label="Next" icon="pi pi-arrow-right" iconPos="right" @click="nextStep" :disabled="!canProceed" />
          </div>
        </div>

        <!-- Step 3 (windows): Local Configuration -->
        <div v-if="currentStep === 3 && setupMode === 'windows'" class="step-panel">
          <h2 class="step-title">Local Configuration</h2>
          <p class="step-subtitle">Configure this server's settings as a domain controller.</p>

          <div class="form-group">
            <label class="form-label" for="winSiteName">Site Name</label>
            <InputText
              id="winSiteName"
              v-model="replicaSiteName"
              placeholder="Default-First-Site-Name"
              class="form-input"
              fluid
            />
            <small class="form-help">The Active Directory site this domain controller will belong to.</small>
          </div>

          <div class="form-group">
            <label class="form-label" for="winHostname">Hostname</label>
            <InputText
              id="winHostname"
              v-model="replicaHostname"
              placeholder="DC2"
              class="form-input"
              fluid
            />
            <small class="form-help">Leave blank to auto-detect from the system hostname.</small>
          </div>

          <div class="replica-summary-box">
            <i class="pi pi-info-circle"></i>
            <span>
              This server will join the Windows domain <strong>{{ windowsDomainName }}</strong> as a domain controller
              in site <strong>{{ replicaSiteName }}</strong> via RPC.
            </span>
          </div>

          <div class="step-actions">
            <Button label="Back" icon="pi pi-arrow-left" severity="secondary" text @click="prevStep" />
            <Button label="Next" icon="pi pi-arrow-right" iconPos="right" @click="nextStep" :disabled="!canProceed" />
          </div>
        </div>

        <!-- Step 4 (windows): Review -->
        <div v-if="currentStep === 4 && setupMode === 'windows'" class="step-panel">
          <h2 class="step-title">Review Configuration</h2>
          <p class="step-subtitle">Please review your settings before joining the Windows domain.</p>

          <div class="review-card">
            <div class="review-section">
              <h3 class="review-heading">Domain</h3>
              <div class="review-grid">
                <div class="review-label">Domain Name</div>
                <div class="review-value">{{ windowsDomainName }}</div>
                <div class="review-label">Domain DN</div>
                <div class="review-value">{{ windowsDomainResult?.domainDn }}</div>
                <div class="review-label">DC Hostname</div>
                <div class="review-value">{{ windowsDomainResult?.dcHostname }}</div>
                <div class="review-label">DC IP Address</div>
                <div class="review-value">{{ windowsDomainResult?.dcIpAddress }}</div>
                <div class="review-label">Transport</div>
                <div class="review-value">RPC (native Windows protocol)</div>
              </div>
            </div>

            <div class="review-divider"></div>

            <div class="review-section">
              <h3 class="review-heading">Credentials</h3>
              <div class="review-grid">
                <div class="review-label">Administrator UPN</div>
                <div class="review-value">{{ windowsAdminUpn }}</div>
              </div>
            </div>

            <div class="review-divider"></div>

            <div class="review-section">
              <h3 class="review-heading">Local Configuration</h3>
              <div class="review-grid">
                <div class="review-label">Site Name</div>
                <div class="review-value">{{ replicaSiteName }}</div>
                <div class="review-label">Hostname</div>
                <div class="review-value">{{ replicaHostname || '(auto-detect)' }}</div>
              </div>
            </div>

            <div class="review-divider"></div>

            <div class="review-section">
              <h3 class="review-heading">Database</h3>
              <div class="review-grid">
                <div class="review-label">Database Name</div>
                <div class="review-value">{{ databaseName }}</div>
                <div class="review-label">Connection</div>
                <div class="review-value connected"><i class="pi pi-check-circle"></i> Verified</div>
              </div>
            </div>
          </div>

          <Message severity="warn" :closable="false" class="review-warning">
            This will add this server as a domain controller to the Windows domain and replicate all directory data via RPC. This operation cannot be undone.
          </Message>

          <div class="step-actions">
            <Button label="Back" icon="pi pi-arrow-left" severity="secondary" text @click="prevStep" />
            <Button label="Join Domain" icon="pi pi-play" @click="nextStep" severity="success" size="large" />
          </div>
        </div>

        <!-- ════════════════════════════════════════════ -->
        <!-- MODERN MODE: Steps 2-4                      -->
        <!-- ════════════════════════════════════════════ -->

        <!-- Step 2 (modern): Source DC -->
        <div v-if="currentStep === 2 && setupMode === 'modern'" class="step-panel">
          <h2 class="step-title">Source Domain Controller</h2>
          <p class="step-subtitle">Connect to another modern domain controller instance to replicate from.</p>

          <div class="form-group">
            <label class="form-label" for="sourceDcUrl">Source Domain Controller URL</label>
            <InputText
              id="sourceDcUrl"
              v-model="sourceDcUrl"
              placeholder="https://dc1.contoso.com:9389"
              class="form-input"
              fluid
            />
            <small class="form-help">The URL of another modern domain controller instance.</small>
          </div>

          <div class="form-group">
            <Button
              label="Test Connection"
              icon="pi pi-bolt"
              :loading="sourceDcTesting"
              @click="testSourceDc"
              :disabled="!sourceDcUrl.trim()"
              outlined
            />
          </div>

          <Message v-if="sourceDcResult && !sourceDcResult.success" severity="error" :closable="false">
            <i class="pi pi-times-circle"></i>&nbsp; {{ sourceDcResult.error || 'Failed to connect to the source DC' }}
          </Message>

          <!-- Discovered domain info card -->
          <div v-if="sourceDcResult?.success" class="review-card source-dc-info">
            <div class="source-dc-header">
              <i class="pi pi-check-circle source-dc-check"></i>
              <span class="source-dc-header-text">Domain Controller Found</span>
            </div>
            <div class="review-grid">
              <div class="review-label">Domain Name</div>
              <div class="review-value">{{ sourceDcResult.domainName }}</div>
              <div class="review-label">Domain DN</div>
              <div class="review-value">{{ sourceDcResult.domainDn }}</div>
              <div class="review-label">Forest Name</div>
              <div class="review-value">{{ sourceDcResult.forestName }}</div>
              <div class="review-label">DC Hostname</div>
              <div class="review-value">{{ sourceDcResult.dcHostname }}</div>
              <div class="review-label">Functional Level</div>
              <div class="review-value">{{ functionalLevelLabel }}</div>
            </div>
          </div>

          <div v-if="sourceDcResult?.success" class="form-group" style="margin-top: 1.5rem;">
            <label class="form-label" for="replicaUpn">Administrator UPN</label>
            <InputText
              id="replicaUpn"
              v-model="replicaAdminUpn"
              placeholder="administrator@contoso.com"
              class="form-input"
              fluid
            />
            <small class="form-help">A domain administrator account authorized to add domain controllers.</small>
          </div>

          <div v-if="sourceDcResult?.success" class="form-group">
            <label class="form-label" for="replicaPass">Administrator Password</label>
            <Password
              id="replicaPass"
              v-model="replicaAdminPassword"
              :feedback="false"
              toggleMask
              class="form-input"
              inputClass="form-input-inner"
              fluid
            />
          </div>

          <div class="step-actions">
            <Button label="Back" icon="pi pi-arrow-left" severity="secondary" text @click="prevStep" />
            <Button label="Next" icon="pi pi-arrow-right" iconPos="right" @click="nextStep" :disabled="!canProceed" />
          </div>
        </div>

        <!-- Step 3 (modern): Local Configuration -->
        <div v-if="currentStep === 3 && setupMode === 'modern'" class="step-panel">
          <h2 class="step-title">Local Configuration</h2>
          <p class="step-subtitle">Configure this server's settings as a replica domain controller.</p>

          <div class="form-group">
            <label class="form-label" for="repSiteName">Site Name</label>
            <InputText
              id="repSiteName"
              v-model="replicaSiteName"
              placeholder="Default-First-Site-Name"
              class="form-input"
              fluid
            />
            <small class="form-help">The Active Directory site this domain controller will belong to.</small>
          </div>

          <div class="form-group">
            <label class="form-label" for="repHostname">Hostname</label>
            <InputText
              id="repHostname"
              v-model="replicaHostname"
              placeholder="DC2"
              class="form-input"
              fluid
            />
            <small class="form-help">Leave blank to auto-detect from the system hostname.</small>
          </div>

          <div class="replica-summary-box">
            <i class="pi pi-info-circle"></i>
            <span>
              This server will join domain <strong>{{ sourceDcResult?.domainName }}</strong> as a replica domain controller
              in site <strong>{{ replicaSiteName }}</strong>.
            </span>
          </div>

          <div class="step-actions">
            <Button label="Back" icon="pi pi-arrow-left" severity="secondary" text @click="prevStep" />
            <Button label="Next" icon="pi pi-arrow-right" iconPos="right" @click="nextStep" :disabled="!canProceed" />
          </div>
        </div>

        <!-- Step 4 (modern): Review -->
        <div v-if="currentStep === 4 && setupMode === 'modern'" class="step-panel">
          <h2 class="step-title">Review Configuration</h2>
          <p class="step-subtitle">Please review your settings before joining the domain.</p>

          <div class="review-card">
            <div class="review-section">
              <h3 class="review-heading">Source Domain Controller</h3>
              <div class="review-grid">
                <div class="review-label">Source DC URL</div>
                <div class="review-value">{{ sourceDcUrl }}</div>
                <div class="review-label">DC Hostname</div>
                <div class="review-value">{{ sourceDcResult?.dcHostname }}</div>
                <div class="review-label">Transport</div>
                <div class="review-value">HTTP (modern protocol)</div>
              </div>
            </div>

            <div class="review-divider"></div>

            <div class="review-section">
              <h3 class="review-heading">Domain</h3>
              <div class="review-grid">
                <div class="review-label">Domain Name</div>
                <div class="review-value">{{ sourceDcResult?.domainName }}</div>
                <div class="review-label">Domain DN</div>
                <div class="review-value">{{ sourceDcResult?.domainDn }}</div>
                <div class="review-label">Forest Name</div>
                <div class="review-value">{{ sourceDcResult?.forestName }}</div>
                <div class="review-label">Functional Level</div>
                <div class="review-value">{{ functionalLevelLabel }}</div>
              </div>
            </div>

            <div class="review-divider"></div>

            <div class="review-section">
              <h3 class="review-heading">Credentials</h3>
              <div class="review-grid">
                <div class="review-label">Administrator UPN</div>
                <div class="review-value">{{ replicaAdminUpn }}</div>
              </div>
            </div>

            <div class="review-divider"></div>

            <div class="review-section">
              <h3 class="review-heading">Local Configuration</h3>
              <div class="review-grid">
                <div class="review-label">Site Name</div>
                <div class="review-value">{{ replicaSiteName }}</div>
                <div class="review-label">Hostname</div>
                <div class="review-value">{{ replicaHostname || '(auto-detect)' }}</div>
              </div>
            </div>

            <div class="review-divider"></div>

            <div class="review-section">
              <h3 class="review-heading">Database</h3>
              <div class="review-grid">
                <div class="review-label">Database Name</div>
                <div class="review-value">{{ databaseName }}</div>
                <div class="review-label">Connection</div>
                <div class="review-value connected"><i class="pi pi-check-circle"></i> Verified</div>
              </div>
            </div>
          </div>

          <Message severity="warn" :closable="false" class="review-warning">
            This will add this server as a domain controller and replicate all directory data. This operation cannot be undone.
          </Message>

          <div class="step-actions">
            <Button label="Back" icon="pi pi-arrow-left" severity="secondary" text @click="prevStep" />
            <Button label="Join Domain" icon="pi pi-play" @click="nextStep" severity="success" size="large" />
          </div>
        </div>

        <!-- ════════════════════════════════════════════ -->
        <!-- PROVISIONING (shared, with mode variations) -->
        <!-- ════════════════════════════════════════════ -->

        <div v-if="currentStep === provisioningStepIndex" class="step-panel provisioning-panel">
          <!-- In progress (new domain mode) -->
          <template v-if="setupMode === 'new' && !provisioningComplete && !provisioningFailed && !provisioningError">
            <div class="provisioning-spinner">
              <ProgressSpinner strokeWidth="3" style="width: 64px; height: 64px;" />
            </div>
            <h2 class="step-title">Setting Up Your Domain</h2>
            <p class="step-subtitle">{{ provisioningPhase }}</p>
            <div class="progress-container">
              <ProgressBar :value="provisioningProgress" :showValue="true" class="provisioning-bar" />
            </div>
            <p class="provisioning-hint">This may take a few minutes. Please do not close this window.</p>
          </template>

          <!-- In progress (windows mode) -->
          <template v-if="setupMode === 'windows' && !provisioningComplete && !provisioningFailed && !provisioningError">
            <div class="provisioning-spinner">
              <ProgressSpinner strokeWidth="3" style="width: 64px; height: 64px;" />
            </div>
            <h2 class="step-title">Replicating Directory Data</h2>
            <p class="step-subtitle">{{ provisioningPhase }}</p>

            <div class="replication-nc-list">
              <!-- Schema NC -->
              <div class="nc-row" :class="{ 'nc-active': replicationProgress?.namingContext?.toLowerCase().includes('schema'), 'nc-done': replicaNcStatuses.schema.done }">
                <div class="nc-icon-wrap">
                  <i v-if="replicaNcStatuses.schema.done" class="pi pi-check-circle nc-icon-done"></i>
                  <ProgressSpinner v-else-if="replicationProgress?.namingContext?.toLowerCase().includes('schema')" style="width: 20px; height: 20px;" strokeWidth="4" />
                  <i v-else class="pi pi-circle nc-icon-pending"></i>
                </div>
                <div class="nc-details">
                  <div class="nc-name">Schema</div>
                  <div class="nc-stats" v-if="replicaNcStatuses.schema.objects > 0 || replicaNcStatuses.schema.done">
                    {{ replicaNcStatuses.schema.objects }} objects &middot; {{ formatBytes(replicaNcStatuses.schema.bytes) }}
                  </div>
                </div>
                <div class="nc-bar-wrap">
                  <div class="nc-bar" :style="{ width: replicaNcStatuses.schema.done ? '100%' : (replicationProgress?.namingContext?.toLowerCase().includes('schema') ? '50%' : '0%') }"></div>
                </div>
              </div>

              <!-- Configuration NC -->
              <div class="nc-row" :class="{ 'nc-active': replicationProgress?.namingContext?.toLowerCase().includes('configuration'), 'nc-done': replicaNcStatuses.configuration.done }">
                <div class="nc-icon-wrap">
                  <i v-if="replicaNcStatuses.configuration.done" class="pi pi-check-circle nc-icon-done"></i>
                  <ProgressSpinner v-else-if="replicationProgress?.namingContext?.toLowerCase().includes('configuration')" style="width: 20px; height: 20px;" strokeWidth="4" />
                  <i v-else class="pi pi-circle nc-icon-pending"></i>
                </div>
                <div class="nc-details">
                  <div class="nc-name">Configuration</div>
                  <div class="nc-stats" v-if="replicaNcStatuses.configuration.objects > 0 || replicaNcStatuses.configuration.done">
                    {{ replicaNcStatuses.configuration.objects }} objects &middot; {{ formatBytes(replicaNcStatuses.configuration.bytes) }}
                  </div>
                </div>
                <div class="nc-bar-wrap">
                  <div class="nc-bar" :style="{ width: replicaNcStatuses.configuration.done ? '100%' : (replicationProgress?.namingContext?.toLowerCase().includes('configuration') ? '50%' : '0%') }"></div>
                </div>
              </div>

              <!-- Domain NC -->
              <div class="nc-row" :class="{ 'nc-active': replicationProgress?.namingContext && !replicationProgress.namingContext.toLowerCase().includes('schema') && !replicationProgress.namingContext.toLowerCase().includes('configuration'), 'nc-done': replicaNcStatuses.domain.done }">
                <div class="nc-icon-wrap">
                  <i v-if="replicaNcStatuses.domain.done" class="pi pi-check-circle nc-icon-done"></i>
                  <ProgressSpinner v-else-if="replicationProgress?.namingContext && !replicationProgress.namingContext.toLowerCase().includes('schema') && !replicationProgress.namingContext.toLowerCase().includes('configuration')" style="width: 20px; height: 20px;" strokeWidth="4" />
                  <i v-else class="pi pi-circle nc-icon-pending"></i>
                </div>
                <div class="nc-details">
                  <div class="nc-name">Domain</div>
                  <div class="nc-stats" v-if="replicaNcStatuses.domain.objects > 0 || replicaNcStatuses.domain.done">
                    {{ replicaNcStatuses.domain.objects }} objects &middot; {{ formatBytes(replicaNcStatuses.domain.bytes) }}
                  </div>
                </div>
                <div class="nc-bar-wrap">
                  <div class="nc-bar" :style="{ width: replicaNcStatuses.domain.done ? '100%' : (replicationProgress?.namingContext && !replicationProgress.namingContext.toLowerCase().includes('schema') && !replicationProgress.namingContext.toLowerCase().includes('configuration') ? '50%' : '0%') }"></div>
                </div>
              </div>
            </div>

            <div class="progress-container" style="margin-top: 1.5rem;">
              <ProgressBar :value="provisioningProgress" :showValue="true" class="provisioning-bar" />
            </div>
            <p class="provisioning-hint">Replicating directory data from the Windows domain controller via RPC. This may take several minutes.</p>
          </template>

          <!-- In progress (modern mode) -->
          <template v-if="setupMode === 'modern' && !provisioningComplete && !provisioningFailed && !provisioningError">
            <div class="provisioning-spinner">
              <ProgressSpinner strokeWidth="3" style="width: 64px; height: 64px;" />
            </div>
            <h2 class="step-title">Replicating Directory Data</h2>
            <p class="step-subtitle">{{ provisioningPhase }}</p>

            <div class="replication-nc-list">
              <!-- Schema NC -->
              <div class="nc-row" :class="{ 'nc-active': replicationProgress?.namingContext?.toLowerCase().includes('schema'), 'nc-done': replicaNcStatuses.schema.done }">
                <div class="nc-icon-wrap">
                  <i v-if="replicaNcStatuses.schema.done" class="pi pi-check-circle nc-icon-done"></i>
                  <ProgressSpinner v-else-if="replicationProgress?.namingContext?.toLowerCase().includes('schema')" style="width: 20px; height: 20px;" strokeWidth="4" />
                  <i v-else class="pi pi-circle nc-icon-pending"></i>
                </div>
                <div class="nc-details">
                  <div class="nc-name">Schema</div>
                  <div class="nc-stats" v-if="replicaNcStatuses.schema.objects > 0 || replicaNcStatuses.schema.done">
                    {{ replicaNcStatuses.schema.objects }} objects &middot; {{ formatBytes(replicaNcStatuses.schema.bytes) }}
                  </div>
                </div>
                <div class="nc-bar-wrap">
                  <div class="nc-bar" :style="{ width: replicaNcStatuses.schema.done ? '100%' : (replicationProgress?.namingContext?.toLowerCase().includes('schema') ? '50%' : '0%') }"></div>
                </div>
              </div>

              <!-- Configuration NC -->
              <div class="nc-row" :class="{ 'nc-active': replicationProgress?.namingContext?.toLowerCase().includes('configuration'), 'nc-done': replicaNcStatuses.configuration.done }">
                <div class="nc-icon-wrap">
                  <i v-if="replicaNcStatuses.configuration.done" class="pi pi-check-circle nc-icon-done"></i>
                  <ProgressSpinner v-else-if="replicationProgress?.namingContext?.toLowerCase().includes('configuration')" style="width: 20px; height: 20px;" strokeWidth="4" />
                  <i v-else class="pi pi-circle nc-icon-pending"></i>
                </div>
                <div class="nc-details">
                  <div class="nc-name">Configuration</div>
                  <div class="nc-stats" v-if="replicaNcStatuses.configuration.objects > 0 || replicaNcStatuses.configuration.done">
                    {{ replicaNcStatuses.configuration.objects }} objects &middot; {{ formatBytes(replicaNcStatuses.configuration.bytes) }}
                  </div>
                </div>
                <div class="nc-bar-wrap">
                  <div class="nc-bar" :style="{ width: replicaNcStatuses.configuration.done ? '100%' : (replicationProgress?.namingContext?.toLowerCase().includes('configuration') ? '50%' : '0%') }"></div>
                </div>
              </div>

              <!-- Domain NC -->
              <div class="nc-row" :class="{ 'nc-active': replicationProgress?.namingContext && !replicationProgress.namingContext.toLowerCase().includes('schema') && !replicationProgress.namingContext.toLowerCase().includes('configuration'), 'nc-done': replicaNcStatuses.domain.done }">
                <div class="nc-icon-wrap">
                  <i v-if="replicaNcStatuses.domain.done" class="pi pi-check-circle nc-icon-done"></i>
                  <ProgressSpinner v-else-if="replicationProgress?.namingContext && !replicationProgress.namingContext.toLowerCase().includes('schema') && !replicationProgress.namingContext.toLowerCase().includes('configuration')" style="width: 20px; height: 20px;" strokeWidth="4" />
                  <i v-else class="pi pi-circle nc-icon-pending"></i>
                </div>
                <div class="nc-details">
                  <div class="nc-name">Domain</div>
                  <div class="nc-stats" v-if="replicaNcStatuses.domain.objects > 0 || replicaNcStatuses.domain.done">
                    {{ replicaNcStatuses.domain.objects }} objects &middot; {{ formatBytes(replicaNcStatuses.domain.bytes) }}
                  </div>
                </div>
                <div class="nc-bar-wrap">
                  <div class="nc-bar" :style="{ width: replicaNcStatuses.domain.done ? '100%' : (replicationProgress?.namingContext && !replicationProgress.namingContext.toLowerCase().includes('schema') && !replicationProgress.namingContext.toLowerCase().includes('configuration') ? '50%' : '0%') }"></div>
                </div>
              </div>
            </div>

            <div class="progress-container" style="margin-top: 1.5rem;">
              <ProgressBar :value="provisioningProgress" :showValue="true" class="provisioning-bar" />
            </div>
            <p class="provisioning-hint">Replicating directory data from the source DC. This may take several minutes.</p>
          </template>

          <!-- Success (new domain) -->
          <template v-if="provisioningComplete && setupMode === 'new'">
            <div class="success-icon">
              <i class="pi pi-check-circle"></i>
            </div>
            <h2 class="step-title success-title">Domain Provisioning Complete!</h2>
            <p class="step-subtitle">
              Your Active Directory domain <strong>{{ domainName }}</strong> has been successfully created.
            </p>
            <div class="success-summary">
              <div class="summary-item">
                <i class="pi pi-globe"></i>
                <span>Domain: {{ domainName }}</span>
              </div>
              <div class="summary-item">
                <i class="pi pi-user"></i>
                <span>Administrator: {{ adminUpn }}</span>
              </div>
              <div class="summary-item">
                <i class="pi pi-sitemap"></i>
                <span>Site: {{ siteName }}</span>
              </div>
            </div>
            <div class="step-actions center">
              <Button label="Launch Dashboard" icon="pi pi-arrow-right" iconPos="right" @click="launchDashboard" size="large" />
            </div>
          </template>

          <!-- Success (windows) -->
          <template v-if="provisioningComplete && setupMode === 'windows'">
            <div class="success-icon">
              <i class="pi pi-check-circle"></i>
            </div>
            <h2 class="step-title success-title">Domain Join Complete!</h2>
            <p class="step-subtitle">
              This server has joined the Windows domain <strong>{{ windowsDomainName }}</strong>.
            </p>
            <div class="success-summary">
              <div class="summary-item">
                <i class="pi pi-globe"></i>
                <span>Domain: {{ windowsDomainName }}</span>
              </div>
              <div class="summary-item">
                <i class="pi pi-server"></i>
                <span>Source DC: {{ windowsDomainResult?.dcHostname }}</span>
              </div>
              <div class="summary-item">
                <i class="pi pi-sitemap"></i>
                <span>Site: {{ replicaSiteName }}</span>
              </div>
            </div>
            <div class="step-actions center">
              <Button label="Launch Dashboard" icon="pi pi-arrow-right" iconPos="right" @click="launchDashboard" size="large" />
            </div>
          </template>

          <!-- Success (modern) -->
          <template v-if="provisioningComplete && setupMode === 'modern'">
            <div class="success-icon">
              <i class="pi pi-check-circle"></i>
            </div>
            <h2 class="step-title success-title">Domain Join Complete!</h2>
            <p class="step-subtitle">
              This server has been added as a Modern DC in <strong>{{ sourceDcResult?.domainName }}</strong>.
            </p>
            <div class="success-summary">
              <div class="summary-item">
                <i class="pi pi-globe"></i>
                <span>Domain: {{ sourceDcResult?.domainName }}</span>
              </div>
              <div class="summary-item">
                <i class="pi pi-server"></i>
                <span>Source DC: {{ sourceDcResult?.dcHostname }}</span>
              </div>
              <div class="summary-item">
                <i class="pi pi-sitemap"></i>
                <span>Site: {{ replicaSiteName }}</span>
              </div>
            </div>
            <div class="step-actions center">
              <Button label="Launch Dashboard" icon="pi pi-arrow-right" iconPos="right" @click="launchDashboard" size="large" />
            </div>
          </template>

          <!-- Error -->
          <template v-if="provisioningFailed || provisioningError">
            <div class="error-icon">
              <i class="pi pi-times-circle"></i>
            </div>
            <h2 class="step-title error-title">Provisioning Failed</h2>
            <Message severity="error" :closable="false">
              {{ setupStore.status?.provisioningError || provisioningError }}
            </Message>
            <div class="step-actions center">
              <Button label="Retry" icon="pi pi-refresh" @click="retryProvisioning" severity="danger" />
            </div>
          </template>
        </div>

      </div>
    </div>
  </div>
</template>

<style scoped>
.setup-wizard {
  min-height: 100vh;
  background: var(--p-surface-ground);
  display: flex;
  flex-direction: column;
}

/* Header */
.setup-header {
  background: var(--p-surface-card);
  border-bottom: 1px solid var(--app-neutral-border);
  padding: 1rem 2rem;
}

.setup-header-inner {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  max-width: 900px;
  margin: 0 auto;
}

.setup-header-icon {
  font-size: 1.5rem;
  color: var(--app-info-text);
}

.setup-header-title {
  font-size: 1.125rem;
  font-weight: 600;
  color: var(--p-text-color);
}

/* Step Indicator */
.step-indicator {
  background: var(--p-surface-card);
  border-bottom: 1px solid var(--app-neutral-border);
  padding: 1.5rem 2rem;
}

.step-indicator-inner {
  display: flex;
  align-items: center;
  justify-content: center;
  max-width: 700px;
  margin: 0 auto;
}

.step-dot {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
  position: relative;
  z-index: 1;
}

.step-circle {
  width: 2.25rem;
  height: 2.25rem;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.8125rem;
  font-weight: 600;
  background: var(--app-neutral-border);
  color: var(--p-text-muted-color);
  transition: all 0.3s ease;
}

.step-dot.active .step-circle {
  background: var(--app-info-text);
  color: var(--p-surface-0);
  box-shadow: 0 0 0 4px color-mix(in srgb, var(--app-info-text) 15%, transparent);
}

.step-dot.completed .step-circle {
  background: var(--app-success-text);
  color: var(--p-surface-0);
}

.step-label {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  font-weight: 500;
  white-space: nowrap;
}

.step-dot.active .step-label {
  color: var(--app-info-text);
  font-weight: 600;
}

.step-dot.completed .step-label {
  color: var(--app-success-text);
}

.step-line {
  flex: 1;
  height: 2px;
  background: var(--app-neutral-border);
  margin: 0 0.5rem;
  margin-bottom: 1.5rem;
  transition: background 0.3s ease;
}

.step-line.filled {
  background: var(--app-success-text);
}

/* Content Area */
.setup-content {
  flex: 1;
  display: flex;
  justify-content: center;
  padding: 2rem;
}

.setup-panel {
  width: 100%;
  max-width: 700px;
}

/* Step Panels */
.step-panel {
  background: var(--p-surface-card);
  border: 1px solid var(--app-neutral-border);
  border-radius: 0.75rem;
  padding: 2.5rem;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04), 0 4px 12px rgba(0, 0, 0, 0.02);
}

.step-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--p-text-color);
  margin-bottom: 0.5rem;
}

.step-subtitle {
  color: var(--p-text-muted-color);
  font-size: 0.9375rem;
  line-height: 1.6;
  margin-bottom: 2rem;
}

/* Welcome step */
.welcome-icon {
  display: flex;
  justify-content: center;
  margin-bottom: 1.5rem;
}

.welcome-icon .pi {
  font-size: 3.5rem;
  color: var(--app-info-text);
  background: var(--app-info-bg);
  width: 5rem;
  height: 5rem;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 1rem;
}

.welcome-title {
  text-align: center;
}

.welcome-icon + .welcome-title + .step-subtitle {
  text-align: center;
}

/* Mode Selection Cards */
.mode-selection {
  display: flex;
  gap: 1rem;
  margin-bottom: 1rem;
}

.mode-card {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  padding: 1.75rem 1.25rem;
  border: 2px solid var(--app-neutral-border);
  border-radius: 0.75rem;
  cursor: pointer;
  transition: all 0.2s ease;
  background: var(--p-surface-card);
}

.mode-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.08);
}

.mode-card-new:hover,
.mode-card-new.selected {
  border-color: var(--app-info-text);
}

.mode-card-windows:hover,
.mode-card-windows.selected {
  border-color: var(--app-warn-text);
}

.mode-card-modern:hover,
.mode-card-modern.selected {
  border-color: var(--app-success-text);
}

.mode-card.selected {
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.08);
  transform: translateY(-2px);
}

.mode-icon-wrap {
  width: 4rem;
  height: 4rem;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 1.25rem;
}

.mode-icon-wrap .pi {
  font-size: 1.75rem;
}

.mode-icon-blue {
  background: var(--app-info-bg);
  color: var(--app-info-text);
}

.mode-icon-amber {
  background: var(--app-warn-bg);
  color: var(--app-warn-text);
}

.mode-icon-green {
  background: var(--app-success-bg);
  color: var(--app-success-text);
}

.mode-card-title {
  font-size: 1.0625rem;
  font-weight: 700;
  color: var(--p-text-color);
  margin-bottom: 0.5rem;
}

.mode-card-desc {
  font-size: 0.875rem;
  color: var(--p-text-muted-color);
  line-height: 1.5;
  margin: 0;
}

/* Discover row */
.discover-row {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
}

.discover-row .form-input {
  flex: 1;
}

/* Form elements */
.form-group {
  margin-bottom: 1.5rem;
}

.form-label {
  display: block;
  font-weight: 600;
  color: var(--p-text-color);
  margin-bottom: 0.5rem;
  font-size: 0.875rem;
}

.form-input {
  width: 100%;
}

.form-input-inner {
  width: 100%;
}

.form-help {
  display: block;
  color: var(--p-text-muted-color);
  font-size: 0.8125rem;
  margin-top: 0.375rem;
}

.form-feedback {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 0.8125rem;
  margin-top: 0.375rem;
}

.form-feedback.success { color: var(--app-success-text); }
.form-feedback.error { color: var(--app-danger-text); }
.form-feedback.info { color: var(--p-text-muted-color); }

.password-toggle-wrap {
  position: relative;
}

.password-toggle-wrap .form-input {
  padding-right: 2.5rem;
}

.toggle-visibility {
  position: absolute;
  right: 0.75rem;
  top: 50%;
  transform: translateY(-50%);
  background: none;
  border: none;
  color: var(--p-text-muted-color);
  cursor: pointer;
  padding: 0.25rem;
  font-size: 1rem;
}

.toggle-visibility:hover {
  color: var(--p-text-muted-color);
}

/* Password strength */
.password-strength {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-top: 0.5rem;
}

.strength-bars {
  display: flex;
  gap: 0.25rem;
  flex: 1;
}

.strength-bar {
  height: 4px;
  flex: 1;
  border-radius: 2px;
  transition: background 0.3s ease;
}

.strength-label {
  font-size: 0.75rem;
  font-weight: 600;
  min-width: 3rem;
}

.password-requirements {
  margin-top: 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.req {
  font-size: 0.8125rem;
  display: flex;
  align-items: center;
  gap: 0.375rem;
}

.req.met { color: var(--app-success-text); }
.req.unmet { color: var(--p-text-muted-color); }

.req .pi {
  font-size: 0.75rem;
}

/* Review */
.review-card {
  background: var(--p-surface-ground);
  border: 1px solid var(--app-neutral-border);
  border-radius: 0.625rem;
  padding: 1.5rem;
  margin-bottom: 1.5rem;
}

.review-section {
  padding: 0.25rem 0;
}

.review-heading {
  font-size: 0.8125rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--p-text-color);
  margin-bottom: 0.75rem;
}

.review-grid {
  display: grid;
  grid-template-columns: 140px 1fr;
  gap: 0.5rem 1rem;
}

.review-label {
  font-size: 0.875rem;
  color: var(--p-text-color);
}

.review-value {
  font-size: 0.875rem;
  color: var(--p-text-color);
  font-weight: 500;
}

.review-value.connected {
  color: var(--app-success-text);
}

.review-divider {
  height: 1px;
  background: var(--app-neutral-border);
  margin: 1rem 0;
}

.review-warning {
  margin-bottom: 1.5rem;
}

/* Source DC info card */
.source-dc-info {
  border-color: var(--app-success-border);
  background: var(--app-success-bg);
}

.source-dc-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.source-dc-check {
  color: var(--app-success-text);
  font-size: 1.125rem;
}

.source-dc-header-text {
  font-weight: 600;
  color: var(--app-success-text-strong);
  font-size: 0.9375rem;
}

/* Replica summary box */
.replica-summary-box {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  background: var(--app-info-bg);
  border: 1px solid var(--app-info-border);
  border-radius: 0.625rem;
  padding: 1rem 1.25rem;
  margin-top: 1rem;
  font-size: 0.875rem;
  color: var(--app-info-text-strong);
  line-height: 1.5;
}

.replica-summary-box .pi {
  font-size: 1rem;
  margin-top: 0.125rem;
  flex-shrink: 0;
}

/* Replication NC progress */
.replication-nc-list {
  max-width: 500px;
  margin: 1.5rem auto 0;
  display: flex;
  flex-direction: column;
  gap: 1rem;
  text-align: left;
}

.nc-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem 1rem;
  background: var(--p-surface-ground);
  border: 1px solid var(--app-neutral-border);
  border-radius: 0.5rem;
  transition: all 0.2s ease;
}

.nc-row.nc-active {
  border-color: var(--app-info-border);
  background: var(--app-info-bg);
}

.nc-row.nc-done {
  border-color: var(--app-success-border);
  background: var(--app-success-bg);
}

.nc-icon-wrap {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  flex-shrink: 0;
}

.nc-icon-done {
  color: var(--app-success-text);
  font-size: 1.125rem;
}

.nc-icon-pending {
  color: var(--p-text-muted-color);
  font-size: 1rem;
}

.nc-details {
  flex: 1;
  min-width: 0;
}

.nc-name {
  font-weight: 600;
  font-size: 0.875rem;
  color: var(--p-text-color);
}

.nc-stats {
  font-size: 0.75rem;
  color: var(--p-text-color);
  margin-top: 0.125rem;
}

.nc-bar-wrap {
  width: 80px;
  height: 4px;
  background: var(--app-neutral-border);
  border-radius: 2px;
  overflow: hidden;
  flex-shrink: 0;
}

.nc-bar {
  height: 100%;
  background: var(--app-info-text);
  border-radius: 2px;
  transition: width 0.5s ease;
}

.nc-row.nc-done .nc-bar {
  background: var(--app-success-text);
}

/* Actions */
.step-actions {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 2rem;
  padding-top: 1.5rem;
  border-top: 1px solid var(--app-neutral-bg);
}

.step-actions.center {
  justify-content: center;
}

/* Provisioning */
.provisioning-panel {
  text-align: center;
}

.provisioning-spinner {
  display: flex;
  justify-content: center;
  margin-bottom: 1.5rem;
}

.progress-container {
  max-width: 500px;
  margin: 0 auto 1.5rem;
}

.provisioning-bar {
  height: 0.75rem;
}

.provisioning-bar :deep(.p-progressbar-value) {
  transition: width 0.5s ease;
}

.provisioning-hint {
  color: var(--p-text-muted-color);
  font-size: 0.8125rem;
}

/* Success */
.success-icon {
  display: flex;
  justify-content: center;
  margin-bottom: 1.5rem;
}

.success-icon .pi {
  font-size: 4rem;
  color: var(--app-success-text);
}

.success-title {
  color: var(--app-success-text);
}

.success-summary {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  background: var(--app-success-bg);
  border: 1px solid var(--app-success-border);
  border-radius: 0.625rem;
  padding: 1.25rem;
  margin: 1.5rem auto;
  max-width: 400px;
  text-align: left;
}

.summary-item {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  font-size: 0.875rem;
  color: var(--app-success-text-strong);
}

.summary-item .pi {
  font-size: 1rem;
}

/* Error */
.error-icon {
  display: flex;
  justify-content: center;
  margin-bottom: 1.5rem;
}

.error-icon .pi {
  font-size: 4rem;
  color: var(--app-danger-text);
}

.error-title {
  color: var(--app-danger-text);
}

/* Responsive */
@media (max-width: 768px) {
  .setup-content {
    padding: 1rem;
  }

  .step-panel {
    padding: 1.5rem;
  }

  .step-indicator-inner {
    overflow-x: auto;
    padding-bottom: 0.5rem;
  }

  .step-label {
    font-size: 0.625rem;
  }

  .review-grid {
    grid-template-columns: 1fr;
  }

  .mode-selection {
    flex-direction: column;
  }
}
</style>
