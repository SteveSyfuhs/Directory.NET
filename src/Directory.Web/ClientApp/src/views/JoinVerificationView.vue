<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import ProgressSpinner from 'primevue/progressspinner'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import ConfirmDialog from 'primevue/confirmdialog'
import Message from 'primevue/message'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import {
  verifyDomainJoin,
  diagnoseDomainJoin,
  getDomainJoinHealth,
  repairDomainJoin,
} from '../api/joinVerification'
import type {
  JoinVerificationResult,
  JoinDiagnosticResult,
  DomainJoinHealthSummary,
  VerificationCheck,
  DiagnosticEntry,
} from '../types/joinVerification'

const toast = useToast()
const confirm = useConfirm()

// ── Verify Tab ──────────────────────────────────────────────────────
const verifyName = ref('')
const verifying = ref(false)
const verifyResult = ref<JoinVerificationResult | null>(null)

async function onVerify() {
  if (!verifyName.value.trim()) return
  verifying.value = true
  verifyResult.value = null
  try {
    verifyResult.value = await verifyDomainJoin(verifyName.value.trim())
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Verification Failed', detail: e.message, life: 5000 })
  } finally {
    verifying.value = false
  }
}

const healthBadgeSeverity = computed(() => {
  if (!verifyResult.value) return undefined
  if (verifyResult.value.overallHealthy) return 'success' as const
  const failCount = verifyResult.value.checks.filter(c => !c.passed).length
  return failCount > 3 ? ('danger' as const) : ('warn' as const)
})

const healthBadgeLabel = computed(() => {
  if (!verifyResult.value) return ''
  if (verifyResult.value.overallHealthy) return 'Healthy'
  const failCount = verifyResult.value.checks.filter(c => !c.passed).length
  return failCount > 3 ? 'Failed' : 'Issues Found'
})

// ── Diagnostics Tab ─────────────────────────────────────────────────
const diagName = ref('')
const diagnosing = ref(false)
const diagResult = ref<JoinDiagnosticResult | null>(null)

async function onDiagnose() {
  if (!diagName.value.trim()) return
  diagnosing.value = true
  diagResult.value = null
  try {
    diagResult.value = await diagnoseDomainJoin(diagName.value.trim())
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Diagnostics Failed', detail: e.message, life: 5000 })
  } finally {
    diagnosing.value = false
  }
}

function diagStatusSeverity(status: string) {
  switch (status) {
    case 'Pass': return 'success' as const
    case 'Fail': return 'danger' as const
    case 'Warning': return 'warn' as const
    case 'Skip': return 'secondary' as const
    default: return undefined
  }
}

// ── Repair ──────────────────────────────────────────────────────────
const repairing = ref(false)
const repairResult = ref<JoinVerificationResult | null>(null)

function onRepair(computerName: string) {
  confirm.require({
    message: `Auto-repair will attempt to fix common issues for "${computerName}" (re-register SPNs, re-enable account). Continue?`,
    header: 'Confirm Repair',
    icon: 'pi pi-wrench',
    acceptClass: 'p-button-warning',
    accept: async () => {
      repairing.value = true
      repairResult.value = null
      try {
        repairResult.value = await repairDomainJoin(computerName)
        toast.add({
          severity: 'success',
          summary: 'Repair Complete',
          detail: `Repair completed for ${computerName}.`,
          life: 5000,
        })
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Repair Failed', detail: e.message, life: 5000 })
      } finally {
        repairing.value = false
      }
    },
  })
}

// ── Health Dashboard Tab ────────────────────────────────────────────
const healthLoading = ref(false)
const health = ref<DomainJoinHealthSummary | null>(null)

async function loadHealth() {
  healthLoading.value = true
  try {
    health.value = await getDomainJoinHealth()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    healthLoading.value = false
  }
}

onMounted(() => loadHealth())

const failureReasonsList = computed(() => {
  if (!health.value) return []
  return Object.entries(health.value.failureReasons)
    .map(([reason, count]) => ({ reason, count }))
    .sort((a, b) => b.count - a.count)
})
</script>

