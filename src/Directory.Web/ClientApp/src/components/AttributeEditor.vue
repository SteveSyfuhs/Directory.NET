<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Textarea from 'primevue/textarea'
import Dropdown from 'primevue/dropdown'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import ProgressSpinner from 'primevue/progressspinner'
import ToggleSwitch from 'primevue/toggleswitch'
import DatePicker from 'primevue/datepicker'
import Tooltip from 'primevue/tooltip'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import ConfirmDialog from 'primevue/confirmdialog'
import { fetchAttributes, setAttribute, clearAttribute, addAttributeValues, removeAttributeValues } from '../api/attributes'
import type { FormattedAttribute, FormattedValue } from '../types/attributes'
import { KNOWN_FLAG_ATTRIBUTES } from '../types/attributes'
import HexViewer from './HexViewer.vue'
import DateTimeDisplay from './DateTimeDisplay.vue'
import SidDisplay from './SidDisplay.vue'
import FlagEditor from './FlagEditor.vue'
import DnPicker from './DnPicker.vue'

const vTooltip = Tooltip

const props = defineProps<{
  /** Distinguished name of the object */
  dn: string
}>()

const toast = useToast()
const confirm = useConfirm()

// State
const attributes = ref<FormattedAttribute[]>([])
const loading = ref(true)
const saving = ref(false)
const filterMode = ref<string>('all')
const searchText = ref('')
const showAllSchema = ref(true)

// Edit dialog state
const editDialogVisible = ref(false)
const editAttr = ref<FormattedAttribute | null>(null)
const editValues = ref<string[]>([])
const editSingleValue = ref<string>('')
const editBoolValue = ref<boolean>(true)
const editIntValue = ref<number>(0)
const editNewMultiValue = ref<string>('')
const editDnValue = ref<string>('')
const editDateValue = ref<Date | null>(null)

// Flag editor state
const flagEditorVisible = ref(false)
const flagEditAttr = ref<FormattedAttribute | null>(null)

// Multi-value expanded rows tracker
const expandedMultiValues = ref<Set<string>>(new Set())

const filterOptions = [
  { label: 'All Attributes', value: 'all' },
  { label: 'Set (non-empty)', value: 'set' },
  { label: 'Writable Only', value: 'writable' },
  { label: 'Backlinks Only', value: 'backlink' },
]

