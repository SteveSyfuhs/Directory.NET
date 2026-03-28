<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import Button from 'primevue/button'
import Toast from 'primevue/toast'
import { useToast } from 'primevue/usetoast'
import ProgressSpinner from 'primevue/progressspinner'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'
import InputText from 'primevue/inputtext'
import ConfirmDialog from 'primevue/confirmdialog'
import { useSetupStore } from './stores/setup'
import { useThemeStore } from './stores/theme'
import { useAuthStore } from './stores/auth'
import SetupWizardView from './views/SetupWizardView.vue'
import LoginView from './views/LoginView.vue'
import ErrorBoundary from './components/ErrorBoundary.vue'
import AppBreadcrumb from './components/AppBreadcrumb.vue'
import { useKeyboard } from './composables/useKeyboard'
import { useSessionMonitor } from './composables/useSessionMonitor'

const router = useRouter()
const route = useRoute()
const setupStore = useSetupStore()
const theme = useThemeStore()
const authStore = useAuthStore()
const toast = useToast()

// Start session monitoring (polls /auth/me, shows expiry warnings)
useSessionMonitor()

// Listen for session:expired events dispatched by the API client's 401 handler
function onSessionExpired() {
  toast.add({
    severity: 'warn',
    summary: 'Session Expired',
    detail: 'Your session has expired. Please log in again.',
    life: 5000,
  })
}
window.addEventListener('session:expired', onSessionExpired)
onUnmounted(() => {
  window.removeEventListener('session:expired', onSessionExpired)
})

const sidebarOpen = ref(false)
const searchInput = ref<HTMLInputElement | null>(null)
const searchValue = ref('')

onMounted(async () => {
  await setupStore.checkStatus()
  await authStore.checkAuth()
})

async function handleLogout() {
  await authStore.logout()
  await router.push('/login')
}

// Close sidebar on route change (mobile)
watch(() => route.path, () => {
  if (window.innerWidth < 1024) {
    sidebarOpen.value = false
  }
})

function toggleSidebar() {
  sidebarOpen.value = !sidebarOpen.value
}

function focusSearch() {
  searchInput.value?.focus()
}

function closeSidebar() {
  sidebarOpen.value = false
}

// Keyboard shortcuts
useKeyboard([
  {
    key: 'k',
    ctrl: true,
    handler: (e) => { e.preventDefault(); focusSearch() },
  },
  {
    key: '/',
    handler: (e) => {
      // Only if not in an input
      const target = e.target as HTMLElement
      if (!['INPUT', 'TEXTAREA'].includes(target.tagName)) {
        e.preventDefault()
        focusSearch()
      }
    },
  },
  {
    key: 'b',
    ctrl: true,
    handler: (e) => { e.preventDefault(); toggleSidebar() },
  },
  {
    key: 'Escape',
    handler: () => {
      if (window.innerWidth < 1024 && sidebarOpen.value) {
        sidebarOpen.value = false
      }
    },
  },
])

interface MenuItem {
  label?: string
  icon?: string
  route?: string
  separator?: boolean
  permission?: string
}

