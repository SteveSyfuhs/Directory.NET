import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { login as apiLogin, logout as apiLogout, getMe, type UserInfo } from '../api/auth'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<UserInfo | null>(null)
  const loading = ref(false)
  const checked = ref(false)

  const isAuthenticated = computed(() => user.value !== null)

  const roles = computed<string[]>(() => user.value?.roles ?? [])
  const permissions = computed<string[]>(() => user.value?.permissions ?? [])

  const isAdmin = computed(() =>
    roles.value.includes('DomainAdmin') || roles.value.includes('EnterpriseAdmin')
  )

  function hasRole(role: string): boolean {
    return roles.value.includes(role)
  }

  function hasPermission(perm: string): boolean {
    return permissions.value.includes(perm)
  }

  function hasAnyPermission(perms: string[]): boolean {
    return perms.some((p) => permissions.value.includes(p))
  }

  async function checkAuth(): Promise<void> {
    if (checked.value) return
    loading.value = true
    try {
      user.value = await getMe()
    } catch {
      user.value = null
    } finally {
      loading.value = false
      checked.value = true
    }
  }

  async function login(username: string, password: string): Promise<void> {
    const info = await apiLogin(username, password)
    user.value = info
    checked.value = true
  }

  async function logout(): Promise<void> {
    try {
      await apiLogout()
    } finally {
      user.value = null
      checked.value = false
    }
  }

  /** Clear local session state without calling the logout API (used on 401 expiry). */
  function clearSession(): void {
    user.value = null
    checked.value = false
  }

  return {
    user, loading, checked, isAuthenticated,
    roles, permissions, isAdmin,
    hasRole, hasPermission, hasAnyPermission,
    checkAuth, login, logout, clearSession,
  }
})