// Load attributes
async function loadAttributes() {
  loading.value = true
  try {
    // When filter is "set" or "backlink", never include unset attributes
    // When filter is "all" or "writable", respect the showAll toggle
    const effectiveShowAll = (filterMode.value === 'set' || filterMode.value === 'backlink')
      ? false
      : showAllSchema.value
    const data = await fetchAttributes(props.dn, filterMode.value, effectiveShowAll)
    attributes.value = data
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error loading attributes', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

watch(() => props.dn, () => {
  if (props.dn) loadAttributes()
})

watch(filterMode, () => {
  loadAttributes()
})

watch(showAllSchema, () => {
  loadAttributes()
})

onMounted(() => {
  if (props.dn) loadAttributes()
})

// Filtered and searched attributes
const filteredAttributes = computed(() => {
  if (!searchText.value) return attributes.value
  const q = searchText.value.toLowerCase()
  return attributes.value.filter(a =>
    a.name.toLowerCase().includes(q) ||
    a.syntaxName.toLowerCase().includes(q) ||
    (a.description?.toLowerCase().includes(q))
  )
})

const attributeCount = computed(() => ({
  showing: filteredAttributes.value.length,
  total: attributes.value.length,
  set: attributes.value.filter(a => a.isValueSet).length,
}))

// Value display helpers
function primaryDisplayValue(attr: FormattedAttribute): string {
  if (attr.values.length === 0) return '<not set>'
  return attr.values[0].displayValue
}

function extraValueCount(attr: FormattedAttribute): number {
  if (attr.values.length <= 1) return 0
  return attr.values.length - 1
}

function isExpanded(attrName: string): boolean {
  return expandedMultiValues.value.has(attrName)
}

function toggleExpand(attrName: string) {
  if (expandedMultiValues.value.has(attrName)) {
    expandedMultiValues.value.delete(attrName)
  } else {
    expandedMultiValues.value.add(attrName)
  }
}

/** Decode flag integer to comma-separated flag names */
function decodeFlagNames(attr: FormattedAttribute): string {
  const flagDefs = KNOWN_FLAG_ATTRIBUTES[attr.name]
  if (!flagDefs || attr.values.length === 0) return ''
  const val = parseInt(attr.values[0].rawValue)
  if (isNaN(val)) return ''
  const names: string[] = []
  for (const [bit, name] of Object.entries(flagDefs)) {
    if (val & Number(bit)) names.push(name)
  }
  return names.join(', ')
}

function flagNamesArray(attr: FormattedAttribute): string[] {
  const flagDefs = KNOWN_FLAG_ATTRIBUTES[attr.name]
  if (!flagDefs || attr.values.length === 0) return []
  const val = parseInt(attr.values[0].rawValue)
  if (isNaN(val)) return []
  const names: string[] = []
  for (const [bit, name] of Object.entries(flagDefs)) {
    if (val & Number(bit)) names.push(name)
  }
  return names
}

/** Build tooltip text for schema info */
function schemaTooltip(attr: FormattedAttribute): string {
  const parts: string[] = []
  parts.push(`LDAP Name: ${attr.name}`)
  parts.push(`Syntax: ${attr.syntaxName} (${attr.syntaxOid})`)
  parts.push(`Single-Valued: ${!attr.isMultiValued ? 'Yes' : 'No'}`)
  if (attr.description) parts.push(`Description: ${attr.description}`)
  if (attr.isIndexed) parts.push('Indexed: Yes')
  if (attr.isInGlobalCatalog) parts.push('Global Catalog: Yes')
  if (attr.rangeLower != null) parts.push(`Range Lower: ${attr.rangeLower}`)
  if (attr.rangeUpper != null) parts.push(`Range Upper: ${attr.rangeUpper}`)
  if (attr.isConstructed) parts.push('Constructed (computed)')
  if (attr.isSystemOnly) parts.push('System-Only')
  return parts.join('\n')
}

/** Determine if an attribute uses a long-text editor */
function isLongTextAttribute(name: string): boolean {
  const lower = name.toLowerCase()
  return lower === 'description' || lower === 'info' || lower === 'comment' ||
         lower === 'admindescription' || lower === 'streetaddress'
}

/** Determine if an attribute is an email field */
function isEmailAttribute(name: string): boolean {
  const lower = name.toLowerCase()
  return lower === 'mail' || lower === 'othermailbox' || lower === 'proxyaddresses'
}

/** Determine if an attribute is a URL field */
function isUrlAttribute(name: string): boolean {
  const lower = name.toLowerCase()
  return lower === 'url' || lower === 'wwwhomepage' || lower === 'wbempath'
}

// Edit handlers
function openEditor(attr: FormattedAttribute) {
  editAttr.value = attr

  if (attr.displayType === 'flags' && KNOWN_FLAG_ATTRIBUTES[attr.name]) {
    flagEditAttr.value = attr
    flagEditorVisible.value = true
    return
  }

  const hasValues = attr.values.length > 0

  if (attr.isMultiValued) {
    editValues.value = hasValues ? attr.values.map(v => v.displayValue) : []
    editNewMultiValue.value = ''
  } else if (attr.displayType === 'bool') {
    editBoolValue.value = hasValues ? attr.values[0].displayValue === 'TRUE' : false
  } else if (attr.displayType === 'int') {
    editIntValue.value = hasValues ? (parseInt(attr.values[0].rawValue) || 0) : 0
  } else if (attr.displayType === 'dn') {
    editDnValue.value = hasValues ? (attr.values[0].rawValue?.toString() ?? '') : ''
  } else if (attr.displayType === 'datetime') {
    editSingleValue.value = hasValues ? attr.values[0].displayValue : ''
    if (hasValues) {
      const d = new Date(attr.values[0].displayValue.replace(' UTC', 'Z'))
      editDateValue.value = isNaN(d.getTime()) ? null : d
    } else {
      editDateValue.value = null
    }
  } else {
    editSingleValue.value = hasValues ? attr.values[0].displayValue : ''
  }

  editDialogVisible.value = true
}

function addMultiValue() {
  const v = editNewMultiValue.value.trim()
  if (v && !editValues.value.includes(v)) {
    editValues.value.push(v)
    editNewMultiValue.value = ''
  }
}

function removeMultiValue(index: number) {
  editValues.value.splice(index, 1)
}

async function saveEdit() {
  if (!editAttr.value) return
  saving.value = true
  try {
    const attr = editAttr.value
    let values: any[]

    if (attr.isMultiValued) {
      values = editValues.value.filter(v => v.trim() !== '')
    } else if (attr.displayType === 'bool') {
      values = [editBoolValue.value ? 'TRUE' : 'FALSE']
    } else if (attr.displayType === 'int') {
      values = [editIntValue.value]
    } else if (attr.displayType === 'dn' && !attr.isMultiValued) {
      values = editDnValue.value.trim() ? [editDnValue.value.trim()] : []
    } else if (attr.displayType === 'datetime' && editDateValue.value) {
      // Convert date back to generalized time or FILETIME string
      values = [editDateValue.value.toISOString()]
    } else {
      values = editSingleValue.value.trim() ? [editSingleValue.value.trim()] : []
    }

    await setAttribute(props.dn, attr.name, values)
    toast.add({ severity: 'success', summary: 'Saved', detail: `${attr.name} updated`, life: 3000 })
    editDialogVisible.value = false
    await loadAttributes()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error saving', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

async function onFlagSave(value: number) {
  if (!flagEditAttr.value) return
  saving.value = true
  try {
    await setAttribute(props.dn, flagEditAttr.value.name, [value])
    toast.add({ severity: 'success', summary: 'Saved', detail: `${flagEditAttr.value.name} updated`, life: 3000 })
    await loadAttributes()
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error saving', detail: e.message, life: 5000 })
  } finally {
    saving.value = false
  }
}

function confirmClear(attr: FormattedAttribute) {
  confirm.require({
    message: `Are you sure you want to clear all values from "${attr.name}"? This action cannot be undone.`,
    header: 'Clear Attribute',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Clear',
    acceptClass: 'p-button-danger',
    accept: async () => {
      saving.value = true
      try {
        await clearAttribute(props.dn, attr.name)
        toast.add({ severity: 'success', summary: 'Cleared', detail: `${attr.name} cleared`, life: 3000 })
        await loadAttributes()
      } catch (e: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
      } finally {
        saving.value = false
      }
    },
  })
}

/** Build the edit dialog title */
const editDialogTitle = computed(() => {
  if (!editAttr.value) return 'Edit Attribute'
  const action = editAttr.value.isValueSet ? 'Edit' : 'Set Value'
  const suffix = editAttr.value.isMultiValued ? ' (multi-valued)' : ''
  return `${action}: ${editAttr.value.name}${suffix}`
})

/** Whether the current edit attribute should use a textarea */
const useLargeInput = computed(() => {
  if (!editAttr.value || editAttr.value.isMultiValued) return false
  if (editAttr.value.displayType !== 'string' && editAttr.value.displayType !== 'dn') return false
  return editSingleValue.value.length > 100 || isLongTextAttribute(editAttr.value.name)
})

/** Email validation */
const emailError = computed(() => {
  if (!editAttr.value || !isEmailAttribute(editAttr.value.name)) return ''
  if (!editSingleValue.value) return ''
  const re = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
  return re.test(editSingleValue.value) ? '' : 'Invalid email format'
})

/** URL validation */
const urlError = computed(() => {
  if (!editAttr.value || !isUrlAttribute(editAttr.value.name)) return ''
  if (!editSingleValue.value) return ''
  try {
    new URL(editSingleValue.value)
    return ''
  } catch {
    return 'Invalid URL format'
  }
})

/** Range validation for int fields */
const rangeError = computed(() => {
  if (!editAttr.value || editAttr.value.displayType !== 'int') return ''
  if (editAttr.value.rangeLower != null && editIntValue.value < editAttr.value.rangeLower) {
    return `Value must be >= ${editAttr.value.rangeLower}`
  }
  if (editAttr.value.rangeUpper != null && editIntValue.value > editAttr.value.rangeUpper) {
    return `Value must be <= ${editAttr.value.rangeUpper}`
  }
  return ''
})
</script>

<template>
  <div class="attribute-editor">
    <ConfirmDialog />

    <!-- Toolbar -->
    <div class="toolbar">
      <Dropdown
        v-model="filterMode"
        :options="filterOptions"
        optionLabel="label"
        optionValue="value"
        size="small"
        style="width: 180px"
      />
      <InputText
        v-model="searchText"
        placeholder="Search attributes..."
        size="small"
        style="width: 250px"
      />
      <label class="show-all-toggle">
        <input type="checkbox" v-model="showAllSchema" />
        <span>Show all schema attributes</span>
      </label>
      <div class="toolbar-spacer" />
      <Button
        icon="pi pi-refresh"
        text
        rounded
        size="small"
        @click="loadAttributes"
        :loading="loading"
        title="Refresh attributes"
      />
      <span class="attr-count">
        Showing {{ attributeCount.showing }} of {{ attributeCount.total }} attributes ({{ attributeCount.set }} set)
      </span>
    </div>

    <!-- Loading -->
    <div v-if="loading" style="text-align: center; padding: 3rem">
      <ProgressSpinner />
    </div>

    <!-- Data Table -->
    <DataTable
      v-else
      :value="filteredAttributes"
      stripedRows
      size="small"
      scrollable
      scrollHeight="flex"
      sortField="name"
      :sortOrder="1"
      :paginator="filteredAttributes.length > 100"
      :rows="100"
      :rowsPerPageOptions="[50, 100, 250, 500]"
      dataKey="name"
      class="attr-table"
      :rowClass="(data: FormattedAttribute) => ({ 'attr-row-unset': !data.isValueSet })"
    >
      <!-- Attribute Name Column -->
      <Column field="name" header="Attribute" sortable style="min-width: 220px; max-width: 280px">
        <template #body="{ data }: { data: FormattedAttribute }">
          <div class="attr-name-cell" v-tooltip.top="schemaTooltip(data)">
            <span class="attr-name" :class="{ 'attr-name-unset': !data.isValueSet }">
              <span v-if="data.isMustContain" class="required-marker">*</span>
              {{ data.name }}
            </span>
            <div class="attr-badges">
              <Tag v-if="data.isConstructed" value="Constructed" severity="secondary" class="attr-badge" />
              <Tag v-if="data.isSystemOnly" value="System" severity="secondary" class="attr-badge" />
              <Tag v-if="!data.isWritable" value="Read-only" severity="warn" class="attr-badge" />
              <Tag v-if="data.isMustContain" value="Required" severity="danger" class="attr-badge" />
            </div>
          </div>
        </template>
      </Column>

      <!-- Syntax Column -->
      <Column field="syntaxName" header="Syntax" sortable style="width: 150px">
        <template #body="{ data }: { data: FormattedAttribute }">
          <span class="syntax-text">{{ data.syntaxName }}</span>
        </template>
      </Column>

      <!-- Value(s) Column -->
      <Column header="Value(s)" style="min-width: 300px">
        <template #body="{ data }: { data: FormattedAttribute }">
          <div class="value-cell">
            <!-- Unset attribute -->
            <span v-if="data.values.length === 0 && !data.isValueSet" class="value-not-set">&lt;not set&gt;</span>

            <!-- Empty (set but no values) -->
            <span v-else-if="data.values.length === 0" class="value-empty">&lt;not set&gt;</span>

            <!-- Boolean -->
            <template v-else-if="data.displayType === 'bool'">
              <Tag
                :value="data.values[0].displayValue"
                :severity="data.values[0].displayValue === 'TRUE' ? 'success' : 'danger'"
              />
            </template>

            <!-- DN -->
            <template v-else-if="data.displayType === 'dn'">
              <div v-for="(val, i) in (isExpanded(data.name) ? data.values : data.values.slice(0, 1))" :key="i" class="dn-value">
                <router-link :to="{ path: '/browse', query: { dn: val.rawValue } }" class="dn-link">
                  <i class="pi pi-link" style="font-size: 0.6875rem"></i>
                  {{ val.resolvedName || val.displayValue }}
                </router-link>
              </div>
              <Button
                v-if="extraValueCount(data) > 0 && !isExpanded(data.name)"
                :label="`+${extraValueCount(data)} more`"
                text
                size="small"
                @click="toggleExpand(data.name)"
                class="expand-btn"
              />
              <Button
                v-if="isExpanded(data.name) && data.values.length > 1"
                label="Show less"
                text
                size="small"
                @click="toggleExpand(data.name)"
                class="expand-btn"
              />
            </template>

            <!-- SID -->
            <template v-else-if="data.displayType === 'sid'">
              <SidDisplay
                :displayValue="data.values[0].displayValue"
                :resolvedName="data.values[0].resolvedName"
              />
            </template>

            <!-- DateTime -->
            <template v-else-if="data.displayType === 'datetime'">
              <DateTimeDisplay
                :displayValue="data.values[0].displayValue"
                :rawValue="data.values[0].rawValue"
              />
            </template>

            <!-- GUID -->
            <template v-else-if="data.displayType === 'guid'">
              <span class="guid-value">{{ data.values[0].displayValue }}</span>
            </template>

            <!-- Hex / Octet String -->
            <template v-else-if="data.displayType === 'hex'">
              <HexViewer :value="data.values[0].displayValue" />
            </template>

            <!-- Flags -->
            <template v-else-if="data.displayType === 'flags'">
              <div class="flags-display">
                <span class="flags-int">{{ data.values[0].displayValue }}</span>
                <div v-if="flagNamesArray(data).length > 0" class="flags-tags">
                  <Tag
                    v-for="flag in flagNamesArray(data)"
                    :key="flag"
                    :value="flag"
                    severity="info"
                    class="flag-tag"
                  />
                </div>
              </div>
            </template>

            <!-- Interval -->
            <template v-else-if="data.displayType === 'interval'">
              <span class="interval-value">{{ data.values[0].displayValue }}</span>
            </template>

            <!-- Security Descriptor -->
            <template v-else-if="data.displayType === 'security_descriptor'">
              <Button label="View Security..." icon="pi pi-shield" text size="small" disabled />
            </template>

            <!-- String / Default -->
            <template v-else>
              <div class="string-values">
                <template v-for="(val, i) in (isExpanded(data.name) ? data.values : data.values.slice(0, 1))" :key="i">
                  <span class="string-value">{{ val.displayValue }}</span>
                </template>
                <Button
                  v-if="extraValueCount(data) > 0 && !isExpanded(data.name)"
                  :label="`+${extraValueCount(data)} more`"
                  text
                  size="small"
                  @click="toggleExpand(data.name)"
                  class="expand-btn"
                />
                <Button
                  v-if="isExpanded(data.name) && data.values.length > 1"
                  label="Show less"
                  text
                  size="small"
                  @click="toggleExpand(data.name)"
                  class="expand-btn"
                />
              </div>
            </template>
          </div>
        </template>
      </Column>

      <!-- Actions Column -->
      <Column header="Actions" style="width: 160px" :exportable="false">
        <template #body="{ data }: { data: FormattedAttribute }">
          <div class="action-buttons">
            <Button
              v-if="data.isWritable && data.isValueSet"
              icon="pi pi-pencil"
              text
              rounded
              size="small"
              @click="openEditor(data)"
              title="Edit attribute"
            />
            <Button
              v-if="data.isWritable && !data.isValueSet"
              label="Set Value"
              icon="pi pi-plus"
              text
              size="small"
              @click="openEditor(data)"
              title="Set attribute value"
              class="set-value-btn"
            />
            <Button
              v-if="data.isWritable && !data.isSystemOnly && data.values.length > 0"
              icon="pi pi-times"
              text
              rounded
              size="small"
              severity="danger"
              @click="confirmClear(data)"
              title="Clear attribute"
            />
          </div>
        </template>
      </Column>

      <template #empty>
        <div style="text-align: center; padding: 2rem; color: var(--p-text-muted-color)">
          <i class="pi pi-info-circle" style="font-size: 1.5rem; display: block; margin-bottom: 0.5rem"></i>
          No attributes found matching the current filter
        </div>
      </template>
    </DataTable>

    <!-- Edit Dialog -->
    <Dialog
      v-model:visible="editDialogVisible"
      :header="editDialogTitle"
      modal
      :style="{ width: '520px' }"
      :closable="true"
    >
      <template v-if="editAttr">
        <!-- Schema info bar -->
        <div v-if="editAttr.description || !editAttr.isValueSet" class="edit-schema-info">
          <i class="pi pi-info-circle"></i>
          <div style="display: flex; flex-direction: column; gap: 0.25rem">
            <span v-if="editAttr.description">{{ editAttr.description }}</span>
            <span v-if="!editAttr.isValueSet">Syntax: {{ editAttr.syntaxName }} ({{ editAttr.syntaxOid }})</span>
          </div>
        </div>

        <!-- Multi-valued editor -->
        <div v-if="editAttr.isMultiValued" class="multi-value-editor">
          <div class="multi-value-list">
            <div v-if="editValues.length === 0" class="multi-value-empty">No values</div>
            <div v-for="(val, i) in editValues" :key="i" class="multi-value-item">
              <span class="multi-value-text">{{ val }}</span>
              <Button
                icon="pi pi-times"
                text
                rounded
                size="small"
                severity="danger"
                @click="removeMultiValue(i)"
              />
            </div>
          </div>
          <div class="multi-value-add">
            <InputText
              v-model="editNewMultiValue"
              :placeholder="`Add ${editAttr.displayType === 'dn' ? 'DN' : 'value'}...`"
              size="small"
              style="flex: 1"
              @keydown.enter="addMultiValue"
            />
            <Button
              icon="pi pi-plus"
              size="small"
              @click="addMultiValue"
              :disabled="!editNewMultiValue.trim()"
            />
          </div>
        </div>

        <!-- Bool editor (toggle switch) -->
        <div v-else-if="editAttr.displayType === 'bool'" class="single-editor">
          <label class="edit-label">Value</label>
          <div class="bool-toggle-row">
            <ToggleSwitch v-model="editBoolValue" />
            <span class="bool-toggle-label">{{ editBoolValue ? 'TRUE' : 'FALSE' }}</span>
          </div>
        </div>

        <!-- Int editor with range constraints -->
        <div v-else-if="editAttr.displayType === 'int'" class="single-editor">
          <label class="edit-label">Value</label>
          <InputNumber
            v-model="editIntValue"
            :useGrouping="false"
            :min="editAttr.rangeLower ?? undefined"
            :max="editAttr.rangeUpper ?? undefined"
            style="width: 100%"
          />
          <small v-if="editAttr.rangeLower != null || editAttr.rangeUpper != null" class="edit-hint">
            Range: {{ editAttr.rangeLower ?? '...' }} to {{ editAttr.rangeUpper ?? '...' }}
          </small>
          <small v-if="rangeError" class="edit-error">{{ rangeError }}</small>
        </div>

        <!-- DateTime editor -->
        <div v-else-if="editAttr.displayType === 'datetime'" class="single-editor">
          <div class="readonly-notice">
            <i class="pi pi-info-circle"></i>
            DateTime attributes are typically system-managed. Edit with caution.
          </div>
          <label class="edit-label">Date/Time</label>
          <DatePicker
            v-model="editDateValue"
            showTime
            hourFormat="24"
            :showSeconds="true"
            style="width: 100%"
          />
          <label class="edit-label" style="margin-top: 0.5rem">Raw Value</label>
          <InputText v-model="editSingleValue" style="width: 100%" />
        </div>

        <!-- DN editor (single-valued) -->
        <div v-else-if="editAttr.displayType === 'dn' && !editAttr.isMultiValued" class="single-editor">
          <label class="edit-label">Distinguished Name</label>
          <DnPicker v-model="editDnValue" label="Target Object" />
        </div>

        <!-- String editor with email/URL validation -->
        <div v-else class="single-editor">
          <label class="edit-label">Value</label>
          <Textarea
            v-if="useLargeInput"
            v-model="editSingleValue"
            style="width: 100%"
            rows="5"
            autoResize
          />
          <InputText
            v-else
            v-model="editSingleValue"
            style="width: 100%"
            :class="{ 'p-invalid': emailError || urlError }"
            :placeholder="isEmailAttribute(editAttr.name) ? 'user@domain.com' : isUrlAttribute(editAttr.name) ? 'https://...' : ''"
          />
          <small v-if="emailError" class="edit-error">{{ emailError }}</small>
          <small v-if="urlError" class="edit-error">{{ urlError }}</small>
          <small v-if="editAttr.rangeUpper" class="edit-hint">
            Max length: {{ editAttr.rangeUpper }} characters
          </small>
        </div>
      </template>

      <template #footer>
        <div style="display: flex; justify-content: flex-end; gap: 0.5rem">
          <Button label="Cancel" severity="secondary" text @click="editDialogVisible = false" />
          <Button label="Save" icon="pi pi-save" @click="saveEdit" :loading="saving"
                  :disabled="!!emailError || !!urlError || !!rangeError" />
        </div>
      </template>
    </Dialog>

    <!-- Flag Editor Dialog -->
    <FlagEditor
      v-if="flagEditAttr"
      v-model:visible="flagEditorVisible"
      :attributeName="flagEditAttr.name"
      :currentValue="flagEditAttr.values.length > 0 ? parseInt(flagEditAttr.values[0].rawValue) || 0 : 0"
      :flagDefinitions="KNOWN_FLAG_ATTRIBUTES[flagEditAttr.name] || {}"
      @save="onFlagSave"
    />
  </div>
</template>

<style scoped>
.attribute-editor {
  display: flex;
  flex-direction: column;
  gap: 0;
  flex: 1;
  min-height: 0;
}

.toolbar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
  flex-wrap: wrap;
}

.toolbar-spacer {
  flex: 1;
}

.show-all-toggle {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 0.8125rem;
  color: var(--p-text-muted-color);
  cursor: pointer;
  user-select: none;
}

.show-all-toggle input {
  cursor: pointer;
}

.attr-count {
  color: var(--p-text-muted-color);
  font-size: 0.8125rem;
  white-space: nowrap;
}

/* Table styles */
.attr-table :deep(.p-datatable-tbody > tr) {
  cursor: default;
}

.attr-table :deep(.attr-row-unset) {
  /* no background override — inherit table default */
}

.attr-name-cell {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.attr-name {
  font-family: 'Cascadia Code', 'Fira Code', Consolas, monospace;
  font-size: 0.8125rem;
  font-weight: 500;
  color: var(--p-text-color);
}

.attr-name-unset {
  color: var(--p-text-muted-color);
  font-style: italic;
  font-weight: 400;
}

.required-marker {
  color: var(--app-danger-text);
  font-weight: 700;
  margin-right: 0.125rem;
}

.attr-badges {
  display: flex;
  gap: 0.25rem;
  flex-wrap: wrap;
}

.attr-badge {
  font-size: 0.625rem !important;
  padding: 0 0.375rem !important;
  line-height: 1.25rem !important;
}

.syntax-text {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
}

/* Value display styles */
.value-cell {
  font-size: 0.8125rem;
  min-height: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
}

.value-empty {
  color: var(--p-text-muted-color);
  font-style: italic;
  font-size: 0.8125rem;
}

.value-not-set {
  color: var(--p-text-muted-color);
  font-style: italic;
  font-size: 0.8125rem;
}

.dn-value {
  line-height: 1.5;
}

.dn-link {
  color: var(--app-info-text);
  text-decoration: none;
  font-size: 0.8125rem;
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
}

.dn-link:hover {
  text-decoration: underline;
  color: var(--app-info-text-strong);
}

.guid-value {
  font-family: monospace;
  font-size: 0.8125rem;
  color: var(--p-text-color);
}

.flags-display {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.flags-int {
  font-family: monospace;
  font-size: 0.8125rem;
  color: var(--p-text-color);
}

.flags-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
}

.flag-tag {
  font-size: 0.625rem !important;
  padding: 0 0.375rem !important;
  line-height: 1.25rem !important;
}

.interval-value {
  font-size: 0.8125rem;
  color: var(--p-text-color);
}

.string-values {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
}

.string-value {
  font-family: monospace;
  font-size: 0.8125rem;
  color: var(--p-text-color);
  word-break: break-all;
  line-height: 1.4;
}

.expand-btn {
  align-self: flex-start;
  font-size: 0.75rem !important;
  padding: 0 0.25rem !important;
}

/* Action buttons */
.action-buttons {
  display: flex;
  gap: 0.125rem;
  align-items: center;
}

.set-value-btn {
  font-size: 0.75rem !important;
  padding: 0.125rem 0.5rem !important;
}

/* Edit dialog styles */
.edit-schema-info {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
  padding: 0.625rem 0.75rem;
  background: var(--app-info-bg);
  border-radius: 0.375rem;
  color: var(--app-info-text-strong);
  font-size: 0.8125rem;
  margin-bottom: 0.75rem;
  line-height: 1.4;
}

.single-editor {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.edit-label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
}

.edit-hint {
  color: var(--p-text-muted-color);
  font-size: 0.75rem;
}

.edit-error {
  color: var(--app-danger-text);
  font-size: 0.75rem;
}

.bool-toggle-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.bool-toggle-label {
  font-family: monospace;
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--p-text-color);
}

.readonly-notice {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem;
  background: var(--app-warn-bg);
  border-radius: 0.375rem;
  color: var(--app-warn-text-strong);
  font-size: 0.8125rem;
}

.multi-value-editor {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.multi-value-list {
  border: 1px solid var(--app-neutral-border);
  border-radius: 0.375rem;
  max-height: 300px;
  overflow-y: auto;
}

.multi-value-empty {
  padding: 1rem;
  text-align: center;
  color: var(--p-text-muted-color);
  font-style: italic;
  font-size: 0.8125rem;
}

.multi-value-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.375rem 0.75rem;
  border-bottom: 1px solid var(--app-neutral-bg);
}

.multi-value-item:last-child {
  border-bottom: none;
}

.multi-value-text {
  font-family: monospace;
  font-size: 0.8125rem;
  color: var(--p-text-color);
  word-break: break-all;
}

.multi-value-add {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}
</style>
