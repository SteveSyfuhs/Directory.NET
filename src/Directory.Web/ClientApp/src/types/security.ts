// Re-export security types from the canonical location
export type { SecurityDescriptorInfo, AceInfo, EffectivePermissions } from '../api/types'

export interface EffectivePermission {
  permission: string
  granted: boolean
}

export interface AceDto {
  type: 'allow' | 'deny'
  principalSid: string
  principalName?: string
  accessMask: number
  flags: string[]
  objectType?: string
  objectTypeName?: string
  inheritedObjectType?: string
  inheritedObjectTypeName?: string
  isObjectAce?: boolean
}

export interface DelegationTask {
  name: string
  description: string
  objectTypes?: string[]
  permissions: number
  objectTypeGuid?: string
}

/** AD access mask bit constants */
export const ACCESS_MASK = {
  // Directory service specific rights
  CREATE_CHILD: 0x00000001,
  DELETE_CHILD: 0x00000002,
  LIST_CONTENTS: 0x00000004,
  SELF: 0x00000008,
  READ_PROPERTY: 0x00000010,
  WRITE_PROPERTY: 0x00000020,
  DELETE_TREE: 0x00000040,
  LIST_OBJECT: 0x00000080,
  CONTROL_ACCESS: 0x00000100,

  // Standard rights
  DELETE: 0x00010000,
  READ_PERMISSIONS: 0x00020000,
  MODIFY_PERMISSIONS: 0x00040000,
  MODIFY_OWNER: 0x00080000,

  // Generic / combined
  FULL_CONTROL: 0x000F01FF,
  GENERIC_ALL: 0x10000000,
  GENERIC_EXECUTE: 0x20000000,
  GENERIC_WRITE: 0x40000000,
  GENERIC_READ: 0x80000000,
} as const

/** ACE flag constants */
export const ACE_FLAGS = {
  OBJECT_INHERIT: 'OBJECT_INHERIT',
  CONTAINER_INHERIT: 'CONTAINER_INHERIT',
  NO_PROPAGATE_INHERIT: 'NO_PROPAGATE_INHERIT',
  INHERIT_ONLY: 'INHERIT_ONLY',
  INHERITED_ACE: 'INHERITED_ACE',
} as const

/** "Applies to" scope presets mapping to ACE flag combinations */
export const APPLIES_TO_OPTIONS = [
  { label: 'This object only', flags: [], inheritOnly: false },
  { label: 'This object and all descendant objects', flags: ['CONTAINER_INHERIT'], inheritOnly: false },
  { label: 'All descendant objects only', flags: ['CONTAINER_INHERIT', 'INHERIT_ONLY'], inheritOnly: true },
  { label: 'Descendant user objects only', flags: ['CONTAINER_INHERIT', 'INHERIT_ONLY'], inheritOnly: true, inheritedObjectType: 'bf967aba-0de6-11d0-a285-00aa003049e2' },
  { label: 'Descendant group objects only', flags: ['CONTAINER_INHERIT', 'INHERIT_ONLY'], inheritOnly: true, inheritedObjectType: 'bf967a9c-0de6-11d0-a285-00aa003049e2' },
  { label: 'Descendant computer objects only', flags: ['CONTAINER_INHERIT', 'INHERIT_ONLY'], inheritOnly: true, inheritedObjectType: 'bf967a86-0de6-11d0-a285-00aa003049e2' },
  { label: 'Descendant OU objects only', flags: ['CONTAINER_INHERIT', 'INHERIT_ONLY'], inheritOnly: true, inheritedObjectType: 'bf967aa5-0de6-11d0-a285-00aa003049e2' },
] as const

/** Well-known schema GUIDs for object classes */
export const OBJECT_CLASS_GUIDS: Record<string, string> = {
  'bf967aba-0de6-11d0-a285-00aa003049e2': 'User',
  'bf967a9c-0de6-11d0-a285-00aa003049e2': 'Group',
  'bf967a86-0de6-11d0-a285-00aa003049e2': 'Computer',
  'bf967aa5-0de6-11d0-a285-00aa003049e2': 'Organizational Unit',
  'bf967a8b-0de6-11d0-a285-00aa003049e2': 'Contact',
}

/** Well-known extended rights / property set GUIDs */
export const EXTENDED_RIGHTS_GUIDS: Record<string, string> = {
  '00299570-246d-11d0-a768-00aa006e0529': 'User-Force-Change-Password',
  'ab721a53-1e2f-11d0-9819-00aa0040529b': 'User-Change-Password',
  'bf9679c0-0de6-11d0-a285-00aa003049e2': 'Self-Membership',
  '77b5b886-944a-11d1-aebd-0000f80367c1': 'Personal Information',
  'e45795b3-9455-11d1-aebd-0000f80367c1': 'Web Information',
  'e45795b2-9455-11d1-aebd-0000f80367c1': 'Phone and Mail Options',
  '59ba2f42-79a2-11d0-9020-00c04fc2d3cf': 'General Information',
  'e48d0154-bcf8-11d1-8702-00c04fb96050': 'Public Information',
  'bc0ac240-79a9-11d0-9020-00c04fc2d3cf': 'Membership',
  '4c164200-20c0-11d0-a768-00aa006e0529': 'Account Restrictions',
  '5f202010-79a5-11d0-9020-00c04fc2d3cf': 'Logon Information',
}