const allMenuItems: MenuItem[] = [
  { label: 'Dashboard', icon: 'pi pi-home', route: '/' },
  { label: 'Browse Directory', icon: 'pi pi-sitemap', route: '/browse' },
  { label: 'Advanced Search', icon: 'pi pi-search', route: '/search' },
  { separator: true },
  { label: 'Users', icon: 'pi pi-user', route: '/users', permission: 'users:read' },
  { label: 'Groups', icon: 'pi pi-users', route: '/groups', permission: 'groups:read' },
  { label: 'Computers', icon: 'pi pi-desktop', route: '/computers', permission: 'users:read' },
  { label: 'Domain Join', icon: 'pi pi-desktop', route: '/domain-join', permission: 'users:write' },
  { label: 'Join Verification', icon: 'pi pi-verified', route: '/join-verification', permission: 'users:read' },
  { label: 'Computer Pre-staging', icon: 'pi pi-plus-circle', route: '/computer-prestaging', permission: 'users:write' },
  { label: 'OUs', icon: 'pi pi-folder', route: '/ous', permission: 'ous:manage' },
  { separator: true },
  { label: 'Group Policy', icon: 'pi pi-file-edit', route: '/gpos', permission: 'gpo:manage' },
  { label: 'Password Policies', icon: 'pi pi-shield', route: '/password-policies', permission: 'config:manage' },
  { label: 'Account Lockout', icon: 'pi pi-lock', route: '/account-lockout', permission: 'config:manage' },
  { label: 'Service Accounts', icon: 'pi pi-key', route: '/service-accounts', permission: 'users:read' },
  { label: 'gMSA Management', icon: 'pi pi-key', route: '/gmsa', permission: 'users:read' },
  { label: 'MFA Management', icon: 'pi pi-shield', route: '/mfa', permission: 'config:manage' },
  { label: 'FIDO2 / WebAuthn', icon: 'pi pi-key', route: '/fido2', permission: 'config:manage' },
  { label: 'Conditional Access', icon: 'pi pi-shield', route: '/conditional-access', permission: 'config:manage' },
  { label: 'Smart Card / PIV', icon: 'pi pi-id-card', route: '/smartcard', permission: 'config:manage' },
  { label: 'Privileged Access (PAM)', icon: 'pi pi-bolt', route: '/pam', permission: 'config:manage' },
  { label: 'RADIUS Server', icon: 'pi pi-wifi', route: '/radius', permission: 'config:manage' },
  { label: 'SSH Key Management', icon: 'pi pi-key', route: '/ssh-keys', permission: 'config:manage' },
  { label: 'SSPR Settings', icon: 'pi pi-unlock', route: '/sspr-settings', permission: 'config:manage' },
  { label: 'Certificate Services', icon: 'pi pi-lock', route: '/certificates', permission: 'certificates:manage' },
  { label: 'TLS / LDAPS Certificate', icon: 'pi pi-shield', route: '/certificates/tls', permission: 'certificates:manage' },
  { label: 'Delegated Admin', icon: 'pi pi-id-card', route: '/delegation', permission: 'config:manage' },
  { separator: true },
  { label: 'OAuth / OIDC Clients', icon: 'pi pi-globe', route: '/oauth-clients', permission: 'config:manage' },
  { label: 'SAML Providers', icon: 'pi pi-globe', route: '/saml-providers', permission: 'config:manage' },
  { separator: true },
  { label: 'Recycle Bin', icon: 'pi pi-trash', route: '/recyclebin' },
  { label: 'Audit Log', icon: 'pi pi-history', route: '/audit', permission: 'audit:read' },
  { label: 'LDAP Protocol Audit', icon: 'pi pi-list', route: '/ldap-audit', permission: 'audit:read' },
  { separator: true },
  { label: 'DNS Manager', icon: 'pi pi-globe', route: '/dns', permission: 'dns:manage' },
  { label: 'Sites & Services', icon: 'pi pi-map', route: '/sites', permission: 'sites:manage' },
  { label: 'Trusts', icon: 'pi pi-link', route: '/trusts', permission: 'config:manage' },
  { label: 'Replication', icon: 'pi pi-sync', route: '/replication', permission: 'config:manage' },
  { label: 'SYSVOL', icon: 'pi pi-folder', route: '/sysvol', permission: 'config:manage' },
  { label: 'RODC Settings', icon: 'pi pi-eye', route: '/rodc', permission: 'config:manage' },
  { label: 'DC Management', icon: 'pi pi-server', route: '/dc-management', permission: 'config:manage' },
  { separator: true },
  { label: 'Schema Browser', icon: 'pi pi-database', route: '/schema', permission: 'schema:manage' },
  { label: 'Domain Config', icon: 'pi pi-cog', route: '/config', permission: 'config:manage' },
  { separator: true },
  { label: 'Cluster Settings', icon: 'pi pi-sliders-h', route: '/settings/cluster', permission: 'config:manage' },
  { label: 'Node Settings', icon: 'pi pi-server', route: '/settings/nodes', permission: 'config:manage' },
  { label: 'Config Status', icon: 'pi pi-check-circle', route: '/settings/status', permission: 'config:manage' },
  { label: 'Service Settings', icon: 'pi pi-sliders-h', route: '/service-settings', permission: 'config:manage' },
  { separator: true },
  { label: 'Backup & Export', icon: 'pi pi-cloud-download', route: '/backup', permission: 'backup:manage' },
  { label: 'LDIF Import/Export', icon: 'pi pi-file-import', route: '/ldif', permission: 'config:manage' },
  { separator: true },
  { label: 'Scheduled Tasks', icon: 'pi pi-clock', route: '/scheduled-tasks', permission: 'config:manage' },
  { label: 'Webhooks', icon: 'pi pi-bell', route: '/webhooks', permission: 'config:manage' },
  { separator: true },
  { label: 'SCIM Integrations', icon: 'pi pi-cloud', route: '/scim-integrations', permission: 'config:manage' },
  { label: 'HR System Sync', icon: 'pi pi-sync', route: '/hr-sync', permission: 'config:manage' },
  { label: 'Lifecycle Workflows', icon: 'pi pi-sitemap', route: '/workflows', permission: 'config:manage' },
  { separator: true },
  { label: 'Migration Wizard', icon: 'pi pi-upload', route: '/migration', permission: 'config:manage' },
  { label: 'LDAP Proxy', icon: 'pi pi-arrows-h', route: '/ldap-proxy', permission: 'config:manage' },
  { label: 'MDM Integrations', icon: 'pi pi-mobile', route: '/mdm', permission: 'config:manage' },
  { label: 'Multi-Region', icon: 'pi pi-globe', route: '/multi-region', permission: 'config:manage' },
  { separator: true },
  { label: 'Compliance Reports', icon: 'pi pi-chart-bar', route: '/compliance', permission: 'compliance:manage' },
  { label: 'Access Reviews', icon: 'pi pi-check-square', route: '/access-reviews', permission: 'compliance:manage' },
  { label: 'Data Retention', icon: 'pi pi-clock', route: '/data-retention', permission: 'compliance:manage' },
]

