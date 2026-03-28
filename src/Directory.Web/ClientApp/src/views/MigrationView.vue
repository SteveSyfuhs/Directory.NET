<script setup lang="ts">
import { ref, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import InputSwitch from 'primevue/inputswitch'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import ProgressBar from 'primevue/progressbar'
import { useToast } from 'primevue/usetoast'
import type {
  MigrationSource,
  MigrationSourceType,
  MigrationPlan,
  MigrationMapping,
  MigrationPreview,
  MigrationResult,
  MigrationHistoryEntry,
  SchemaDiscoveryResult,
  ConflictResolution,
} from '../types/migration'
import {
  fetchMigrationSources,
  createMigrationSource,
  deleteMigrationSource,
  testMigrationSource,
  discoverMigrationSchema,
  previewMigration,
  executeMigration,
  fetchMigrationHistory,
} from '../api/migration'

const toast = useToast()
const loading = ref(false)
const sources = ref<MigrationSource[]>([])
const history = ref<MigrationHistoryEntry[]>([])

// Source dialog
const showSourceDialog = ref(false)
const editingSource = ref<Partial<MigrationSource>>({})
const testingConnection = ref(false)

// Migration wizard dialog
const showWizardDialog = ref(false)
const wizardStep = ref(1)
const selectedSource = ref<MigrationSource | null>(null)
const discoveredSchema = ref<SchemaDiscoveryResult | null>(null)
const plan = ref<Partial<MigrationPlan>>({
  attributeMappings: [],
  options: {
    migrateUsers: true,
    migrateGroups: true,
    migrateOUs: true,
    migrateComputers: false,
    preserveSidHistory: false,
    preservePasswords: false,
    migrateGroupMemberships: true,
    dryRun: false,
    onConflict: 'Skip' as ConflictResolution,
  },
})
const preview = ref<MigrationPreview | null>(null)
const migrationResult = ref<MigrationResult | null>(null)
const executing = ref(false)

const sourceTypeOptions = [
  { label: 'Active Directory', value: 'ActiveDirectory' },
  { label: 'OpenLDAP', value: 'OpenLDAP' },
  { label: 'FreeIPA', value: 'FreeIPA' },
  { label: 'Generic LDAP', value: 'GenericLDAP' },
  { label: 'LDIF File', value: 'LdifFile' },
]

const conflictOptions = [
  { label: 'Skip', value: 'Skip' },
  { label: 'Overwrite', value: 'Overwrite' },
  { label: 'Merge', value: 'Merge' },
  { label: 'Rename', value: 'Rename' },
]

onMounted(async () => {
  await Promise.all([loadSources(), loadHistory()])
})

async function loadSources() {
  loading.value = true
  try {
    sources.value = await fetchMigrationSources()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function loadHistory() {
  try {
    history.value = await fetchMigrationHistory()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function openAddSource() {
  editingSource.value = { port: 389, useSsl: false, type: 'ActiveDirectory' as MigrationSourceType }
  showSourceDialog.value = true
}

async function saveSource() {
  try {
    await createMigrationSource(editingSource.value)
    toast.add({ severity: 'success', summary: 'Source Created', life: 3000 })
    showSourceDialog.value = false
    await loadSources()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function removeSource(source: MigrationSource) {
  try {
    await deleteMigrationSource(source.id)
    toast.add({ severity: 'success', summary: 'Source Deleted', life: 3000 })
    await loadSources()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function testConnection() {
  testingConnection.value = true
  try {
    const result = await testMigrationSource(editingSource.value)
    if (result.success) {
      toast.add({ severity: 'success', summary: 'Connection OK', detail: result.message, life: 5000 })
    } else {
      toast.add({ severity: 'error', summary: 'Connection Failed', detail: result.message, life: 5000 })
    }
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    testingConnection.value = false
  }
}

function openMigrationWizard(source: MigrationSource) {
  selectedSource.value = source
  plan.value.sourceId = source.id
  plan.value.targetBaseDn = source.baseDn
  wizardStep.value = 1
  discoveredSchema.value = null
  preview.value = null
  migrationResult.value = null
  showWizardDialog.value = true
}

async function discoverSchema() {
  if (!selectedSource.value) return
  try {
    discoveredSchema.value = await discoverMigrationSchema(selectedSource.value)
    toast.add({ severity: 'info', summary: 'Schema Discovered', detail: `Found ${discoveredSchema.value.attributes.length} attributes`, life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function runPreview() {
  try {
    preview.value = await previewMigration(plan.value)
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

async function runMigration() {
  executing.value = true
  try {
    migrationResult.value = await executeMigration(plan.value)
    toast.add({
      severity: migrationResult.value.status === 'Completed' ? 'success' : 'error',
      summary: `Migration ${migrationResult.value.status}`,
      detail: `Processed: ${migrationResult.value.totalProcessed}, Created: ${migrationResult.value.created}, Failed: ${migrationResult.value.failed}`,
      life: 10000,
    })
    await loadHistory()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    executing.value = false
  }
}

function addMapping() {
  if (!plan.value.attributeMappings) plan.value.attributeMappings = []
  plan.value.attributeMappings.push({ sourceAttribute: '', targetAttribute: '' })
}

function removeMapping(index: number) {
  plan.value.attributeMappings?.splice(index, 1)
}

function statusSeverity(status: string) {
  switch (status) {
    case 'Completed': return 'success'
    case 'Failed': return 'danger'
    case 'Running': return 'info'
    case 'Cancelled': return 'warn'
    default: return 'secondary'
  }
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Migration Wizard</h1>
      <p>Migrate users, groups, and OUs from Active Directory, OpenLDAP, or LDIF files.</p>
    </div>

    <!-- Sources Section -->
    <div class="card" style="margin-bottom: 1.5rem">
      <div class="toolbar">
        <span class="card-title" style="margin-bottom: 0">Migration Sources</span>
        <span class="toolbar-spacer"></span>
        <Button label="Add Source" icon="pi pi-plus" size="small" @click="openAddSource" />
      </div>
      <DataTable :value="sources" :loading="loading" size="small" stripedRows>
        <Column field="name" header="Name" />
        <Column field="type" header="Type">
          <template #body="{ data }">
            <Tag :value="data.type" />
          </template>
        </Column>
        <Column field="host" header="Host" />
        <Column field="port" header="Port" />
        <Column field="baseDn" header="Base DN" />
        <Column header="Actions" style="width: 180px">
          <template #body="{ data }">
            <Button icon="pi pi-play" size="small" text rounded v-tooltip="'Start Migration'" @click="openMigrationWizard(data)" />
            <Button icon="pi pi-trash" size="small" text rounded severity="danger" v-tooltip="'Delete'" @click="removeSource(data)" />
          </template>
        </Column>
      </DataTable>
    </div>

    <!-- Migration History -->
    <div class="card">
      <div class="card-title">Migration History</div>
      <DataTable :value="history" size="small" stripedRows>
        <Column field="sourceName" header="Source" />
        <Column field="sourceType" header="Type">
          <template #body="{ data }">
            <Tag :value="data.sourceType" />
          </template>
        </Column>
        <Column field="status" header="Status">
          <template #body="{ data }">
            <Tag :value="data.status" :severity="statusSeverity(data.status)" />
          </template>
        </Column>
        <Column field="totalProcessed" header="Processed" />
        <Column field="created" header="Created" />
        <Column field="failed" header="Failed" />
        <Column field="startedAt" header="Started">
          <template #body="{ data }">{{ new Date(data.startedAt).toLocaleString() }}</template>
        </Column>
      </DataTable>
    </div>

    <!-- Add Source Dialog -->
    <Dialog v-model:visible="showSourceDialog" header="Add Migration Source" :style="{ width: '550px' }" modal>
      <div style="display: flex; flex-direction: column; gap: 1rem">
        <div>
          <label>Name</label>
          <InputText v-model="editingSource.name" style="width: 100%" placeholder="e.g., Production AD" />
        </div>
        <div>
          <label>Type</label>
          <Select v-model="editingSource.type" :options="sourceTypeOptions" optionLabel="label" optionValue="value" style="width: 100%" />
        </div>
        <div style="display: flex; gap: 1rem">
          <div style="flex: 1">
            <label>Host</label>
            <InputText v-model="editingSource.host" style="width: 100%" placeholder="dc1.example.com" />
          </div>
          <div style="width: 100px">
            <label>Port</label>
            <InputNumber v-model="editingSource.port" style="width: 100%" />
          </div>
        </div>
        <div>
          <label style="display: flex; align-items: center; gap: 0.5rem">
            <InputSwitch v-model="editingSource.useSsl" /> Use SSL/TLS
          </label>
        </div>
        <div>
          <label>Bind DN</label>
          <InputText v-model="editingSource.bindDn" style="width: 100%" placeholder="CN=Admin,DC=example,DC=com" />
        </div>
        <div>
          <label>Bind Password</label>
          <InputText v-model="editingSource.bindPassword" type="password" style="width: 100%" />
        </div>
        <div>
          <label>Base DN</label>
          <InputText v-model="editingSource.baseDn" style="width: 100%" placeholder="DC=example,DC=com" />
        </div>
        <div>
          <label>LDAP Filter (optional)</label>
          <InputText v-model="editingSource.filter" style="width: 100%" placeholder="(objectClass=*)" />
        </div>
      </div>
      <template #footer>
        <Button label="Test Connection" icon="pi pi-bolt" text :loading="testingConnection" @click="testConnection" />
        <Button label="Cancel" text @click="showSourceDialog = false" />
        <Button label="Save" icon="pi pi-check" @click="saveSource" />
      </template>
    </Dialog>

    <!-- Migration Wizard Dialog -->
    <Dialog v-model:visible="showWizardDialog" header="Migration Wizard" :style="{ width: '700px' }" modal>
      <!-- Step 1: Schema Discovery -->
      <div v-if="wizardStep === 1" style="display: flex; flex-direction: column; gap: 1rem">
        <h3>Step 1: Discover Schema</h3>
        <p>Discover available attributes from <strong>{{ selectedSource?.name }}</strong>.</p>
        <Button label="Discover Schema" icon="pi pi-search" @click="discoverSchema" />
        <div v-if="discoveredSchema">
          <p><strong>Object Classes:</strong> {{ discoveredSchema.objectClasses.join(', ') }}</p>
          <p><strong>Attributes:</strong> {{ discoveredSchema.attributes.length }} found</p>
        </div>
      </div>

      <!-- Step 2: Attribute Mapping -->
      <div v-if="wizardStep === 2" style="display: flex; flex-direction: column; gap: 1rem">
        <h3>Step 2: Attribute Mapping</h3>
        <div>
          <label>Target Base DN</label>
          <InputText v-model="plan.targetBaseDn" style="width: 100%" />
        </div>
        <div>
          <Button label="Add Mapping" icon="pi pi-plus" size="small" text @click="addMapping" />
        </div>
        <div v-for="(mapping, i) in plan.attributeMappings" :key="i" style="display: flex; gap: 0.5rem; align-items: center">
          <InputText v-model="mapping.sourceAttribute" placeholder="Source attr" style="flex: 1" />
          <i class="pi pi-arrow-right" />
          <InputText v-model="mapping.targetAttribute" placeholder="Target attr" style="flex: 1" />
          <InputText v-model="mapping.transformRule" placeholder="Transform" style="width: 120px" />
          <Button icon="pi pi-trash" size="small" text severity="danger" @click="removeMapping(i)" />
        </div>
      </div>

      <!-- Step 3: Options & Preview -->
      <div v-if="wizardStep === 3" style="display: flex; flex-direction: column; gap: 1rem">
        <h3>Step 3: Options & Preview</h3>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem">
          <label style="display: flex; align-items: center; gap: 0.5rem">
            <InputSwitch v-model="plan.options!.migrateUsers" /> Migrate Users
          </label>
          <label style="display: flex; align-items: center; gap: 0.5rem">
            <InputSwitch v-model="plan.options!.migrateGroups" /> Migrate Groups
          </label>
          <label style="display: flex; align-items: center; gap: 0.5rem">
            <InputSwitch v-model="plan.options!.migrateOUs" /> Migrate OUs
          </label>
          <label style="display: flex; align-items: center; gap: 0.5rem">
            <InputSwitch v-model="plan.options!.migrateComputers" /> Migrate Computers
          </label>
          <label style="display: flex; align-items: center; gap: 0.5rem">
            <InputSwitch v-model="plan.options!.preserveSidHistory" /> Preserve SID History
          </label>
          <label style="display: flex; align-items: center; gap: 0.5rem">
            <InputSwitch v-model="plan.options!.migrateGroupMemberships" /> Migrate Memberships
          </label>
          <label style="display: flex; align-items: center; gap: 0.5rem">
            <InputSwitch v-model="plan.options!.dryRun" /> Dry Run
          </label>
        </div>
        <div>
          <label>On Conflict</label>
          <Select v-model="plan.options!.onConflict" :options="conflictOptions" optionLabel="label" optionValue="value" style="width: 200px" />
        </div>
        <Button label="Preview Migration" icon="pi pi-eye" @click="runPreview" />
        <div v-if="preview" class="stat-grid">
          <div class="stat-card"><div class="stat-info"><div class="stat-value">{{ preview.users }}</div><div class="stat-label">Users</div></div></div>
          <div class="stat-card"><div class="stat-info"><div class="stat-value">{{ preview.groups }}</div><div class="stat-label">Groups</div></div></div>
          <div class="stat-card"><div class="stat-info"><div class="stat-value">{{ preview.ous }}</div><div class="stat-label">OUs</div></div></div>
          <div class="stat-card"><div class="stat-info"><div class="stat-value">{{ preview.computers }}</div><div class="stat-label">Computers</div></div></div>
        </div>
        <div v-if="preview?.warnings?.length">
          <Tag v-for="w in preview.warnings" :key="w" :value="w" severity="warn" style="margin: 0.25rem" />
        </div>
      </div>

      <!-- Step 4: Execute -->
      <div v-if="wizardStep === 4" style="display: flex; flex-direction: column; gap: 1rem">
        <h3>Step 4: Execute Migration</h3>
        <div v-if="!migrationResult">
          <p>Ready to migrate objects from <strong>{{ selectedSource?.name }}</strong>.</p>
          <Button label="Execute Migration" icon="pi pi-play" severity="success" :loading="executing" @click="runMigration" />
        </div>
        <div v-if="migrationResult">
          <Tag :value="migrationResult.status" :severity="statusSeverity(migrationResult.status)" style="margin-bottom: 1rem" />
          <ProgressBar :value="migrationResult.progressPercent" style="margin-bottom: 1rem" />
          <div class="stat-grid">
            <div class="stat-card"><div class="stat-info"><div class="stat-value">{{ migrationResult.totalProcessed }}</div><div class="stat-label">Processed</div></div></div>
            <div class="stat-card"><div class="stat-info"><div class="stat-value">{{ migrationResult.created }}</div><div class="stat-label">Created</div></div></div>
            <div class="stat-card"><div class="stat-info"><div class="stat-value">{{ migrationResult.skipped }}</div><div class="stat-label">Skipped</div></div></div>
            <div class="stat-card"><div class="stat-info"><div class="stat-value">{{ migrationResult.failed }}</div><div class="stat-label">Failed</div></div></div>
          </div>
          <div v-if="migrationResult.errors.length">
            <h4>Errors</h4>
            <ul>
              <li v-for="err in migrationResult.errors" :key="err.dn">{{ err.dn }}: {{ err.message }}</li>
            </ul>
          </div>
        </div>
      </div>

      <template #footer>
        <Button v-if="wizardStep > 1" label="Back" text @click="wizardStep--" />
        <span style="flex: 1"></span>
        <Button v-if="wizardStep < 4" label="Next" icon="pi pi-arrow-right" @click="wizardStep++" />
        <Button v-if="wizardStep === 4 && migrationResult" label="Close" @click="showWizardDialog = false" />
      </template>
    </Dialog>
  </div>
</template>
