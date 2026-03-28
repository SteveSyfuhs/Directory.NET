/**
 * Format a date string as a relative time (e.g., "2 hours ago").
 */
export function relativeTime(dateStr?: string | null): string {
  if (!dateStr) return ''
  const date = new Date(dateStr)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffSec = Math.floor(diffMs / 1000)
  const diffMin = Math.floor(diffSec / 60)
  const diffHr = Math.floor(diffMin / 60)
  const diffDay = Math.floor(diffHr / 24)

  if (diffSec < 60) return 'just now'
  if (diffMin < 60) return `${diffMin}m ago`
  if (diffHr < 24) return `${diffHr}h ago`
  if (diffDay < 7) return `${diffDay}d ago`
  if (diffDay < 30) return `${Math.floor(diffDay / 7)}w ago`
  return date.toLocaleDateString()
}

/**
 * Extract the CN (common name) from a DN string.
 */
export function cnFromDn(dn: string): string {
  const match = dn.match(/^(?:CN|OU)=([^,]+)/i)
  return match ? match[1] : dn
}

/**
 * Map an AD object class to an icon class name.
 */
export function objectClassIcon(objectClass: string): string {
  const map: Record<string, string> = {
    user: 'pi pi-user',
    computer: 'pi pi-desktop',
    group: 'pi pi-users',
    organizationalUnit: 'pi pi-folder',
    container: 'pi pi-folder-open',
    domain: 'pi pi-globe',
    domainDNS: 'pi pi-globe',
    builtinDomain: 'pi pi-shield',
    foreignSecurityPrincipal: 'pi pi-id-card',
    contact: 'pi pi-envelope',
    printQueue: 'pi pi-print',
    volume: 'pi pi-database',
    msDS_GroupManagedServiceAccount: 'pi pi-key',
  }
  return map[objectClass] || 'pi pi-box'
}

/**
 * Format Windows FILETIME (100ns ticks since 1601-01-01) to a readable date.
 */
export function formatFileTime(filetime?: number | null): string {
  if (!filetime || filetime <= 0 || filetime === 9223372036854775807) return 'Never'
  const epoch = BigInt(filetime) - BigInt('116444736000000000')
  const ms = Number(epoch / BigInt(10000))
  return new Date(ms).toLocaleString()
}

/**
 * Get the functional level display name.
 */
export function functionalLevelName(level: number): string {
  const map: Record<number, string> = {
    0: 'Windows 2000',
    1: 'Windows Server 2003 (interim)',
    2: 'Windows Server 2003',
    3: 'Windows Server 2008',
    4: 'Windows Server 2008 R2',
    5: 'Windows Server 2012',
    6: 'Windows Server 2012 R2',
    7: 'Windows Server 2016',
    10: 'Windows Server 2025',
  }
  return map[level] || `Level ${level}`
}