// Filter menu items based on user permissions, also removing orphan separators
const menuItems = computed(() => {
  const filtered = allMenuItems.filter((item) => {
    if (item.separator) return true // keep separators initially
    if (!item.permission) return true // no permission required
    return authStore.hasPermission(item.permission)
  })

  // Remove leading, trailing, and consecutive separators
  const result: MenuItem[] = []
  for (let i = 0; i < filtered.length; i++) {
    const item = filtered[i]
    if (item.separator) {
      // Skip if first, last, or previous was also a separator
      if (result.length === 0) continue
      if (i === filtered.length - 1) continue
      if (result[result.length - 1]?.separator) continue
    }
    result.push(item)
  }
  // Remove trailing separator if any
  if (result.length > 0 && result[result.length - 1]?.separator) {
    result.pop()
  }
  return result
})

function isActive(itemRoute: string | undefined): boolean {
  if (!itemRoute) return false
  if (itemRoute === '/') return route.path === '/'
  return route.path === itemRoute || route.path.startsWith(itemRoute + '/')
}

function navigate(itemRoute: string | undefined) {
  if (!itemRoute) return
  router.push(itemRoute)
}
</script>

<template>
  <!-- Loading spinner while checking setup or auth status -->
  <div v-if="setupStore.loading || (setupStore.isProvisioned && authStore.loading && !authStore.checked)" class="loading-screen">
    <ProgressSpinner strokeWidth="3" />
    <p>Checking configuration...</p>
  </div>

  <!-- Setup wizard if not provisioned -->
  <SetupWizardView v-else-if="!setupStore.isProvisioned" />

  <!-- Login page when not authenticated -->
  <LoginView v-else-if="!authStore.isAuthenticated" />

  <!-- Normal app layout if provisioned and authenticated -->
  <div v-else class="app-layout">
    <!-- Backdrop for mobile sidebar overlay -->
    <div
      v-if="sidebarOpen"
      class="sidebar-backdrop"
      @click="closeSidebar"
    />

    <aside class="app-sidebar" :class="{ 'sidebar-open': sidebarOpen }">
      <div class="app-logo">
        <i class="pi pi-server"></i>
        <span class="app-title">AD Manager</span>
        <span style="flex: 1"></span>
        <Button
          :icon="theme.isDark ? 'pi pi-sun' : 'pi pi-moon'"
          text
          rounded
          @click="theme.toggle()"
          v-tooltip="theme.isDark ? 'Switch to light mode' : 'Switch to dark mode'"
          style="color: inherit"
        />
      </div>
      <!-- Logged-in user info and logout -->
      <div v-if="authStore.isAuthenticated" class="sidebar-user">
        <i class="pi pi-user sidebar-user-icon"></i>
        <span class="sidebar-user-name" :title="authStore.user?.dn">
          {{ authStore.user?.displayName || authStore.user?.username }}
        </span>
        <Button
          icon="pi pi-sign-out"
          text
          rounded
          size="small"
          @click="handleLogout"
          v-tooltip="'Sign out'"
          aria-label="Sign out"
          style="color: inherit; margin-left: auto"
        />
      </div>

      <nav class="sidebar-nav">
        <template v-for="(item, index) in menuItems" :key="index">
          <hr v-if="item.separator" class="nav-separator" />
          <a
            v-else
            class="nav-item"
            :class="{ active: isActive(item.route) }"
            @click.prevent="navigate(item.route)"
            :href="item.route"
            :title="item.label"
          >
            <i :class="item.icon" class="nav-item-icon"></i>
            <span class="nav-item-label">{{ item.label }}</span>
          </a>
        </template>
      </nav>
    </aside>

    <main class="app-content">
      <!-- Mobile header bar -->
      <div class="mobile-header">
        <Button
          icon="pi pi-bars"
          text
          rounded
          class="hamburger-btn"
          @click="toggleSidebar"
          v-tooltip="'Toggle menu (Ctrl+B)'"
          aria-label="Toggle sidebar"
        />
        <span class="mobile-logo">
          <i class="pi pi-server" style="color: var(--app-accent-color)"></i>
          AD Manager
        </span>
        <span style="flex: 1"></span>
        <Button
          :icon="theme.isDark ? 'pi pi-sun' : 'pi pi-moon'"
          text
          rounded
          @click="theme.toggle()"
          style="color: inherit"
        />
      </div>

      <!-- Global search bar -->
      <div class="global-search-bar">
        <IconField>
          <InputIcon class="pi pi-search" />
          <InputText
            ref="searchInput"
            v-model="searchValue"
            placeholder="Search... (Ctrl+K or /)"
            class="global-search-input"
          />
        </IconField>
      </div>

      <!-- Breadcrumb -->
      <AppBreadcrumb />

      <ErrorBoundary>
        <router-view />
      </ErrorBoundary>
    </main>
  </div>

  <Toast position="top-right" />
  <ConfirmDialog />
