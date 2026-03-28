<script setup lang="ts">
import { ref, onMounted, reactive } from 'vue'
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
import { fetchSections, fetchSectionSchema, fetchClusterConfig, updateClusterConfig } from '../api/configuration'
import type { ConfigSection, ConfigFieldMeta, ConfigValues } from '../types/configuration'

const toast = useToast()
const loading = ref(true)
const sections = ref<ConfigSection[]>([])

// Per-section state keyed by section name
const schemas = reactive<Record<string, ConfigFieldMeta[]>>({})
const configs = reactive<Record<string, ConfigValues>>({})
const formValues = reactive<Record<string, Record<string, any>>>({})
const saving = reactive<Record<string, boolean>>({})
const sectionLoading = reactive<Record<string, boolean>>({})

const clusterSections = ['Cache', 'Ldap', 'Kerberos', 'Dns', 'Replication']

onMounted(async () => {
  try {
    sections.value = await fetchSections()
    // Load schema and values for each cluster section
    await Promise.all(clusterSections.map(s => loadSection(s)))
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
})

async function loadSection(section: string) {
  sectionLoading[section] = true
  try {
    const [schema, config] = await Promise.all([
      fetchSectionSchema(section),
      fetchClusterConfig(section),
    ])
    schemas[section] = schema
    configs[section] = config

    // Build form values: use saved values or defaults from schema
    const vals: Record<string, any> = {}
    for (const field of schema) {
      const savedVal = config.values?.[field.name]
      vals[field.name] = savedVal !== undefined ? savedVal : field.defaultValue
    }
    formValues[section] = vals
  } catch (e: any) {
    toast.add({ severity: 'error', summary: 'Error', detail: `Failed to load ${section}: ${e.message}`, life: 5000 })
  } finally {
    sectionLoading[section] = false
  }
}

async function saveSection(section: string) {
  saving[section] = true
  try {
    const result = await updateClusterConfig(section, formValues[section], configs[section]?.etag)
    configs[section] = result
    toast.add({ severity: 'success', summary: 'Saved', detail: `${section} configuration saved successfully`, life: 3000 })
  } catch (e: any) {
    if (e.message?.includes('409')) {
      toast.add({ severity: 'warn', summary: 'Conflict', detail: 'Configuration was modified by another user. Refreshing...', life: 5000 })
      await loadSection(section)
    } else {
      toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
    }
  } finally {
    saving[section] = false
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
      <h1>Cluster Settings</h1>
      <p>Manage cluster-wide configuration for all domain controllers</p>
      <p style="font-size: 0.8125rem; color: var(--p-text-muted-color); margin: 0 0 0.75rem 0">Configure settings that apply to all nodes in the cluster by default. Individual nodes can override these values in Node Settings. Changes are versioned and propagated to all nodes automatically.</p>
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else class="card">
      <TabView>
        <TabPanel v-for="section in clusterSections" :key="section" :value="section" :header="section">
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
                <small class="field-description">{{ field.description }}</small>

                <!-- Boolean fields -->
                <InputSwitch
                  v-if="field.type === 'bool'"
                  v-model="formValues[section][field.name]"
                />

                <!-- Integer fields -->
                <InputNumber
                  v-else-if="field.type === 'int'"
                  v-model="formValues[section][field.name]"
                  :min="field.minValue ?? undefined"
                  :max="field.maxValue ?? undefined"
                  class="config-input"
                />

                <!-- Sensitive string fields -->
                <Password
                  v-else-if="field.type === 'string' && isSensitiveField(field.name)"
                  v-model="formValues[section][field.name]"
                  :feedback="false"
                  toggleMask
                  class="config-input"
                  inputClass="config-input"
                />

                <!-- Regular string fields -->
                <InputText
                  v-else
                  v-model="formValues[section][field.name]"
                  class="config-input"
                />
              </div>
            </div>

            <div style="margin-top: 1.5rem; display: flex; justify-content: flex-end; gap: 0.5rem">
              <span v-if="configs[section]?.version" style="align-self: center; color: var(--p-text-muted-color); font-size: 0.8rem">
                v{{ configs[section].version }} &mdash; {{ configs[section].modifiedBy }}
              </span>
              <Button label="Save" icon="pi pi-save" @click="saveSection(section)" :loading="saving[section]" />
            </div>
          </div>
        </TabPanel>
      </TabView>
    </div>
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
