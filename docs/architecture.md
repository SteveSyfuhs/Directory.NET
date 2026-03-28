# Directory.NET — Architecture

## High-Level Architecture

```
                          ┌────────────────────────────────────────────────────┐
                          │                   Clients                          │
                          │  Windows / Linux / macOS workstations & servers    │
                          │  Third-party LDAP clients, RADIUS NAS devices,     │
                          │  SCIM consumers, OAuth/SAML service providers      │
                          └───────────┬────────────┬───────────────────────────┘
                                      │            │
               ┌──────────────────────▼──┐   ┌────▼──────────────────────────┐
               │    Directory.Server     │   │      Directory.Web             │
               │   (headless worker)     │   │  (ASP.NET Core + Vue 3 SPA)   │
               │                         │   │                                │
               │  ┌─────────────────┐   │   │  REST API  /api/v1/...         │
               │  │  LDAP v3 / TLS  │   │   │  SCIM 2.0  /scim/v2/...        │
               │  │  port 389/636   │   │   │  OAuth 2.0 /api/v1/oauth/...   │
               │  ├─────────────────┤   │   │  SAML 2.0  /api/v1/saml/...    │
               │  │  Kerberos v5    │   │   │  OpenAPI   /openapi/v1.json     │
               │  │  port 88 / 464  │   │   │  Scalar UI /scalar/v1           │
               │  ├─────────────────┤   │   └─────────────────┬──────────────┘
               │  │  DNS            │   │                      │
               │  │  port 53        │   └──────────────────────┘
               │  ├─────────────────┤                         │
               │  │  MS-RPC/SAMR    │                         │
               │  │  NRPC / DRSR    │                         │
               │  │  port 1135/9389 │                         │
               │  └─────────────────┘                         │
               └──────────────┬──────────────────────────────-┘
                              │
               ┌──────────────▼───────────────────────────────┐
               │              Directory.Core                   │
               │  Domain model · Interfaces · Caching layer    │
               │  Directory store · Audit store                │
               └──────────────┬───────────────────────────────┘
                              │
               ┌──────────────▼───────────────────────────────┐
               │           Directory.CosmosDb                  │
               │  CosmosDirectoryStore · CosmosAuditStore      │
               │  CachedDirectoryStore (L1 memory + L2 Redis)  │
               │  CosmosClientHolder · DeferredCosmosDbInit    │
               └──────────────┬───────────────────────────────┘
                              │
               ┌──────────────▼───────────────────────────────┐
               │           Azure Cosmos DB                     │
               │  (NoSQL API — local emulator or cloud)        │
               └──────────────────────────────────────────────┘
```

---

## Data Flow — LDAP Operation

```
Client (TCP 389)
    │
    │  BER-encoded LDAPMessage (RFC 4511)
    ▼
Directory.Ldap — LdapServer
    │
    ├── BER decode  ──► LdapMessage (messageId, protocolOp)
    │
    ├── Bind (Simple / SASL NTLM / SASL GSSAPI)
    │       └── Directory.Security — NtlmAuthenticator / KerberosValidator
    │
    ├── Search
    │       ├── FilterParser  →  IDirectoryStore.SearchAsync(baseDn, scope, filter, attrs)
    │       │       └── CachedDirectoryStore
    │       │               ├── L1: IMemoryCache  (hot objects, ~5 s TTL)
    │       │               ├── L2: IDistributedCache / Redis  (warm objects, ~60 s TTL)
    │       │               └── CosmosDirectoryStore  (Cosmos DB point-read / query)
    │       └── AccessControlService.CheckReadPermission(entry, requestorToken)
    │
    ├── Modify / Add / Delete / ModifyDN
    │       ├── SchemaService.ValidateEntry(entry)
    │       ├── AccessControlService.CheckWritePermission(...)
    │       ├── CosmosDirectoryStore.SaveAsync(entry)
    │       ├── CacheInvalidationBus.Invalidate(dn)   ← Cosmos change-feed driven
    │       └── LdapAuditService.Record(operation)
    │
    └── BER encode response  ──► Client
```

---

## Data Flow — Kerberos Authentication

```
Client
    │  AS-REQ  (KDC_ERR_PREAUTH_REQUIRED → client retries with PA-ENC-TIMESTAMP)
    ▼
Directory.Kerberos — KerberosServer (built on Kerberos.NET)
    │
    ├── Decrypt PA-ENC-TIMESTAMP with principal's DES/AES key
    │       └── CosmosDirectoryStore.FindPrincipalAsync(upn)
    │               └── PasswordService.GetKerberosKeys(principal)
    │
    ├── Build TGT (Ticket-Granting Ticket)
    │       ├── PacGenerator.BuildPac(principal)   [MS-PAC]
    │       │       ├── KERB_VALIDATION_INFO  (groups, SIDs, logon info)
    │       │       └── ClaimsProvider.GetClaims(principal)  [MS-CTA]
    │       └── Encrypt TGT with krbtgt key
    │
    ├── AS-REP  ──► Client
    │
    │  TGS-REQ  (client presents TGT, requests service ticket)
    ▼
Directory.Kerberos — KerberosServer
    │
    ├── Validate TGT, extract PAC
    ├── S4U2Self / S4U2Proxy (constrained delegation) if requested
    ├── Build service ticket encrypted with service principal's key
    └── TGS-REP  ──► Client
```

