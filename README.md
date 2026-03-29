# This is 100% an AI Experiment

Please please please take note that I built this as an experiment to see the hard edges of AI coding agents. Do not in any way shape or form look at this and think it's something you should be running for anything other than chasing rabbits down holes.

All of this code is prompt driven. I have not written a single line and I haven't reviewed any of it for correctness.

# Directory.NET

A modern, cloud-native Active Directory-compatible directory service built on .NET 10 and Azure Cosmos DB.

Directory.NET implements the core AD DS protocols (LDAP v3, Kerberos v5, DNS, MS-RPC) with a
cloud-native backend, replacing the traditional NTDS.dit file store with Azure Cosmos DB for
global scale, multi-region replication, and operational simplicity.

---

## Architecture Overview

The solution is organised into 11 projects:

| Project | Type | Responsibility |
|---|---|---|
| `Directory.Core` | Library | Domain model, interfaces, caching abstractions, telemetry |
| `Directory.Schema` | Library | AD schema definitions, attribute syntax validation, OID registry |
| `Directory.Security` | Library | Kerberos PAC, ACL/DACL enforcement, certificate authority, OAuth 2.0/OIDC, SAML 2.0, FIDO2, MFA, RADIUS, PAM |
| `Directory.CosmosDb` | Library | Azure Cosmos DB data access layer, change-feed replication consumer |
| `Directory.Ldap` | Library | RFC 4511 LDAP v3 protocol implementation, filter evaluation, ASN.1 BER codec |
| `Directory.Kerberos` | Library | Kerberos v5 AS/TGS exchange, PAC generation, delegation support (built on Kerberos.NET) |
| `Directory.Dns` | Library | RFC 1035 DNS server, AD-integrated zone store, DNSSEC |
| `Directory.Rpc` | Library | MS-RPC/SAMR, MS-NRPC, MS-DRSR (DsGetNCChanges) stubs |
| `Directory.Replication` | Library | Multi-master replication engine, USN-based change tracking, KCC |
| `Directory.Server` | Worker service | Headless server host — binds LDAP, Kerberos, DNS, and RPC listeners |
| `Directory.Web` | ASP.NET Core | REST API + Vue 3 management portal, setup wizard, SCIM 2.0 provisioning |

The test project lives under `tests/Directory.Tests`.

---

## Technology Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| Data store | Azure Cosmos DB (SDK 3.x, NoSQL API) |
| Kerberos | Kerberos.NET 4.x |
| ASN.1 / BER | System.Formats.Asn1 |
| Async I/O | System.IO.Pipelines |
| XML/SAML crypto | System.Security.Cryptography.Xml |
| Observability | OpenTelemetry (traces + metrics via OTLP) |
| Frontend framework | Vue 3 + Vite |
| UI component library | PrimeVue 4.x |
| API documentation | Microsoft.AspNetCore.OpenApi + Scalar |
| Serialisation | System.Text.Json (default) + Newtonsoft.Json (Cosmos SDK) |
| Caching | IMemoryCache + IDistributedCache (Redis or in-memory) |
| Container runtime | Docker / docker-compose |

---

## Protocol Support

| Protocol | Standard | Port(s) | Notes |
|---|---|---|---|
| LDAP v3 | RFC 4511 | 389 / 636 (TLS) | Full search, modify, add, delete, compare, extended ops |
| Global Catalog | MS-ADTS | 3268 / 3269 (TLS) | Forest-wide attribute search |
| Kerberos v5 | RFC 4120 | 88 | AS, TGS, S4U2Self, S4U2Proxy, constrained delegation |
| Kpasswd | RFC 3244 | 464 | Password change over Kerberos |
| DNS | RFC 1035 | 53 (TCP+UDP) | AD-integrated zones, SRV records, DNSSEC |
| MS-RPC / SAMR | MS-SAMR | 135, 49664 | Account management RPC |
| MS-NRPC | MS-NRPC | 49664 | Secure channel, netlogon |
| MS-DRSR | MS-DRSR | 9389 | DsGetNCChanges replication |
| SCIM 2.0 | RFC 7644 | — | `/scim/v2` provisioning endpoint |
| RADIUS | RFC 2865 | 1812 | Network device authentication |
| OAuth 2.0 / OIDC | RFC 6749 + OIDC Core 1.0 | — | Identity provider |
| SAML 2.0 | OASIS SAML 2.0 | — | Identity provider |

