<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Card from 'primevue/card'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import ProgressSpinner from 'primevue/progressspinner'
import ProgressBar from 'primevue/progressbar'
import { useToast } from 'primevue/usetoast'
import { exportToCsv } from '../composables/useExport'
import {
  fetchComplianceReports,
  fetchComplianceDashboard,
  runComplianceReport,
  fetchReportResult,
  createCustomReport,
} from '../api/compliance'
import type {
  ComplianceReport,
  ComplianceDashboard,
  ReportResult,
  ComplianceRecommendation,
} from '../api/compliance'

const toast = useToast()

const loading = ref(true)
const reports = ref<ComplianceReport[]>([])
const dashboard = ref<ComplianceDashboard | null>(null)
const activeResult = ref<ReportResult | null>(null)
const activeReport = ref<ComplianceReport | null>(null)
const showResultDialog = ref(false)
const showCreateDialog = ref(false)
const runningReportId = ref<string | null>(null)

const newReport = ref({
  name: '',
  description: '',
  category: 'Security',
  customFilter: '',
})

const categoryOptions = [
  { label: 'Security', value: 'Security' },
  { label: 'SOX', value: 'SOX' },
  { label: 'HIPAA', value: 'HIPAA' },
  { label: 'GDPR', value: 'GDPR' },
  { label: 'Operational', value: 'Operational' },
]

onMounted(async () => {
  await loadData()
})

async function loadData() {
  loading.value = true
  try {
    const [reportsData, dashboardData] = await Promise.all([
      fetchComplianceReports(),
      fetchComplianceDashboard(),
    ])
    reports.value = reportsData
    dashboard.value = dashboardData
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

async function handleRunReport(report: ComplianceReport) {
  runningReportId.value = report.id
  try {
    const result = await runComplianceReport(report.id)
    activeResult.value = result
    activeReport.value = report
    showResultDialog.value = true
    toast.add({ severity: 'success', summary: 'Report Complete', detail: `${report.name}: ${result.complianceStatus}`, life: 3000 })
    await loadData()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    runningReportId.value = null
  }
}

async function handleViewResult(report: ComplianceReport) {
  try {
    const result = await fetchReportResult(report.id)
    activeResult.value = result
    activeReport.value = report
    showResultDialog.value = true
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: 'No results available. Run the report first.', life: 5000 })
  }
}

function handleExportCsv() {
  if (!activeResult.value) return
  const columns = activeResult.value.columns.map(c => ({ field: c, header: c }))
  exportToCsv(activeResult.value.data, columns, `compliance-${activeResult.value.reportId}`)
}

async function handleCreateReport() {
  try {
    await createCustomReport({
      name: newReport.value.name,
      description: newReport.value.description,
      category: newReport.value.category,
      customFilter: newReport.value.customFilter,
    })
    showCreateDialog.value = false
    newReport.value = { name: '', description: '', category: 'Security', customFilter: '' }
    toast.add({ severity: 'success', summary: 'Created', detail: 'Custom report created', life: 3000 })
    await loadData()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  }
}

function statusSeverity(status: string): string {
  switch (status) {
    case 'Compliant': return 'success'
    case 'NonCompliant': return 'danger'
    case 'Warning': return 'warn'
    case 'Error': return 'danger'
    default: return 'info'
  }
}

function severityColor(severity: string): string {
  switch (severity) {
    case 'Critical': return 'danger'
    case 'High': return 'danger'
    case 'Medium': return 'warn'
    case 'Low': return 'info'
    default: return 'secondary'
  }
}

const scoreColor = computed(() => {
  if (!dashboard.value) return ''
  const s = dashboard.value.complianceScore
  if (s >= 80) return 'color: var(--p-green-500)'
  if (s >= 50) return 'color: var(--p-yellow-500)'
  return 'color: var(--p-red-500)'
})
</script>