/** Standard delegation tasks for the Delegate Control wizard */
export const DELEGATION_TASKS: DelegationTask[] = [
  {
    name: 'Create, delete, and manage user accounts',
    description: 'Grants full control over user objects',
    objectTypes: ['user'],
    permissions: ACCESS_MASK.FULL_CONTROL,
    objectTypeGuid: 'bf967aba-0de6-11d0-a285-00aa003049e2',
  },
  {
    name: 'Reset user passwords and force password change at next logon',
    description: 'Grants password reset rights on user objects',
    objectTypes: ['user'],
    permissions: ACCESS_MASK.CONTROL_ACCESS,
    objectTypeGuid: '00299570-246d-11d0-a768-00aa006e0529',
  },
  {
    name: 'Read all user information',
    description: 'Grants read access to all properties on user objects',
    objectTypes: ['user'],
    permissions: ACCESS_MASK.READ_PROPERTY | ACCESS_MASK.LIST_CONTENTS | ACCESS_MASK.READ_PERMISSIONS | ACCESS_MASK.LIST_OBJECT,
    objectTypeGuid: 'bf967aba-0de6-11d0-a285-00aa003049e2',
  },
  {
    name: 'Create, delete and manage groups',
    description: 'Grants full control over group objects',
    objectTypes: ['group'],
    permissions: ACCESS_MASK.FULL_CONTROL,
    objectTypeGuid: 'bf967a9c-0de6-11d0-a285-00aa003049e2',
  },
  {
    name: 'Modify the membership of a group',
    description: 'Grants write access to the member attribute on groups',
    objectTypes: ['group'],
    permissions: ACCESS_MASK.WRITE_PROPERTY,
    objectTypeGuid: 'bc0ac240-79a9-11d0-9020-00c04fc2d3cf',
  },
  {
    name: 'Manage Group Policy links',
    description: 'Grants write access to gPLink and gPOptions attributes',
    objectTypes: ['organizationalUnit', 'domainDNS'],
    permissions: ACCESS_MASK.WRITE_PROPERTY,
  },
  {
    name: 'Create, delete and manage inetOrgPerson accounts',
    description: 'Grants full control over inetOrgPerson objects',
    objectTypes: ['inetOrgPerson'],
    permissions: ACCESS_MASK.FULL_CONTROL,
  },
  {
    name: 'Create, delete and manage computer accounts',
    description: 'Grants full control over computer objects',
    objectTypes: ['computer'],
    permissions: ACCESS_MASK.FULL_CONTROL,
    objectTypeGuid: 'bf967a86-0de6-11d0-a285-00aa003049e2',
  },
]

/** Permission display definitions for the ACE editor */
export const PERMISSION_DEFINITIONS = [
  { name: 'Full Control', mask: ACCESS_MASK.FULL_CONTROL, category: 'General' },
  { name: 'Read All Properties', mask: ACCESS_MASK.READ_PROPERTY, category: 'Properties' },
  { name: 'Write All Properties', mask: ACCESS_MASK.WRITE_PROPERTY, category: 'Properties' },
  { name: 'Create All Child Objects', mask: ACCESS_MASK.CREATE_CHILD, category: 'Children' },
  { name: 'Delete All Child Objects', mask: ACCESS_MASK.DELETE_CHILD, category: 'Children' },
  { name: 'Read Permissions', mask: ACCESS_MASK.READ_PERMISSIONS, category: 'Standard' },
  { name: 'Modify Permissions', mask: ACCESS_MASK.MODIFY_PERMISSIONS, category: 'Standard' },
  { name: 'Modify Owner', mask: ACCESS_MASK.MODIFY_OWNER, category: 'Standard' },
  { name: 'Delete', mask: ACCESS_MASK.DELETE, category: 'Standard' },
  { name: 'Delete Subtree', mask: ACCESS_MASK.DELETE_TREE, category: 'Standard' },
  { name: 'List Contents', mask: ACCESS_MASK.LIST_CONTENTS, category: 'Standard' },
  { name: 'List Object', mask: ACCESS_MASK.LIST_OBJECT, category: 'Standard' },
  { name: 'All Extended Rights', mask: ACCESS_MASK.CONTROL_ACCESS, category: 'Extended' },
  { name: 'All Validated Writes', mask: ACCESS_MASK.SELF, category: 'Extended' },
] as const