---

## Feature Highlights

- **AD-compatible LDAP**: full v3 support including paged results, server-side sorting, VLV,
  persistent search, and LDAP over TLS (LDAPS).
- **Kerberos-first authentication**: AS/TGS with PAC generation, constrained delegation (S4U2Proxy),
  PKINIT (smart card), cross-realm trusts. NTLM is intentionally not supported — all authentication
  uses AES-based Kerberos (AES256-CTS, AES128-CTS). No MD4, RC4, or NT hash dependencies.
- **Fine-grained password policies**: per-OU/per-group PSO objects (MS-ADTS 3.1.1.5.2).
- **Case-insensitive directory queries**: LDAP filter evaluation and Cosmos DB queries use
  case-insensitive string comparison for attribute values and DN lookups.
- **Multi-factor authentication**: TOTP, FIDO2/WebAuthn, conditional risk-based MFA.
- **Certificate Authority**: internal CA with auto-enrollment and template management.
- **Group Managed Service Accounts** (gMSA): automated password rotation per MS-GMSAD.
- **RODC support**: read-only domain controller mode with write blocking middleware.
- **Multi-master replication**: USN-based change tracking, KCC-driven topology, Cosmos DB
  change feed as the replication transport.
- **SYSVOL** (cloud-native): GPO file distribution via Cosmos DB instead of DFS-R.
- **Delegated administration**: RBAC for the management portal.
- **Self-service password reset** (SSPR) and identity lifecycle workflows.
- **Compliance**: access reviews, data retention policies, audit logging.
- **Observability**: OpenTelemetry traces and metrics exported via OTLP.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10)
- [Node.js 20+](https://nodejs.org/) (for the Vue 3 management portal dev server)
- [Azure Cosmos DB Emulator](https://learn.microsoft.com/azure/cosmos-db/local-emulator) (local
  development) **or** an Azure Cosmos DB account (NoSQL API)
- Docker + docker-compose (optional, for containerised local dev)
- Redis (optional; the app falls back to an in-process distributed cache when not available)

---

## Quick Start

### 1. Clone

```bash
git clone https://github.com/SteveSyfuhs/Directory.NET.git
cd Directory.NET
```

### 2. Start the Cosmos DB Emulator

Start the [Azure Cosmos DB Emulator](https://learn.microsoft.com/azure/cosmos-db/local-emulator)
locally (Windows) or via Docker:

```bash
docker run -d -p 8081:8081 \
  -e AZURE_COSMOS_EMULATOR_PARTITION_COUNT=10 \
  mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest
```

### 3. Configure

The default `appsettings.json` files already point to the local emulator. To use a real Cosmos DB
account, set the connection string via environment variable or user-secrets:

```bash
# Management web portal
dotnet user-secrets set "CosmosDb:ConnectionString" "<your-connection-string>" \
  --project src/Directory.Web

# Headless server
dotnet user-secrets set "CosmosDb:ConnectionString" "<your-connection-string>" \
  --project src/Directory.Server
```

Update the domain defaults in `appsettings.json` (or override with environment variables):

```jsonc
"NamingContexts": {
  "DomainDn": "DC=corp,DC=example,DC=com",
  "ForestDnsName": "corp.example.com"
},
"DcNode": {
  "Hostname": "dc01",
  "SiteName": "Default-First-Site-Name"
}
```

### 4. Run the management portal

```bash
dotnet run --project src/Directory.Web
```

Navigate to `https://localhost:6001`. The first-run setup wizard will initialise the Cosmos DB
database and provision the domain.

### 5. Run the headless server (optional)

The headless `Directory.Server` binds the LDAP, Kerberos, DNS, and RPC listeners on standard ports
(requires elevated privileges for ports < 1024 on Linux/macOS):

```bash
sudo dotnet run --project src/Directory.Server
```

---

## Docker Quick Start

```bash
docker-compose up
```

This starts the Cosmos DB Emulator, Redis, `directory-server`, and `directory-web` together.
The management portal is available at `https://localhost:6001`.

### Building Images Individually

```bash
docker build -f Dockerfile.web   -t directory-web:latest   .
docker build -f Dockerfile.server -t directory-server:latest .
```

Both Dockerfiles use multi-stage builds with the .NET 10 SDK for the build stage. The web image
uses `mcr.microsoft.com/dotnet/aspnet:10.0` as the runtime base and includes the Vue 3 SPA built
by Node.js 20. The server image uses `mcr.microsoft.com/dotnet/runtime:10.0`.

---

## CI/CD

GitHub Actions workflows are included:

| Workflow | Trigger | Description |
|---|---|---|
| `build.yml` | Push to `main`/`develop`, PRs to `main` | Builds the solution, runs tests (Ubuntu + Windows matrix), type-checks the Vue frontend, uploads coverage |
| `release.yml` | Tag push (`v*`) | Publishes self-contained binaries (Windows x64, Linux x64), builds the frontend, creates GitHub Release with archives, builds and pushes Docker images to GHCR |

Docker images are published to `ghcr.io/stevesyfuhs/directory.net/web` and
`ghcr.io/stevesyfuhs/directory.net/server` on every release tag.

---

## API Documentation

When running in development mode the following API documentation endpoints are available:

| Endpoint | Description |
|---|---|
| `/openapi/v1.json` | OpenAPI 3.x specification (JSON) |
| `/scalar/v1` | Scalar interactive API reference UI |

In production the OpenAPI spec is still served at `/openapi/v1.json` but the Scalar UI is
disabled. To access the spec from a running container:

```bash
curl -sk https://localhost:6001/openapi/v1.json | python3 -m json.tool | head -40
```

---

## Configuration Reference

Key settings in `appsettings.json` (all overridable via environment variables using `__` as the
section separator, e.g. `CosmosDb__DatabaseName`):

| Setting | Default | Description |
|---|---|---|
| `CosmosDb:ConnectionString` | emulator key | Cosmos DB account connection string |
| `CosmosDb:DatabaseName` | `DirectoryService` | Cosmos DB database name |
| `CosmosDb:DefaultThroughput` | `1000` | Container provisioned RU/s |
| `NamingContexts:DomainDn` | — | Forest root DN, e.g. `DC=corp,DC=example,DC=com` |
| `NamingContexts:ForestDnsName` | — | Forest DNS name, e.g. `corp.example.com` |
| `NamingContexts:DomainSid` | — | Domain SID (assigned at provisioning time) |
| `DcNode:Hostname` | — | This DC's short hostname |
| `DcNode:SiteName` | `Default-First-Site-Name` | AD site assignment |
| `Ldap:Port` | `389` | LDAP clear-text port |
| `Ldap:TlsPort` | `636` | LDAPS port |
| `Kerberos:DefaultRealm` | — | Kerberos realm (uppercase DNS name) |
| `Dns:Port` | `53` | DNS listener port |
| `Cache:RedisConnectionString` | — | Redis connection string (optional) |
| `Replication:HttpPort` | `9389` | DRS HTTP replication port |

---

## Deployment

### Docker

See [Docker Quick Start](#docker-quick-start) above for building images.

### Kubernetes / Helm

A Helm chart is planned. In the interim, use the docker-compose definitions as a reference for
pod specifications. Key considerations:

- Mount a Kubernetes Secret for `CosmosDb__ConnectionString`.
- The server pod requires `NET_BIND_SERVICE` capability (or run as root) for ports 53, 88, 389.
- Use a `LoadBalancer` service for the LDAP, Kerberos, and DNS ports.
- The web pod only needs port 6001 and can run without elevated privileges.

### systemd (Linux)

```ini
[Unit]
Description=Directory.NET Web Portal
After=network.target

[Service]
WorkingDirectory=/opt/directory.net
ExecStart=/usr/bin/dotnet /opt/directory.net/Directory.Web.dll
Restart=always
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=CosmosDb__ConnectionString=<connection-string>

[Install]
WantedBy=multi-user.target
```

### Windows Service

```powershell
sc.exe create DirectoryWeb binPath= "dotnet C:\directory.net\Directory.Web.dll"
sc.exe start DirectoryWeb
```

The application integrates with the Windows Event Log when running as a service
(`DirectoryNET` log, `DirectoryNET-Web` source).

---

## Development

### Build

```bash
dotnet build src/src.sln
```

### Run tests

```bash
dotnet test tests/Directory.Tests/Directory.Tests.csproj
```

Tests run on both Windows and Linux. The project includes a managed MD4 implementation
(`Directory.Security.Md4`) so that NT hash computation works cross-platform without relying on
the operating system's crypto libraries.

### Frontend dev server

The Vue 3 SPA lives in `src/Directory.Web/ClientApp`. In development the ASP.NET Core server
proxies non-API requests to the Vite dev server automatically:

```bash
cd src/Directory.Web/ClientApp
npm install
npm run dev        # starts Vite on http://localhost:6173
```

Then run the backend in a second terminal:

```bash
dotnet run --project src/Directory.Web
```

The `UseViteDevelopmentServer(port: 6173)` middleware in `Program.cs` handles the proxy.

### Frontend type checking

The Vue frontend uses TypeScript with PrimeVue 4.x components. To run the type checker:

```bash
cd src/Directory.Web/ClientApp
npx vue-tsc --noEmit
```

---

## Project Structure

```
Directory.NET/
├── src/
│   ├── Directory.Core/          # Domain model, interfaces, caching, telemetry
│   ├── Directory.Schema/        # AD schema, attribute syntax, OID registry
│   ├── Directory.Security/      # Auth, ACL, CA, OAuth, SAML, FIDO2, MFA
│   ├── Directory.CosmosDb/      # Cosmos DB data access, change feed
│   ├── Directory.Ldap/          # LDAP v3 protocol, BER codec, filter engine
│   ├── Directory.Kerberos/      # Kerberos v5 AS/TGS, PAC, delegation
│   ├── Directory.Dns/           # DNS server, AD-integrated zones, DNSSEC
│   ├── Directory.Rpc/           # MS-RPC/SAMR/NRPC/DRSR stubs
│   ├── Directory.Replication/   # Multi-master replication engine
│   ├── Directory.Server/        # Headless worker host (LDAP/Kerberos/DNS/RPC)
│   └── Directory.Web/           # REST API + Vue 3 management portal
│       └── ClientApp/           # Vue 3 + Vite + PrimeVue 4.x SPA
├── tests/
│   └── Directory.Tests/         # Unit and integration tests (~987 tests)
├── docs/
│   └── architecture.md          # Architecture deep-dive
├── .github/
│   └── workflows/
│       ├── build.yml            # CI: build + test (Ubuntu & Windows matrix)
│       └── release.yml          # CD: publish binaries + Docker images to GHCR
├── Dockerfile.web               # Multi-stage Docker build for web portal
├── Dockerfile.server            # Multi-stage Docker build for headless server
├── docker-compose.yml
├── Directory.Build.props        # Shared MSBuild properties (target framework, etc.)
├── LICENSE                      # MIT License
└── README.md
```

---

## License

This project is licensed under the [MIT License](LICENSE).