<template>
  <div class="compliance-view">
    <div class="page-header">
      <h1><i class="pi pi-chart-bar"></i> Compliance Reporting</h1>
      <Button label="Create Custom Report" icon="pi pi-plus" @click="showCreateDialog = true" />
    </div>

    <ProgressSpinner v-if="loading" strokeWidth="3" />

    <template v-else>
      <!-- Dashboard Cards -->
      <div v-if="dashboard" class="dashboard-grid">
        <Card class="dash-card score-card">
          <template #content>
            <div class="dash-stat">
              <span class="dash-value" :style="scoreColor">{{ dashboard.complianceScore }}%</span>
              <span class="dash-label">Compliance Score</span>
            </div>
          </template>
        </Card>
        <Card class="dash-card">
          <template #content>
            <div class="dash-stat">
              <span class="dash-value" style="color: var(--p-red-500)">{{ dashboard.criticalFindings.length }}</span>
              <span class="dash-label">Critical Findings</span>
            </div>
          </template>
        </Card>
        <Card class="dash-card">
          <template #content>
            <div class="dash-stat">
              <span class="dash-value" style="color: var(--p-green-500)">{{ dashboard.compliantCount }}</span>
              <span class="dash-label">Compliant</span>
            </div>
          </template>
        </Card>
        <Card class="dash-card">
          <template #content>
            <div class="dash-stat">
              <span class="dash-value" style="color: var(--p-orange-500)">{{ dashboard.nonCompliantCount + dashboard.warningCount }}</span>
              <span class="dash-label">Non-Compliant</span>
            </div>
          </template>
        </Card>
        <Card class="dash-card">
          <template #content>
            <div class="dash-stat">
              <span class="dash-value">{{ dashboard.totalReports }}</span>
              <span class="dash-label">Total Reports</span>
            </div>
          </template>
        </Card>
      </div>

      <!-- Critical Findings -->
      <div v-if="dashboard && dashboard.criticalFindings.length > 0" class="section">
        <h2>Critical Findings</h2>
        <div class="findings-list">
          <div v-for="(finding, idx) in dashboard.criticalFindings" :key="idx" class="finding-item">
            <Tag :severity="severityColor(finding.severity)" :value="finding.severity" />
            <div class="finding-content">
              <strong>{{ finding.title }}</strong>
              <p>{{ finding.description }}</p>
              <p v-if="finding.remediationAction" class="remediation"><i class="pi pi-wrench"></i> {{ finding.remediationAction }}</p>
            </div>
          </div>
        </div>
      </div>

      <!-- Reports Table -->
      <div class="section">
        <h2>Compliance Reports</h2>
        <DataTable :value="reports" stripedRows responsiveLayout="scroll" :rowHover="true">
          <Column field="name" header="Report Name" sortable />
          <Column field="category" header="Category" sortable>
            <template #body="{ data }">
              <Tag :value="data.category" severity="info" />
            </template>
          </Column>
          <Column field="type" header="Type" sortable />
          <Column field="lastRunStatus" header="Status" sortable>
            <template #body="{ data }">
              <Tag v-if="data.lastRunStatus" :value="data.lastRunStatus" :severity="statusSeverity(data.lastRunStatus)" />
              <span v-else class="text-muted">Not Run</span>
            </template>
          </Column>
          <Column field="lastRunAt" header="Last Run" sortable>
            <template #body="{ data }">
              {{ data.lastRunAt ? new Date(data.lastRunAt).toLocaleString() : '--' }}
            </template>
          </Column>
          <Column header="Actions" :style="{ width: '220px' }">
            <template #body="{ data }">
              <div class="action-buttons">
                <Button
                  icon="pi pi-play"
                  size="small"
                  severity="success"
                  :loading="runningReportId === data.id"
                  v-tooltip="'Run Report'"
                  @click="handleRunReport(data)"
                />
                <Button
                  icon="pi pi-eye"
                  size="small"
                  severity="info"
                  :disabled="!data.lastRunAt"
                  v-tooltip="'View Results'"
                  @click="handleViewResult(data)"
                />
              </div>
            </template>
          </Column>
        </DataTable>
      </div>
    </template>

    <!-- Result Dialog -->
    <Dialog
      v-model:visible="showResultDialog"
      :header="activeReport?.name ?? 'Report Result'"
      :modal="true"
      :style="{ width: '90vw', maxWidth: '1200px' }"
      :maximizable="true"
    >
      <template v-if="activeResult">
        <div class="result-summary">
          <Tag :value="activeResult.complianceStatus" :severity="statusSeverity(activeResult.complianceStatus)" />
          <span>Total Items: {{ activeResult.totalItems }}</span>
          <span>Flagged Items: {{ activeResult.flaggedItems }}</span>
          <span>Generated: {{ new Date(activeResult.generatedAt).toLocaleString() }}</span>
          <Button label="Export CSV" icon="pi pi-download" size="small" @click="handleExportCsv" />
        </div>

        <!-- Recommendations -->
        <div v-if="activeResult.recommendations.length > 0" class="recommendations-panel">
          <h3>Recommendations</h3>
          <div v-for="(rec, idx) in activeResult.recommendations" :key="idx" class="rec-item">
            <Tag :value="rec.severity" :severity="severityColor(rec.severity)" />
            <div>
              <strong>{{ rec.title }}</strong>
              <p>{{ rec.description }}</p>
              <p v-if="rec.remediationAction" class="remediation"><i class="pi pi-wrench"></i> {{ rec.remediationAction }}</p>
            </div>
          </div>
        </div>

        <!-- Data Table -->
        <DataTable
          :value="activeResult.data"
          stripedRows
          :paginator="activeResult.data.length > 20"
          :rows="20"
          responsiveLayout="scroll"
          class="result-table"
        >
          <Column v-for="col in activeResult.columns" :key="col" :field="col" :header="col" sortable />
        </DataTable>
      </template>
    </Dialog>

    <!-- Create Custom Report Dialog -->
    <Dialog
      v-model:visible="showCreateDialog"
      header="Create Custom Report"
      :modal="true"
      :style="{ width: '500px' }"
    >
      <div class="form-grid">
        <div class="form-field">
          <label>Name</label>
          <InputText v-model="newReport.name" class="w-full" />
        </div>
        <div class="form-field">
          <label>Category</label>
          <Select v-model="newReport.category" :options="categoryOptions" optionLabel="label" optionValue="value" class="w-full" />
        </div>
        <div class="form-field">
          <label>Description</label>
          <Textarea v-model="newReport.description" rows="3" class="w-full" />
        </div>
        <div class="form-field">
          <label>LDAP Filter (optional)</label>
          <InputText v-model="newReport.customFilter" class="w-full" placeholder="(&(objectClass=user)(department=IT))" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" @click="showCreateDialog = false" />
        <Button label="Create" icon="pi pi-check" :disabled="!newReport.name" @click="handleCreateReport" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.compliance-view {
  padding: 1.5rem;
}
.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1.5rem;
}
.page-header h1 {
  margin: 0;
  font-size: 1.5rem;
}
.dashboard-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}
.dash-card {
  text-align: center;
}
.dash-stat {
  display: flex;
  flex-direction: column;
  align-items: center;
}
.dash-value {
  font-size: 2rem;
  font-weight: 700;
}
.dash-label {
  font-size: 0.85rem;
  color: var(--p-text-muted-color);
  margin-top: 0.25rem;
}
.section {
  margin-bottom: 1.5rem;
}
.section h2 {
  margin: 0 0 0.75rem 0;
  font-size: 1.15rem;
}
.action-buttons {
  display: flex;
  gap: 0.5rem;
}
.result-summary {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 1rem;
  flex-wrap: wrap;
}
.recommendations-panel {
  background: var(--p-surface-50);
  border-radius: 6px;
  padding: 1rem;
  margin-bottom: 1rem;
}
.recommendations-panel h3 {
  margin: 0 0 0.75rem 0;
  font-size: 1rem;
}
.rec-item {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
  margin-bottom: 0.75rem;
}
.rec-item p {
  margin: 0.25rem 0 0 0;
  font-size: 0.9rem;
}
.remediation {
  color: var(--p-primary-color);
  font-style: italic;
}
.findings-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}
.finding-item {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
  padding: 0.75rem;
  background: var(--p-surface-50);
  border-radius: 6px;
}
.finding-content p {
  margin: 0.25rem 0 0 0;
  font-size: 0.9rem;
}
.result-table {
  margin-top: 1rem;
}
.form-grid {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.form-field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}
.form-field label {
  font-weight: 600;
  font-size: 0.85rem;
}
.text-muted {
  color: var(--p-text-muted-color);
  font-style: italic;
}
</style>