</template>

<style>
*,
*::before,
*::after {
  box-sizing: border-box;
}

html, body {
  height: 100%;
  margin: 0;
  padding: 0;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
  -webkit-font-smoothing: antialiased;
}

/* Sidebar tokens */
:root {
  --app-sidebar-bg: var(--p-surface-card);
  --app-sidebar-border: var(--p-surface-border);
  --app-sidebar-shadow: 0 0 8px rgba(0, 0, 0, 0.06);
  --app-sidebar-logo-color: var(--p-text-color);
  --app-sidebar-logo-icon: var(--app-accent-color);
  --app-sidebar-logo-border: var(--p-surface-border);
  --app-sidebar-item-color: var(--p-text-muted-color);
  --app-sidebar-item-hover-color: var(--p-text-color);
  --app-sidebar-item-hover-bg: var(--p-surface-ground);
  --app-sidebar-item-hover-accent: var(--app-accent-color);
  --app-sidebar-icon-color: var(--p-text-muted-color);
  --app-sidebar-separator: var(--p-surface-border);
  --app-sidebar-width: 270px;
  --app-sidebar-collapsed-width: 56px;
}

html.dark-mode {
  --app-sidebar-bg: linear-gradient(180deg, #0f172a 0%, #1e293b 100%);
  --app-sidebar-border: rgba(99, 102, 241, 0.15);
  --app-sidebar-shadow: 2px 0 12px rgba(0, 0, 0, 0.2);
  --app-sidebar-logo-color: #f8fafc;
  --app-sidebar-logo-icon: #818cf8;
  --app-sidebar-logo-border: rgba(148, 163, 184, 0.12);
  --app-sidebar-item-color: #94a3b8;
  --app-sidebar-item-hover-color: #e2e8f0;
  --app-sidebar-item-hover-bg: rgba(99, 102, 241, 0.1);
  --app-sidebar-item-hover-accent: #818cf8;
  --app-sidebar-icon-color: #64748b;
  --app-sidebar-separator: rgba(148, 163, 184, 0.1);
}

/* Semantic color tokens for light mode */
:root {
  --app-success-bg: #f0fdf4;
  --app-success-border: #bbf7d0;
  --app-success-text: #16a34a;
  --app-success-text-strong: #15803d;
  --app-danger-bg: #fef2f2;
  --app-danger-border: #fecaca;
  --app-danger-text: #dc2626;
  --app-danger-text-strong: #991b1b;
  --app-warn-bg: #fffbeb;
  --app-warn-border: #fef3c7;
  --app-warn-text: #d97706;
  --app-warn-text-strong: #92400e;
  --app-info-bg: #eff6ff;
  --app-info-border: #bfdbfe;
  --app-info-text: #2563eb;
  --app-info-text-strong: #1e40af;
  --app-accent-color: #6366f1;
  --app-accent-bg: #eef2ff;
  --app-neutral-bg: #f1f5f9;
  --app-neutral-border: #e2e8f0;
  --app-stat-blue-bg: #dbeafe;
  --app-stat-blue-text: #2563eb;
  --app-stat-green-bg: #dcfce7;
  --app-stat-green-text: #16a34a;
  --app-stat-purple-bg: #f3e8ff;
  --app-stat-purple-text: #9333ea;
  --app-stat-amber-bg: #fef3c7;
  --app-stat-amber-text: #d97706;
}

/* Semantic color tokens for dark mode */
html.dark-mode {
  color-scheme: dark;
  --app-success-bg: #052e16;
  --app-success-border: #166534;
  --app-success-text: #4ade80;
  --app-success-text-strong: #86efac;
  --app-danger-bg: #450a0a;
  --app-danger-border: #991b1b;
  --app-danger-text: #f87171;
  --app-danger-text-strong: #fca5a5;
  --app-warn-bg: #451a03;
  --app-warn-border: #92400e;
  --app-warn-text: #fbbf24;
  --app-warn-text-strong: #fcd34d;
  --app-info-bg: #172554;
  --app-info-border: #1e40af;
  --app-info-text: #60a5fa;
  --app-info-text-strong: #93c5fd;
  --app-accent-color: #818cf8;
  --app-accent-bg: #1e1b4b;
  --app-neutral-bg: var(--p-surface-ground);
  --app-neutral-border: var(--p-surface-border);
  --app-stat-blue-bg: #172554;
  --app-stat-blue-text: #60a5fa;
  --app-stat-green-bg: #052e16;
  --app-stat-green-text: #4ade80;
  --app-stat-purple-bg: #2e1065;
  --app-stat-purple-text: #c084fc;
  --app-stat-amber-bg: #451a03;
  --app-stat-amber-text: #fbbf24;
}

#app {
  height: 100%;
}