---

## Cosmos DB Data Model

### Containers

| Container | Partition Key | Primary Content |
|---|---|---|
| `Entries` | `/partitionKey` (tenant/domain segment) | Directory entries (users, groups, computers, OUs, GPOs, …) |
| `Schema` | `/partitionKey` | Schema attribute and class definitions |
| `Audit` | `/partitionKey` (date-bucket) | Audit log records |
| `Configuration` | `/partitionKey` | Domain config, site/subnet topology, replication metadata |
| `Replication` | `/partitionKey` | USN watermarks, replication cursor table |
| `ScheduledTasks` | `/partitionKey` | Scheduled task definitions and run history |

### Partition Key Strategy

The partition key is derived from the DN's domain component suffix (e.g., `dc=corp,dc=example,dc=com`)
combined with a tenant identifier, giving per-domain isolation within a single Cosmos DB database.
Large containers (Entries, Audit) use a compound key `{tenantId}:{domainSegment}` to ensure even
distribution across physical partitions.

### Entry Document Structure

```jsonc
{
  "id": "<objectGuid>",
  "partitionKey": "default:dc=corp,dc=example,dc=com",
  "dn": "CN=Alice Smith,OU=Users,DC=corp,DC=example,DC=com",
  "objectClass": ["top", "person", "organizationalPerson", "user"],
  "objectGuid": "<guid>",
  "objectSid": "S-1-5-21-...",
  "usn": 12345,
  "usnChanged": 12350,
  "whenCreated": "2024-01-15T09:00:00Z",
  "whenChanged": "2024-06-01T14:22:00Z",
  "attributes": { ... },
  "_etag": "...",
  "_ts": 1717245720
}
```

---

## Caching Strategy

```
Read path:
  Request ──► CachedDirectoryStore
                  │
                  ├── L1 IMemoryCache (in-process)
                  │     TTL: 5 s for entries, 30 s for schema
                  │
                  ├── L2 IDistributedCache  (Redis when configured)
                  │     TTL: 60 s for entries, 5 min for schema
                  │
                  └── CosmosDirectoryStore  (authoritative)

Write / Invalidation path:
  Write ──► CosmosDirectoryStore.SaveAsync()
               └── Cosmos change feed ──► CacheInvalidationBus
                                               ├── Evict L1 (local)
                                               └── Evict L2 (Redis pub/sub)
```

Group expansion (transitive group membership) is handled by `GroupExpansionCache`, which
caches the expanded member list separately with a 2-minute TTL.

---

## Replication Design

Directory.NET uses a **change-feed pull** model instead of the traditional DRS push model:

1. All writes are committed to Cosmos DB with a monotonically increasing USN (Update Sequence Number).
2. Each DC maintains a **replication cursor table** in the `Replication` container recording the
   highest USN it has processed from every other DC.
3. The `SchemaReplicationService` polls the Cosmos DB change feed for new documents and applies
   them locally.
4. The `Directory.Replication` project also exposes an **HTTP DRS endpoint** (`/drs/`) on port 9389
   for compatibility with clients that perform explicit `DsGetNCChanges` calls.
5. The **KCC** (Knowledge Consistency Checker) analogue runs periodically to keep the replication
   topology healthy and produce `nTDSConnection` objects.

Multi-region active/active replication is handled by Cosmos DB's built-in multi-region writes.
Directory.NET layers AD-semantics (USN ordering, conflict resolution by timestamp) on top.

---

## Web Management Portal Architecture

```
Browser (Vue 3 + PrimeVue)
    │  HTTP/JSON  /api/v1/...
    ▼
Directory.Web  (ASP.NET Core Minimal API)
    │
    ├── Middleware pipeline
    │     ├── HTTPS redirection
    │     ├── CORS (Vite dev server origin)
    │     ├── Rate limiter (per-IP + mutation policies)
    │     ├── ProblemDetailsMiddleware  (RFC 7807)
    │     └── RodcMiddleware  (blocks writes in RODC mode)
    │
    ├── Endpoint groups  (/api/v1/<resource>)
    │     Mapped in Program.cs — each group lives in
    │     Directory.Web/Endpoints/<Resource>Endpoints.cs
    │
    ├── Setup wizard  (/api/v1/setup)
    │     ├── SetupStateService  (tracks first-run state)
    │     ├── WebDomainProvisioner  (creates schema + partitions)
    │     └── CosmosClientHolder  (deferred init, reconfigurable)
    │
    └── OpenAPI / Scalar
          ├── /openapi/v1.json   (always available)
          └── /scalar/v1         (development only)
```

The Vue 3 SPA is served as static files from `wwwroot/` in production. In development, the
`UseViteDevelopmentServer` middleware proxies requests to the Vite dev server on port 6173.

---

## Security Boundaries

- All write endpoints require authentication. The setup endpoints are the only unauthenticated routes.
- ACL checks (`AccessControlService`) are enforced at the store layer, not only at the HTTP layer.
- LDAP binds are audited via `LdapAuditService` (in-memory ring buffer, queryable via `/api/v1/ldap-audit`).
- Cosmos DB access uses a single connection string; row-level security is implemented in the
  store layer via partition key scoping per tenant/domain.
