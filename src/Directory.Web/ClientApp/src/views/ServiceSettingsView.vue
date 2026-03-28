<script setup lang="ts">
import { ref, onMounted } from 'vue'
import Panel from 'primevue/panel'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'

const toast = useToast()
const loading = ref(true)
const settings = ref<any>(null)
const error = ref<string | null>(null)

async function loadSettings() {
  loading.value = true
  error.value = null
  try {
    const res = await fetch('/api/v1/service-settings')
    if (!res.ok) throw new Error(`Failed to load settings: ${res.status} ${res.statusText}`)
    settings.value = await res.json()
  } catch (e: any) {
    error.value = e.message
    toast.add({ severity: 'error', summary: 'Error', detail: e.message, life: 5000 })
  } finally {
    loading.value = false
  }
}

onMounted(loadSettings)

interface SettingItem {
  label: string
  value: any
  description: string
}

function formatValue(val: any): string {
  if (val === true) return 'Yes'
  if (val === false) return 'No'
  if (val === '' || val === null || val === undefined) return '(not set)'
  return String(val)
}
</script>

<template>
  <div>
    <div class="page-header">
      <h1>Service Settings</h1>
      <p>Current service configuration. Port and connection settings are managed through appsettings.json and require a service restart to take effect.</p>
    </div>

    <div v-if="loading" style="text-align: center; padding: 4rem">
      <ProgressSpinner />
    </div>

    <div v-else-if="error" style="text-align: center; padding: 4rem">
      <p style="color: var(--p-red-500); margin-bottom: 1rem">{{ error }}</p>
      <Button label="Retry" icon="pi pi-refresh" @click="loadSettings" />
    </div>

    <div v-else-if="settings" class="settings-panels">
      <!-- Protocol Servers -->
      <Panel header="Protocol Servers" class="settings-panel">
        <div class="settings-grid">
          <h3 class="section-subtitle">LDAP</h3>
          <div class="setting-item">
            <div class="setting-label">Port</div>
            <div class="setting-value">{{ formatValue(settings.ldap.port) }}</div>
            <div class="setting-desc">Primary LDAP listening port for directory queries</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">TLS Port</div>
            <div class="setting-value">{{ formatValue(settings.ldap.tlsPort) }}</div>
            <div class="setting-desc">LDAPS port for TLS-encrypted directory queries</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Global Catalog Port</div>
            <div class="setting-value">{{ formatValue(settings.ldap.gcPort) }}</div>
            <div class="setting-desc">Global Catalog port for forest-wide searches</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Global Catalog TLS Port</div>
            <div class="setting-value">{{ formatValue(settings.ldap.gcTlsPort) }}</div>
            <div class="setting-desc">TLS-encrypted Global Catalog port</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Max Connections</div>
            <div class="setting-value">{{ formatValue(settings.ldap.maxConnections) }}</div>
            <div class="setting-desc">Maximum number of concurrent LDAP connections</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Max Page Size</div>
            <div class="setting-value">{{ formatValue(settings.ldap.maxPageSize) }}</div>
            <div class="setting-desc">Maximum entries returned per paged search request</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Idle Timeout</div>
            <div class="setting-value">{{ formatValue(settings.ldap.idleTimeoutSeconds) }}s</div>
            <div class="setting-desc">Seconds before an idle LDAP connection is closed</div>
          </div>

          <h3 class="section-subtitle">Kerberos</h3>
          <div class="setting-item">
            <div class="setting-label">Port</div>
            <div class="setting-value">{{ formatValue(settings.kerberos.port) }}</div>
            <div class="setting-desc">KDC port for Kerberos authentication requests</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Kpasswd Port</div>
            <div class="setting-value">{{ formatValue(settings.kerberos.kpasswdPort) }}</div>
            <div class="setting-desc">Kerberos password change service port</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Default Realm</div>
            <div class="setting-value">{{ formatValue(settings.kerberos.defaultRealm) }}</div>
            <div class="setting-desc">Default Kerberos realm for authentication</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Maximum Skew</div>
            <div class="setting-value">{{ formatValue(settings.kerberos.maximumSkew) }}</div>
            <div class="setting-desc">Maximum allowed clock skew between client and KDC</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Session Lifetime</div>
            <div class="setting-value">{{ formatValue(settings.kerberos.sessionLifetime) }}</div>
            <div class="setting-desc">Default ticket-granting ticket lifetime</div>
          </div>

          <h3 class="section-subtitle">DNS</h3>
          <div class="setting-item">
            <div class="setting-label">Port</div>
            <div class="setting-value">{{ formatValue(settings.dns.port) }}</div>
            <div class="setting-desc">DNS server listening port</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Server Hostname</div>
            <div class="setting-value">{{ formatValue(settings.dns.serverHostname) }}</div>
            <div class="setting-desc">Hostname advertised in DNS SOA records</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Default TTL</div>
            <div class="setting-value">{{ formatValue(settings.dns.defaultTtl) }}s</div>
            <div class="setting-desc">Default time-to-live for DNS records in seconds</div>
          </div>

          <h3 class="section-subtitle">RPC</h3>
          <div class="setting-item">
            <div class="setting-label">Endpoint Mapper Port</div>
            <div class="setting-value">{{ formatValue(settings.rpc.endpointMapperPort) }}</div>
            <div class="setting-desc">RPC endpoint mapper port for service discovery</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Service Port</div>
            <div class="setting-value">{{ formatValue(settings.rpc.servicePort) }}</div>
            <div class="setting-desc">RPC service port for directory replication and management</div>
          </div>

          <h3 class="section-subtitle">Replication</h3>
          <div class="setting-item">
            <div class="setting-label">HTTP Port</div>
            <div class="setting-value">{{ formatValue(settings.replication.httpPort) }}</div>
            <div class="setting-desc">AD Web Services / DRS HTTP replication port</div>
          </div>
        </div>
      </Panel>

      <!-- Database -->
      <Panel header="Database" class="settings-panel">
        <div class="settings-grid">
          <div class="setting-item">
            <div class="setting-label">Cosmos DB Status</div>
            <div class="setting-value">
              <Tag :value="settings.cosmosDb.isConfigured ? 'Configured' : 'Not Configured'"
                   :severity="settings.cosmosDb.isConfigured ? 'success' : 'warn'" />
            </div>
            <div class="setting-desc">Whether a Cosmos DB connection string is configured</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Database Name</div>
            <div class="setting-value">{{ formatValue(settings.cosmosDb.databaseName) }}</div>
            <div class="setting-desc">Cosmos DB database name for directory data storage</div>
          </div>
        </div>
      </Panel>

      <!-- Cache -->
      <Panel header="Cache" class="settings-panel">
        <div class="settings-grid">
          <div class="setting-item">
            <div class="setting-label">Redis Status</div>
            <div class="setting-value">
              <Tag :value="settings.cache.redisConfigured ? 'Configured' : 'Not Configured'"
                   :severity="settings.cache.redisConfigured ? 'success' : 'secondary'" />
            </div>
            <div class="setting-desc">Whether a Redis distributed cache is configured for cross-node caching</div>
          </div>
        </div>
      </Panel>

      <!-- Domain -->
      <Panel header="Domain" class="settings-panel">
        <div class="settings-grid">
          <div class="setting-item">
            <div class="setting-label">Domain DN</div>
            <div class="setting-value">{{ formatValue(settings.namingContexts.domainDn) }}</div>
            <div class="setting-desc">Distinguished name of the domain root naming context</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Forest DNS Name</div>
            <div class="setting-value">{{ formatValue(settings.namingContexts.forestDnsName) }}</div>
            <div class="setting-desc">DNS name of the Active Directory forest</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Kerberos Realm</div>
            <div class="setting-value">{{ formatValue(settings.kerberos.defaultRealm) }}</div>
            <div class="setting-desc">Kerberos realm name (typically the uppercased domain DNS name)</div>
          </div>
        </div>
      </Panel>

      <!-- System -->
      <Panel header="System" class="settings-panel">
        <div class="settings-grid">
          <div class="setting-item">
            <div class="setting-label">Machine Name</div>
            <div class="setting-value">{{ formatValue(settings.environment.machineName) }}</div>
            <div class="setting-desc">Hostname of the server running this service instance</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Operating System</div>
            <div class="setting-value">{{ formatValue(settings.environment.osVersion) }}</div>
            <div class="setting-desc">OS platform and version</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">.NET Version</div>
            <div class="setting-value">{{ formatValue(settings.environment.dotnetVersion) }}</div>
            <div class="setting-desc">.NET runtime version powering the service</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">Processor Count</div>
            <div class="setting-value">{{ formatValue(settings.environment.processorCount) }}</div>
            <div class="setting-desc">Number of logical processors available to the service</div>
          </div>
          <div class="setting-item">
            <div class="setting-label">64-bit OS</div>
            <div class="setting-value">
              <Tag :value="settings.environment.is64Bit ? 'Yes' : 'No'"
                   :severity="settings.environment.is64Bit ? 'success' : 'secondary'" />
            </div>
            <div class="setting-desc">Whether the host operating system is 64-bit</div>
          </div>
        </div>
      </Panel>

      <div style="text-align: right; margin-top: 1rem">
        <Button label="Refresh" icon="pi pi-refresh" severity="secondary" @click="loadSettings" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.settings-panels {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.settings-panel :deep(.p-panel-content) {
  padding: 1.25rem;
}

.settings-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}

@media (max-width: 768px) {
  .settings-grid {
    grid-template-columns: 1fr;
  }
}

.section-subtitle {
  grid-column: 1 / -1;
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--p-primary-color);
  margin: 0.5rem 0 0 0;
  padding-bottom: 0.25rem;
  border-bottom: 1px solid var(--p-surface-200);
}

.section-subtitle:first-child {
  margin-top: 0;
}

.setting-item {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  padding: 0.5rem 0;
}

.setting-label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
}

.setting-value {
  font-size: 0.9375rem;
  font-weight: 500;
  color: var(--p-text-color);
  word-break: break-all;
}

.setting-desc {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
}
</style>