/* Loading screen */
.loading-screen {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100vh;
  gap: 1rem;
  background: var(--p-surface-ground);
}

.loading-screen p {
  color: var(--p-text-muted-color);
  font-size: 0.9375rem;
}

.app-layout {
  display: flex;
  height: 100vh;
  overflow: hidden;
  position: relative;
}

/* ─── Sidebar ─────────────────────────────────────────── */
.app-sidebar {
  width: var(--app-sidebar-width);
  min-width: var(--app-sidebar-width);
  background: var(--app-sidebar-bg);
  display: flex;
  flex-direction: column;
  border-right: 1px solid var(--app-sidebar-border);
  overflow-y: auto;
  overflow-x: hidden;
  box-shadow: var(--app-sidebar-shadow);
  transition: width 0.2s ease, min-width 0.2s ease, transform 0.25s ease;
  z-index: 200;
  flex-shrink: 0;
}

/* Tablet: icon-only collapsed sidebar */
@media (min-width: 768px) and (max-width: 1023px) {
  .app-sidebar {
    width: var(--app-sidebar-collapsed-width);
    min-width: var(--app-sidebar-collapsed-width);
  }

  .app-sidebar:hover,
  .app-sidebar.sidebar-open {
    width: var(--app-sidebar-width);
    min-width: var(--app-sidebar-width);
    box-shadow: 4px 0 20px rgba(0, 0, 0, 0.15);
  }

  .app-sidebar .app-title {
    opacity: 0;
    width: 0;
    overflow: hidden;
    transition: opacity 0.2s ease, width 0.2s ease;
  }

  .app-sidebar:hover .app-title,
  .app-sidebar.sidebar-open .app-title {
    opacity: 1;
    width: auto;
  }

  .app-sidebar .nav-item-label {
    opacity: 0;
    width: 0;
    overflow: hidden;
    white-space: nowrap;
    transition: opacity 0.15s ease, width 0.2s ease;
  }

  .app-sidebar:hover .nav-item-label,
  .app-sidebar.sidebar-open .nav-item-label {
    opacity: 1;
    width: auto;
  }

  .app-sidebar .nav-separator {
    width: 32px;
    margin: 0.4rem auto;
    transition: width 0.2s ease, margin 0.2s ease;
  }

  .app-sidebar:hover .nav-separator,
  .app-sidebar.sidebar-open .nav-separator {
    width: calc(100% - 2rem);
    margin: 0.5rem 1rem;
  }
}

