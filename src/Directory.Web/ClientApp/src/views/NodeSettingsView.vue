<script setup lang="ts">
import { ref, onMounted, reactive, watch } from 'vue'
import Select from 'primevue/select'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import InputSwitch from 'primevue/inputswitch'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import {
  fetchNodes,
  fetchSectionSchema,
  fetchClusterConfig,
  fetchNodeConfig,
  updateNodeConfig,
  deleteNodeConfig,
} from '../api/configuration'
import type { ConfigFieldMeta, ConfigValues, ConfigNode } from '../types/configuration'

const toast = useToast()
const loading = ref(true)
const nodes = ref<ConfigNode[]>([])
const selectedHostname = ref<string | null>(null)

const nodeSections = ['Ldap', 'Kerberos', 'Dns', 'RpcServer', 'DcNode']

// Per-section state
const schemas = reactive<Record<string, ConfigFieldMeta[]>>({})
const clusterConfigs = reactive<Record<string, ConfigValues>>({})
const nodeConfigs = reactive<Record<string, ConfigValues>>({})
const formValues = reactive<Record<string, Record<string, any>>>({})
const saving = reactive<Record<string, boolean>>({})
const deleting = reactive<Record<string, boolean>>({})
const sectionLoading = reactive<Record<string, boolean>>({})

onMounted(async () => {
  try {
    nodes.value = await fetchNodes()
    // Preload schemas
    await Promise.all(nodeSections.map(async (s) => {
      schemas[s] = await fetchSectionSchema(s)
    }))
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
})

watch(selectedHostname, async (hostname) => {
  if (!hostname) return
  await Promise.all(nodeSections.map(s => loadNodeSection(s, hostname)))
})

async function loadNodeSection(section: string, hostname: string) {
  sectionLoading[section] = true
  try {
    const [cluster, node] = await Promise.all([
      fetchClusterConfig(section),
      fetchNodeConfig(section, hostname),
    ])
    clusterConfigs[section] = cluster
    nodeConfigs[section] = node

    // Build form values: node override values (empty string means inherit)
    const vals: Record<string, any> = {}
    for (const field of schemas[section] ?? []) {
      const nodeVal = node.values?.[field.name]
      if (nodeVal !== undefined) {
        vals[field.name] = nodeVal
      } else {
        // Empty / null means inherit from cluster
        vals[field.name] = field.type === 'bool' ? null : field.type === 'int' ? null : ''
      }
    }
    formValues[section] = vals
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: `Failed to load ${section}: ${e.message}`, life: 5000 })
  } finally {
    sectionLoading[section] = false
  }
}

function getClusterDefault(section: string, fieldName: string, field: ConfigFieldMeta): string {
  const clusterVal = clusterConfigs[section]?.values?.[fieldName]
  if (clusterVal !== undefined) return String(clusterVal)
  return String(field.defaultValue ?? '')
}

async function saveSection(section: string) {
  if (!selectedHostname.value) return
  saving[section] = true
  try {
    // Only send non-empty overrides
    const overrides: Record<string, any> = {}
    for (const field of schemas[section] ?? []) {
      const val = formValues[section]?.[field.name]
      if (val !== null && val !== '' && val !== undefined) {
        overrides[field.name] = val
      }
    }
    const result = await updateNodeConfig(section, selectedHostname.value, overrides, nodeConfigs[section]?.etag)
    nodeConfigs[section] = result
    toast.add({ severity: 'success', summary: 'Saved', detail: `${section} overrides saved for ${selectedHostname.value}`, life: 3000 })
  } catch (e: any) {
    if (e.message?.includes('409')) {
      toast.add({ severity: 'warn', summary: 'Conflict', detail: 'Configuration was modified by another user. Refreshing...', life: 5000 })
      await loadNodeSection(section, selectedHostname.value!)
    } else {
      toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
    }
  } finally {
    saving[section] = false
  }
}

async function resetSection(section: string) {
  if (!selectedHostname.value) return
  deleting[section] = true
  try {
    await deleteNodeConfig(section, selectedHostname.value)
    // Reload to reflect removed overrides
    await loadNodeSection(section, selectedHostname.value)
    toast.add({ severity: 'info', summary: 'Reset', detail: `${section} overrides removed — node will use cluster defaults`, life: 3000 })
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    deleting[section] = false
  }
}

