<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Select from 'primevue/select'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'

export interface FilterCondition {
  id: string
  attribute: string
  operator: string
  value: string
}

export interface FilterGroup {
  id: string
  logic: 'AND' | 'OR' | 'NOT'
  conditions: FilterCondition[]
  groups: FilterGroup[]
}

const props = defineProps<{
  modelValue: string
  commonAttributes?: string[]
}>()

const emit = defineEmits<{
  'update:modelValue': [val: string]
}>()

const defaultAttributes = [
  'cn', 'sAMAccountName', 'displayName', 'mail', 'description',
  'objectClass', 'objectCategory', 'userPrincipalName', 'givenName', 'sn',
  'title', 'department', 'company', 'manager', 'memberOf', 'member',
  'userAccountControl', 'whenCreated', 'whenChanged', 'operatingSystem',
  'dNSHostName', 'servicePrincipalName', 'groupType', 'distinguishedName',
]

const attributeOptions = computed(() => {
  const attrs = props.commonAttributes?.length ? props.commonAttributes : defaultAttributes
  return attrs.map(a => ({ label: a, value: a }))
})

const operatorOptions = [
  { label: 'Equals (=)', value: '=' },
  { label: 'Greater or Equal (>=)', value: '>=' },
  { label: 'Less or Equal (<=)', value: '<=' },
  { label: 'Approx Match (~=)', value: '~=' },
  { label: 'Present (*)', value: 'present' },
  { label: 'Not Present', value: 'absent' },
  { label: 'Contains', value: 'contains' },
  { label: 'Starts With', value: 'startswith' },
  { label: 'Ends With', value: 'endswith' },
]

const logicOptions = [
  { label: 'AND', value: 'AND' },
  { label: 'OR', value: 'OR' },
]

let nextId = 1
function genId() { return `cond-${nextId++}` }

const rootGroup = ref<FilterGroup>(createGroup('AND'))

function createCondition(): FilterCondition {
  return { id: genId(), attribute: 'cn', operator: '=', value: '' }
}

function createGroup(logic: 'AND' | 'OR' | 'NOT' = 'AND'): FilterGroup {
  return { id: genId(), logic, conditions: [createCondition()], groups: [] }
}

function addCondition(group: FilterGroup) {
  group.conditions.push(createCondition())
  rebuildFilter()
}

function removeCondition(group: FilterGroup, index: number) {
  group.conditions.splice(index, 1)
  rebuildFilter()
}

function addSubGroup(group: FilterGroup) {
  group.groups.push(createGroup('AND'))
  rebuildFilter()
}

function removeSubGroup(group: FilterGroup, index: number) {
  group.groups.splice(index, 1)
  rebuildFilter()
}

function conditionToFilter(cond: FilterCondition): string {
  const attr = cond.attribute
  switch (cond.operator) {
    case '=': return `(${attr}=${cond.value})`
    case '>=': return `(${attr}>=${cond.value})`
    case '<=': return `(${attr}<=${cond.value})`
    case '~=': return `(${attr}~=${cond.value})`
    case 'present': return `(${attr}=*)`
    case 'absent': return `(!(${attr}=*))`
    case 'contains': return `(${attr}=*${cond.value}*)`
    case 'startswith': return `(${attr}=${cond.value}*)`
    case 'endswith': return `(${attr}=*${cond.value})`
    default: return `(${attr}=${cond.value})`
  }
}

function groupToFilter(group: FilterGroup): string {
  const parts: string[] = []

  for (const cond of group.conditions) {
    if (!cond.attribute) continue
    parts.push(conditionToFilter(cond))
  }

  for (const sub of group.groups) {
    const f = groupToFilter(sub)
    if (f) parts.push(f)
  }

  if (parts.length === 0) return ''
  if (parts.length === 1 && group.logic !== 'NOT') return parts[0]

  const op = group.logic === 'AND' ? '&' : group.logic === 'OR' ? '|' : '!'
  if (group.logic === 'NOT') {
    return `(!${parts[0]})`
  }
  return `(${op}${parts.join('')})`
}

function rebuildFilter() {
  const filter = groupToFilter(rootGroup.value)
  emit('update:modelValue', filter)
}

// Watch for changes in group structure
watch(rootGroup, rebuildFilter, { deep: true })

// Allow parsing an existing filter into the builder (basic support)
function onAttributeChange() {
  rebuildFilter()
}
</script>