/* Mobile: hidden by default, slides in as overlay */
@media (max-width: 767px) {
  .app-sidebar {
    position: fixed;
    top: 0;
    left: 0;
    height: 100%;
    width: var(--app-sidebar-width);
    min-width: var(--app-sidebar-width);
    transform: translateX(-100%);
    z-index: 300;
  }

  .app-sidebar.sidebar-open {
    transform: translateX(0);
    box-shadow: 4px 0 24px rgba(0, 0, 0, 0.25);
  }
}

/* ─── Sidebar backdrop (mobile overlay) ─────────────── */
.sidebar-backdrop {
  display: none;
}

@media (max-width: 767px) {
  .sidebar-backdrop {
    display: block;
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.45);
    z-index: 250;
    backdrop-filter: blur(2px);
    animation: fadeIn 0.2s ease;
  }
}

@keyframes fadeIn {
  from { opacity: 0 }
  to   { opacity: 1 }
}

/* ─── Logo area ──────────────────────────────────────── */
.app-logo {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1.5rem 1.25rem;
  border-bottom: 1px solid var(--app-sidebar-logo-border);
  font-size: 1.25rem;
  font-weight: 700;
  letter-spacing: -0.02em;
  color: var(--app-sidebar-logo-color);
  flex-shrink: 0;
  overflow: hidden;
}

.app-logo .pi {
  font-size: 1.625rem;
  color: var(--app-sidebar-logo-icon);
  flex-shrink: 0;
}

.app-title {
  white-space: nowrap;
}

/* ─── Sidebar user strip ──────────────────────────────── */
.sidebar-user {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.625rem 1.25rem;
  border-bottom: 1px solid var(--app-sidebar-separator);
  font-size: 0.8125rem;
  font-weight: 500;
  color: var(--app-sidebar-item-color);
  overflow: hidden;
  flex-shrink: 0;
}

.sidebar-user-icon {
  font-size: 0.9375rem;
  color: var(--app-accent-color);
  flex-shrink: 0;
}

.sidebar-user-name {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex: 1;
  min-width: 0;
}

/* ─── Custom nav ─────────────────────────────────────── */
.sidebar-nav {
  display: flex;
  flex-direction: column;
  padding: 0.75rem 0.5rem;
  overflow-y: auto;
  overflow-x: hidden;
  flex: 1;
}

.nav-item {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.5625rem 0.875rem;
  border-radius: 0.5rem;
  color: var(--app-sidebar-item-color);
  font-size: 0.9375rem;
  font-weight: 500;
  cursor: pointer;
  text-decoration: none;
  transition: background 0.15s ease, color 0.15s ease, box-shadow 0.15s ease;
  margin: 0.0625rem 0;
  overflow: hidden;
  white-space: nowrap;
}

.nav-item:hover {
  background: var(--app-sidebar-item-hover-bg);
  color: var(--app-sidebar-item-hover-color);
  box-shadow: inset 3px 0 0 var(--app-sidebar-item-hover-accent);
}

.nav-item.active {
  background: var(--app-accent-bg);
  color: var(--app-accent-color);
  box-shadow: inset 3px 0 0 var(--app-accent-color);
  font-weight: 600;
}

.nav-item-icon {
  font-size: 1.0625rem;
  color: inherit;
  flex-shrink: 0;
  width: 1.25rem;
  text-align: center;
  transition: color 0.15s ease;
}

.nav-item:hover .nav-item-icon {
  color: var(--app-sidebar-item-hover-accent);
}

.nav-item.active .nav-item-icon {
  color: var(--app-accent-color);
}

.nav-item-label {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
}

.nav-separator {
  border: none;
  border-top: 1px solid var(--app-sidebar-separator);
  margin: 0.5rem 1rem;
}