<template>
  <div class="page-header">
    <h1>Domain Join Verification</h1>
    <p>Verify post-join health, run diagnostics, and auto-repair common issues.</p>
  </div>

  <ConfirmDialog />

  <TabView>
    <!-- ── Verify Tab ──────────────────────────────────────────────── -->
    <TabPanel header="Verify">
      <div class="card" style="margin-bottom: 1.5rem">
        <div class="card-title">Run Verification Checks</div>
        <div class="toolbar">
          <InputText
            v-model="verifyName"
            placeholder="Computer name (e.g. WORKSTATION01)"
            style="width: 320px"
            @keyup.enter="onVerify"
          />
          <Button
            label="Verify"
            icon="pi pi-check-circle"
            :loading="verifying"
            :disabled="!verifyName.trim()"
            @click="onVerify"
          />
        </div>
      </div>

      <!-- Verify Results -->
      <div v-if="verifying" style="text-align: center; padding: 2rem">
        <ProgressSpinner strokeWidth="3" />
        <p style="color: var(--p-text-muted-color); margin-top: 0.5rem">Running verification checks...</p>
      </div>

      <div v-if="verifyResult && !verifying" class="card">
        <div style="display: flex; align-items: center; gap: 1rem; margin-bottom: 1.25rem">
          <span style="font-weight: 700; font-size: 1.125rem">{{ verifyResult.computerName }}</span>
          <Tag :severity="healthBadgeSeverity" :value="healthBadgeLabel" />
          <span class="toolbar-spacer" />
          <Button
            label="Repair"
            icon="pi pi-wrench"
            severity="warn"
            size="small"
            :loading="repairing"
            :disabled="verifyResult.overallHealthy"
            @click="onRepair(verifyResult.computerName)"
          />
        </div>

        <DataTable :value="verifyResult.checks" stripedRows size="small">
          <Column header="Status" style="width: 80px">
            <template #body="{ data }: { data: VerificationCheck }">
              <i
                :class="data.passed ? 'pi pi-check-circle' : 'pi pi-times-circle'"
                :style="{ color: data.passed ? 'var(--app-success-text)' : 'var(--app-danger-text)', fontSize: '1.125rem' }"
              />
            </template>
          </Column>
          <Column field="name" header="Check" style="width: 180px" />
          <Column field="category" header="Category" style="width: 120px">
            <template #body="{ data }: { data: VerificationCheck }">
              <Tag :value="data.category" severity="secondary" />
            </template>
          </Column>
          <Column field="message" header="Message" />
          <Column header="Recommendation" style="min-width: 200px">
            <template #body="{ data }: { data: VerificationCheck }">
              <span v-if="data.recommendation" style="color: var(--app-warn-text); font-size: 0.875rem">
                {{ data.recommendation }}
              </span>
              <span v-else style="color: var(--p-text-muted-color)">—</span>
            </template>
          </Column>
        </DataTable>
      </div>

      <!-- Repair Results -->
      <div v-if="repairResult" class="card" style="margin-top: 1rem">
        <div class="card-title">Repair Results</div>
        <DataTable :value="repairResult.checks" stripedRows size="small">
          <Column header="Status" style="width: 80px">
            <template #body="{ data }: { data: VerificationCheck }">
              <i
                :class="data.passed ? 'pi pi-check-circle' : 'pi pi-times-circle'"
                :style="{ color: data.passed ? 'var(--app-success-text)' : 'var(--app-danger-text)', fontSize: '1.125rem' }"
              />
            </template>
          </Column>
          <Column field="name" header="Action" />
          <Column field="message" header="Details" />
        </DataTable>
      </div>
    </TabPanel>

    <!-- ── Diagnostics Tab ─────────────────────────────────────────── -->
    <TabPanel header="Diagnostics">
      <div class="card" style="margin-bottom: 1.5rem">
        <div class="card-title">Run Diagnostic Tests</div>
        <div class="toolbar">
          <InputText
            v-model="diagName"
            placeholder="Computer name (e.g. WORKSTATION01)"
            style="width: 320px"
            @keyup.enter="onDiagnose"
          />
          <Button
            label="Run Diagnostics"
            icon="pi pi-cog"
            :loading="diagnosing"
            :disabled="!diagName.trim()"
            @click="onDiagnose"
          />
        </div>
      </div>

      <div v-if="diagnosing" style="text-align: center; padding: 2rem">
        <ProgressSpinner strokeWidth="3" />
        <p style="color: var(--p-text-muted-color); margin-top: 0.5rem">Running diagnostics...</p>
      </div>

      <div v-if="diagResult && !diagnosing">
        <div class="card" style="margin-bottom: 1rem">
          <div style="display: flex; align-items: center; gap: 1rem; margin-bottom: 0.5rem">
            <span style="font-weight: 700; font-size: 1.125rem">{{ diagResult.computerName }}</span>
            <span style="color: var(--p-text-muted-color); font-size: 0.875rem">{{ diagResult.summary }}</span>
          </div>
        </div>

        <div class="card" style="margin-bottom: 1rem">
          <div class="card-title">Test Results</div>
          <DataTable :value="diagResult.entries" stripedRows size="small">
            <Column field="test" header="Test" style="width: 220px" />
            <Column header="Status" style="width: 100px">
              <template #body="{ data }: { data: DiagnosticEntry }">
                <Tag :severity="diagStatusSeverity(data.status)" :value="data.status" />
              </template>
            </Column>
            <Column field="details" header="Details" />
            <Column header="Duration" style="width: 100px">
              <template #body="{ data }: { data: DiagnosticEntry }">
                {{ data.durationMs }} ms
              </template>
            </Column>
          </DataTable>
        </div>

        <div v-if="diagResult.recommendations.length > 0" class="card">
          <div class="card-title">Recommendations</div>
          <ul style="margin: 0; padding-left: 1.25rem">
            <li
              v-for="(rec, idx) in diagResult.recommendations"
              :key="idx"
              style="margin-bottom: 0.5rem; color: var(--app-warn-text); font-size: 0.9rem"
            >
              {{ rec }}
            </li>
          </ul>
          <div style="margin-top: 1rem">
            <Button
              label="Auto-Repair"
              icon="pi pi-wrench"
              severity="warn"
              size="small"
              :loading="repairing"
              @click="onRepair(diagResult.computerName)"
            />
          </div>
        </div>
      </div>
    </TabPanel>

    <!-- ── Health Dashboard Tab ─────────────────────────────────────── -->
    <TabPanel header="Health Dashboard">
      <div v-if="healthLoading" style="text-align: center; padding: 2rem">
        <ProgressSpinner strokeWidth="3" />
      </div>

      <div v-else-if="health">
        <div class="stat-grid">
          <div class="stat-card">
            <div class="stat-icon blue"><i class="pi pi-desktop" /></div>
            <div>
              <div class="stat-value">{{ health.totalJoins }}</div>
              <div class="stat-label">Total Operations</div>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon green"><i class="pi pi-check" /></div>
            <div>
              <div class="stat-value">{{ health.successfulJoins }}</div>
              <div class="stat-label">Successful</div>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon" :class="health.failedJoins > 0 ? 'amber' : 'green'">
              <i :class="health.failedJoins > 0 ? 'pi pi-exclamation-triangle' : 'pi pi-check'" />
            </div>
            <div>
              <div class="stat-value">{{ health.failedJoins }}</div>
              <div class="stat-label">Failed</div>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon purple"><i class="pi pi-percentage" /></div>
            <div>
              <div class="stat-value">{{ health.successRate }}%</div>
              <div class="stat-label">Success Rate</div>
            </div>
          </div>
        </div>

        <!-- Common Failures -->
        <div v-if="failureReasonsList.length > 0" class="card" style="margin-bottom: 1.5rem">
          <div class="card-title">Common Failure Reasons</div>
          <DataTable :value="failureReasonsList" stripedRows size="small">
            <Column field="reason" header="Reason" />
            <Column field="count" header="Count" style="width: 100px" />
          </DataTable>
        </div>

        <!-- Recent Operations -->
        <div class="card" style="margin-bottom: 1.5rem">
          <div class="card-title">Recent Operations</div>
          <DataTable :value="health.recentOperations" stripedRows size="small" :rows="10" paginator>
            <Column header="Time" style="width: 180px">
              <template #body="{ data }">
                {{ new Date(data.timestamp).toLocaleString() }}
              </template>
            </Column>
            <Column field="operation" header="Operation" style="width: 100px">
              <template #body="{ data }">
                <Tag :value="data.operation" severity="secondary" />
              </template>
            </Column>
            <Column field="computerName" header="Computer" />
            <Column header="Status" style="width: 100px">
              <template #body="{ data }">
                <Tag :severity="data.success ? 'success' : 'danger'" :value="data.success ? 'Success' : 'Failed'" />
              </template>
            </Column>
            <Column field="operator" header="Operator" />
            <Column header="Error" style="min-width: 200px">
              <template #body="{ data }">
                <span v-if="data.errorMessage" style="color: var(--app-danger-text); font-size: 0.875rem">
                  {{ data.errorMessage }}
                </span>
                <span v-else style="color: var(--p-text-muted-color)">—</span>
              </template>
            </Column>
          </DataTable>
        </div>

        <div style="text-align: right">
          <Button label="Refresh" icon="pi pi-refresh" text size="small" @click="loadHealth" />
        </div>
      </div>

      <Message v-else severity="info">No domain join health data available yet.</Message>
    </TabPanel>
  </TabView>
</template>