<template>
  <div class="filter-builder">
    <div class="filter-group root-group">
      <div class="group-header">
        <Select v-model="rootGroup.logic" :options="logicOptions" optionLabel="label" optionValue="value"
                size="small" style="width: 100px" @change="rebuildFilter" />
        <span class="group-label">group</span>
        <div style="flex: 1" />
        <Button icon="pi pi-plus" label="Condition" size="small" severity="secondary" text
                @click="addCondition(rootGroup)" />
        <Button icon="pi pi-folder-plus" label="Sub-group" size="small" severity="secondary" text
                @click="addSubGroup(rootGroup)" />
      </div>

      <div class="group-conditions">
        <div v-for="(cond, ci) in rootGroup.conditions" :key="cond.id" class="condition-row">
          <Select v-model="cond.attribute" :options="attributeOptions" optionLabel="label" optionValue="value"
                  size="small" style="width: 200px" filter @change="onAttributeChange" editable />
          <Select v-model="cond.operator" :options="operatorOptions" optionLabel="label" optionValue="value"
                  size="small" style="width: 170px" />
          <InputText v-if="cond.operator !== 'present' && cond.operator !== 'absent'"
                     v-model="cond.value" size="small" placeholder="Value" style="flex: 1; min-width: 120px" />
          <div v-else style="flex: 1" />
          <Button icon="pi pi-times" size="small" severity="danger" text
                  @click="removeCondition(rootGroup, ci)"
                  :disabled="rootGroup.conditions.length <= 1 && rootGroup.groups.length === 0" />
        </div>
      </div>

      <!-- Sub-groups -->
      <div v-for="(sub, si) in rootGroup.groups" :key="sub.id" class="filter-group sub-group">
        <div class="group-header">
          <Select v-model="sub.logic" :options="logicOptions" optionLabel="label" optionValue="value"
                  size="small" style="width: 100px" />
          <span class="group-label">sub-group</span>
          <div style="flex: 1" />
          <Button icon="pi pi-plus" size="small" severity="secondary" text
                  @click="addCondition(sub)" v-tooltip="'Add condition'" />
          <Button icon="pi pi-trash" size="small" severity="danger" text
                  @click="removeSubGroup(rootGroup, si)" v-tooltip="'Remove group'" />
        </div>
        <div class="group-conditions">
          <div v-for="(cond, ci) in sub.conditions" :key="cond.id" class="condition-row">
            <Select v-model="cond.attribute" :options="attributeOptions" optionLabel="label" optionValue="value"
                    size="small" style="width: 200px" filter editable />
            <Select v-model="cond.operator" :options="operatorOptions" optionLabel="label" optionValue="value"
                    size="small" style="width: 170px" />
            <InputText v-if="cond.operator !== 'present' && cond.operator !== 'absent'"
                       v-model="cond.value" size="small" placeholder="Value" style="flex: 1; min-width: 120px" />
            <div v-else style="flex: 1" />
            <Button icon="pi pi-times" size="small" severity="danger" text
                    @click="removeCondition(sub, ci)"
                    :disabled="sub.conditions.length <= 1" />
          </div>
        </div>
      </div>
    </div>

    <div class="filter-preview">
      <label>Generated LDAP Filter:</label>
      <code>{{ modelValue || '(empty)' }}</code>
    </div>
  </div>
</template>

<style scoped>
.filter-builder {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.filter-group {
  border: 1px solid var(--p-surface-border);
  border-radius: 0.5rem;
  padding: 0.75rem;
  background: var(--p-surface-ground);
}

.sub-group {
  margin-top: 0.5rem;
  margin-left: 1rem;
  background: var(--p-surface-card);
}

.group-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.5rem;
  padding-bottom: 0.5rem;
  border-bottom: 1px solid var(--p-surface-border);
}

.group-label {
  font-size: 0.8125rem;
  color: var(--p-text-color);
  font-weight: 500;
}

.group-conditions {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.condition-row {
  display: flex;
  align-items: center;
  gap: 0.375rem;
}

.filter-preview {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.filter-preview label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-color);
}

.filter-preview code {
  font-family: monospace;
  font-size: 0.8125rem;
  padding: 0.5rem 0.75rem;
  background: var(--p-surface-ground);
  border: 1px solid var(--p-surface-border);
  border-radius: 0.375rem;
  word-break: break-all;
  color: var(--p-text-color);
}
</style>