function isSensitiveField(name: string): boolean {
  const lower = name.toLowerCase()
  return lower.includes('password') || lower.includes('connectionstring') || lower.includes('secret')
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Node Settings</h1>
      <p>Manage per-node configuration overrides</p>
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">Override cluster-wide settings for individual nodes. Node-level overrides take precedence over cluster defaults for the selected node. Fields left empty will inherit the cluster value shown in parentheses.</p>
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <template v-else>
      <div class="card" style="margin-bottom: 1.5rem">
        <div class="card-title">Select Node</div>
        <Select
          v-model="selectedHostname"
          :options="nodes"
          optionLabel="hostname"
          optionValue="hostname"
          placeholder="Select a node..."
          style="width: 100%; max-width: 400px"
        />
        <div v-if="nodes.length === 0" style="margin-top: 0.75rem; color: var(--p-text-muted-color); font-size: 0.875rem">
          No nodes with overrides found. Node entries are created when you save overrides.
        </div>
      </div>

      <div v-if="selectedHostname" class="card">
        <TabView>
          <TabPanel v-for="section in nodeSections" :key="section" :header="section">
            <div v-if="sectionLoading[section]" style="text-align: center; padding: 2rem">
              <ProgressSpinner />
            </div>

            <div v-else-if="schemas[section]">
              <div class="config-grid">
                <div v-for="field in schemas[section]" :key="field.name" class="config-row">
                  <label>
                    {{ field.name }}
                    <Tag v-if="!field.hotReloadable" value="Requires restart" severity="warn"
                         style="margin-left: 0.5rem; font-size: 0.7rem; vertical-align: middle" />
                  </label>
                  <small class="field-description">
                    {{ field.description }}
                    <span style="color: var(--p-primary-color)">
                      (cluster: {{ getClusterDefault(section, field.name, field) }})
                    </span>
                  </small>

                  <!-- Boolean fields -->
                  <div v-if="field.type === 'bool'" style="display: flex; align-items: center; gap: 0.5rem">
                    <InputSwitch v-model="formValues[section][field.name]" />
                    <small v-if="formValues[section][field.name] === null" style="color: var(--p-text-muted-color)">
                      Inheriting from cluster
                    </small>
                  </div>

                  <!-- Integer fields -->
                  <InputNumber
                    v-else-if="field.type === 'int'"
                    v-model="formValues[section][field.name]"
                    :min="field.minValue ?? undefined"
                    :max="field.maxValue ?? undefined"
                    :placeholder="'Cluster default: ' + getClusterDefault(section, field.name, field)"
                    class="config-input"
                  />

                  <!-- Sensitive string fields -->
                  <Password
                    v-else-if="field.type === 'string' && isSensitiveField(field.name)"
                    v-model="formValues[section][field.name]"
                    :feedback="false"
                    toggleMask
                    :placeholder="'Cluster default: ' + getClusterDefault(section, field.name, field)"
                    class="config-input"
                    inputClass="config-input"
                  />

                  <!-- Regular string fields -->
                  <InputText
                    v-else
                    v-model="formValues[section][field.name]"
                    :placeholder="'Cluster default: ' + getClusterDefault(section, field.name, field)"
                    class="config-input"
                  />
                </div>
              </div>

              <div style="margin-top: 1.5rem; display: flex; justify-content: flex-end; gap: 0.5rem">
                <Button
                  label="Reset to Cluster Defaults"
                  icon="pi pi-refresh"
                  severity="secondary"
                  outlined
                  @click="resetSection(section)"
                  :loading="deleting[section]"
                />
                <Button label="Save Overrides" icon="pi pi-save" @click="saveSection(section)" :loading="saving[section]" />
              </div>
            </div>
          </TabPanel>
        </TabView>
      </div>
    </template>
  </div>
</template>

<style scoped>
.config-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1.25rem;
}

@media (max-width: 768px) {
  .config-grid {
    grid-template-columns: 1fr;
  }
}

.config-row {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.config-row label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
}

.field-description {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  margin-bottom: 0.25rem;
}

.config-input {
  width: 100%;
}
</style>