/* ─── Content area ───────────────────────────────────── */
.app-content {
  flex: 1;
  background: var(--p-surface-ground);
  overflow-y: auto;
  padding: 0 2rem 1.5rem;
  min-width: 0;
}

/* ─── Mobile header bar ──────────────────────────────── */
.mobile-header {
  display: none;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem 0;
  margin-bottom: 0.5rem;
  border-bottom: 1px solid var(--p-surface-border);
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--p-text-color);
  position: sticky;
  top: 0;
  background: var(--p-surface-ground);
  z-index: 100;
}

@media (max-width: 1023px) {
  .mobile-header {
    display: flex;
  }
}

.hamburger-btn {
  color: var(--p-text-color) !important;
}

.mobile-logo {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

/* ─── Global search ──────────────────────────────────── */
.global-search-bar {
  padding: 0.75rem 0 0.5rem;
}

.global-search-input {
  width: 100%;
  max-width: 480px;
}

@media (max-width: 1023px) {
  .app-content {
    padding: 0 1rem 1.5rem;
  }
}

/* ─── Global utility styles ──────────────────────────── */
.page-header {
  margin-bottom: 1.5rem;
}

.page-header h1 {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--p-text-color);
  margin-bottom: 0.25rem;
}

.page-header p {
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
}

.card {
  background: var(--p-surface-card);
  border: 1px solid var(--p-surface-border);
  border-radius: 0.75rem;
  padding: 1.5rem;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
}

.card-title {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: 1rem;
}

.toolbar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 1rem;
  flex-wrap: wrap;
}

.toolbar-spacer {
  flex: 1;
}

.stat-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.stat-card {
  background: var(--p-surface-card);
  border: 1px solid var(--p-surface-border);
  border-radius: 0.75rem;
  padding: 1.25rem 1.5rem;
  display: flex;
  align-items: center;
  gap: 1rem;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
}

.stat-icon {
  width: 3rem;
  height: 3rem;
  border-radius: 0.625rem;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1.25rem;
}

.stat-icon.blue { background: var(--app-stat-blue-bg); color: var(--app-stat-blue-text); }
.stat-icon.green { background: var(--app-stat-green-bg); color: var(--app-stat-green-text); }
.stat-icon.purple { background: var(--app-stat-purple-bg); color: var(--app-stat-purple-text); }
.stat-icon.amber { background: var(--app-stat-amber-bg); color: var(--app-stat-amber-text); }

.stat-value {
  font-size: 1.75rem;
  font-weight: 700;
  color: var(--p-text-color);
  line-height: 1;
}

.stat-label {
  font-size: 0.8125rem;
  color: var(--p-text-muted-color);
  margin-top: 0.125rem;
}

/* DataTable tweaks */
.p-datatable .p-datatable-thead > tr > th {
  font-weight: 600;
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.p-datatable .p-datatable-tbody > tr {
  cursor: pointer;
}

/* Make all PrimeVue dialogs resizable by dragging edges/corners */
.p-dialog {
  resize: both;
  overflow: hidden !important;
  min-width: 300px;
  min-height: 200px;
  max-width: 95vw;
  max-height: 90vh;
  display: flex !important;
  flex-direction: column !important;
  height: auto;
}

.p-dialog .p-dialog-content {
  overflow: auto !important;
  flex: 1 1 auto !important;
  min-height: 0 !important;
  display: flex;
  flex-direction: column;
}

/* Make Tabs fill dialog content height */
.p-dialog .p-dialog-content > .p-tabs {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.p-dialog .p-dialog-content > .p-tabs > .p-tabpanels {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.p-dialog .p-dialog-content > .p-tabs > .p-tabpanels > .p-tabpanel {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

/* Visible resize grip in bottom-right corner */
.p-dialog::after {
  content: '';
  position: absolute;
  bottom: 0;
  right: 0;
  width: 16px;
  height: 16px;
  cursor: nwse-resize;
  background:
    linear-gradient(135deg, transparent 50%, var(--p-text-muted-color) 50%, transparent 52%),
    linear-gradient(135deg, transparent 62%, var(--p-text-muted-color) 62%, transparent 64%),
    linear-gradient(135deg, transparent 74%, var(--p-text-muted-color) 74%, transparent 76%);
  opacity: 0.5;
  pointer-events: none;
  border-radius: 0 0 var(--p-dialog-border-radius, 6px) 0;
}
</style>
