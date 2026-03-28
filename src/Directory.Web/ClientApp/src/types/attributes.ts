export interface FormattedAttribute {
  name: string
  syntaxOid: string
  syntaxName: string
  displayType: 'string' | 'dn' | 'sid' | 'datetime' | 'guid' | 'hex' | 'bool' | 'int' | 'flags' | 'interval' | 'security_descriptor'
  values: FormattedValue[]
  isWritable: boolean
  isMultiValued: boolean
  isConstructed: boolean
  isSystemOnly: boolean
  /** True if the attribute has a value set on the object */
  isValueSet: boolean
  /** True if this attribute is required (mustContain) for the object's class */
  isMustContain: boolean
  /** Schema range lower bound for validation */
  rangeLower?: number
  /** Schema range upper bound for validation */
  rangeUpper?: number
  /** Schema description of the attribute */
  description?: string
  /** Whether the attribute is indexed */
  isIndexed: boolean
  /** Whether the attribute is replicated to the Global Catalog */
  isInGlobalCatalog: boolean
}

export interface FormattedValue {
  rawValue: any
  displayValue: string
  resolvedName?: string
}

/** Schema attribute info returned by the class attributes endpoint */
export interface SchemaAttributeInfo {
  name: string
  oid: string
  syntaxOid: string
  syntaxName: string
  isSingleValued: boolean
  isSystemOnly: boolean
  isIndexed: boolean
  isInGlobalCatalog: boolean
  isMustContain: boolean
  rangeLower?: number
  rangeUpper?: number
  description?: string
  propertySet?: string
}

/** Well-known userAccountControl flags */
export const UAC_FLAGS: Record<number, string> = {
  0x0001: 'SCRIPT',
  0x0002: 'ACCOUNTDISABLE',
  0x0008: 'HOMEDIR_REQUIRED',
  0x0010: 'LOCKOUT',
  0x0020: 'PASSWD_NOTREQD',
  0x0040: 'PASSWD_CANT_CHANGE',
  0x0080: 'ENCRYPTED_TEXT_PWD_ALLOWED',
  0x0100: 'TEMP_DUPLICATE_ACCOUNT',
  0x0200: 'NORMAL_ACCOUNT',
  0x0800: 'INTERDOMAIN_TRUST_ACCOUNT',
  0x1000: 'WORKSTATION_TRUST_ACCOUNT',
  0x2000: 'SERVER_TRUST_ACCOUNT',
  0x10000: 'DONT_EXPIRE_PASSWD',
  0x20000: 'MNS_LOGON_ACCOUNT',
  0x40000: 'SMARTCARD_REQUIRED',
  0x80000: 'TRUSTED_FOR_DELEGATION',
  0x100000: 'NOT_DELEGATED',
  0x200000: 'USE_DES_KEY_ONLY',
  0x400000: 'DONT_REQ_PREAUTH',
  0x800000: 'PASSWORD_EXPIRED',
  0x1000000: 'TRUSTED_TO_AUTH_FOR_DELEGATION',
  0x04000000: 'PARTIAL_SECRETS_ACCOUNT',
}

/** Well-known groupType flags */
export const GROUP_TYPE_FLAGS: Record<number, string> = {
  0x00000001: 'BUILTIN_LOCAL_GROUP',
  0x00000002: 'ACCOUNT_GROUP',
  0x00000004: 'RESOURCE_GROUP',
  0x00000008: 'UNIVERSAL_GROUP',
  0x80000000: 'SECURITY_ENABLED',
}

/** Well-known sAMAccountType values */
export const SAM_ACCOUNT_TYPE_FLAGS: Record<number, string> = {
  0x00000000: 'DOMAIN_OBJECT',
  0x10000000: 'GROUP_OBJECT',
  0x10000001: 'NON_SECURITY_GROUP_OBJECT',
  0x20000000: 'ALIAS_OBJECT',
  0x20000001: 'NON_SECURITY_ALIAS_OBJECT',
  0x30000000: 'NORMAL_USER_ACCOUNT',
  0x30000001: 'MACHINE_ACCOUNT',
  0x30000002: 'TRUST_ACCOUNT',
  0x40000000: 'APP_BASIC_GROUP',
  0x40000001: 'APP_QUERY_GROUP',
}

/** systemFlags constants */
export const SYSTEM_FLAGS: Record<number, string> = {
  0x00000001: 'FLAG_ATTR_NOT_REPLICATED',
  0x00000002: 'FLAG_ATTR_REQ_PARTIAL_SET_MEMBER',
  0x00000004: 'FLAG_ATTR_IS_CONSTRUCTED',
  0x00000010: 'FLAG_ATTR_IS_OPERATIONAL',
  0x00000020: 'FLAG_SCHEMA_BASE_OBJECT',
  0x02000000: 'FLAG_ATTR_IS_RDN',
  0x04000000: 'FLAG_DOMAIN_DISALLOW_MOVE',
  0x08000000: 'FLAG_DOMAIN_DISALLOW_RENAME',
  0x40000000: 'FLAG_CONFIG_ALLOW_LIMITED_MOVE',
  0x80000000: 'FLAG_CONFIG_ALLOW_RENAME',
}

/**
 * Map attribute names to their known flag definitions.
 * Used by FlagEditor to display meaningful flag names.
 */
export const KNOWN_FLAG_ATTRIBUTES: Record<string, Record<number, string>> = {
  userAccountControl: UAC_FLAGS,
  groupType: GROUP_TYPE_FLAGS,
  sAMAccountType: SAM_ACCOUNT_TYPE_FLAGS,
  systemFlags: SYSTEM_FLAGS,
}

/** Well-known SIDs */
export const WELL_KNOWN_SIDS: Record<string, string> = {
  'S-1-0-0': 'Nobody',
  'S-1-1-0': 'Everyone',
  'S-1-2-0': 'Local',
  'S-1-3-0': 'Creator Owner',
  'S-1-3-1': 'Creator Group',
  'S-1-5-1': 'Dialup',
  'S-1-5-2': 'Network',
  'S-1-5-3': 'Batch',
  'S-1-5-4': 'Interactive',
  'S-1-5-6': 'Service',
  'S-1-5-7': 'Anonymous',
  'S-1-5-9': 'Enterprise Domain Controllers',
  'S-1-5-10': 'Self',
  'S-1-5-11': 'Authenticated Users',
  'S-1-5-13': 'Terminal Server Users',
  'S-1-5-14': 'Remote Interactive Logon',
  'S-1-5-18': 'Local System',
  'S-1-5-19': 'NT Authority\\Local Service',
  'S-1-5-20': 'NT Authority\\Network Service',
  'S-1-5-32-544': 'BUILTIN\\Administrators',
  'S-1-5-32-545': 'BUILTIN\\Users',
  'S-1-5-32-546': 'BUILTIN\\Guests',
  'S-1-5-32-547': 'BUILTIN\\Power Users',
  'S-1-5-32-548': 'BUILTIN\\Account Operators',
  'S-1-5-32-549': 'BUILTIN\\Server Operators',
  'S-1-5-32-550': 'BUILTIN\\Print Operators',
  'S-1-5-32-551': 'BUILTIN\\Backup Operators',
  'S-1-5-32-552': 'BUILTIN\\Replicators',
}
