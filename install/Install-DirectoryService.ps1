#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs Directory.NET Active Directory Service on Windows Server.
.DESCRIPTION
    This script performs a complete installation of the Directory.NET service including:
    - Pre-flight environment validation
    - .NET 9 runtime check
    - Port conflict detection and resolution
    - Windows Firewall rule creation
    - Windows Service registration
    - TLS certificate generation (optional)
    - Cosmos DB connectivity validation
.PARAMETER InstallPath
    Installation directory. Default: C:\Program Files\DirectoryNET
.PARAMETER CosmosConnectionString
    Azure Cosmos DB connection string. If not provided, uses local emulator.
.PARAMETER DomainName
    Domain DNS name (e.g., corp.example.com)
.PARAMETER SkipFirewall
    Skip firewall rule creation
.PARAMETER SkipServiceRegistration
    Skip Windows service registration
.PARAMETER UseLocalEmulator
    Use Cosmos DB local emulator connection string
#>
param(
    [string]$InstallPath = "C:\Program Files\DirectoryNET",
    [string]$CosmosConnectionString = "",
    [string]$DomainName = "",
    [switch]$SkipFirewall,
    [switch]$SkipServiceRegistration,
    [switch]$UseLocalEmulator,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$ServiceNameServer = "DirectoryServer"
$ServiceNameWeb = "DirectoryWeb"

# Color helpers
function Write-Step($msg) { Write-Host "`n▶ $msg" -ForegroundColor Cyan }
function Write-OK($msg) { Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "  ⚠ $msg" -ForegroundColor Yellow }
function Write-Err($msg) { Write-Host "  ✗ $msg" -ForegroundColor Red }

# ─── Uninstall ───
if ($Uninstall) {
    Write-Step "Uninstalling Directory.NET..."

    # Stop and remove services
    foreach ($svc in @($ServiceNameServer, $ServiceNameWeb)) {
        $service = Get-Service -Name $svc -ErrorAction SilentlyContinue
        if ($service) {
            if ($service.Status -eq "Running") {
                Write-Host "  Stopping $svc..."
                Stop-Service -Name $svc -Force
            }
            sc.exe delete $svc | Out-Null
            Write-OK "Removed service: $svc"
        }
    }

    # Remove firewall rules
    Get-NetFirewallRule -DisplayName "DirectoryNET*" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Write-OK "Removed firewall rules"

    # Remove Event Log sources
    foreach ($source in @("DirectoryNET-Server", "DirectoryNET-Web")) {
        if ([System.Diagnostics.EventLog]::SourceExists($source)) {
            [System.Diagnostics.EventLog]::DeleteEventSource($source)
            Write-OK "Removed Event Log source: $source"
        }
    }
    if ([System.Diagnostics.EventLog]::Exists("DirectoryNET")) {
        [System.Diagnostics.EventLog]::Delete("DirectoryNET")
        Write-OK "Removed Event Log: DirectoryNET"
    }

    Write-Host "`nUninstall complete. Installation files at '$InstallPath' were not removed." -ForegroundColor Green
    Write-Host "Delete the folder manually if no longer needed." -ForegroundColor Gray
    exit 0
}

# ─── Pre-flight checks ───
Write-Host "`n╔══════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Directory.NET Installer                     ║" -ForegroundColor Cyan
Write-Host "║   Active Directory Compatible Service         ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor Cyan

# Check OS
Write-Step "Checking prerequisites..."
$os = Get-CimInstance Win32_OperatingSystem
Write-OK "OS: $($os.Caption) ($($os.Version))"

# Check .NET 9
$dotnetVersions = dotnet --list-runtimes 2>&1
$hasNet9 = $dotnetVersions | Where-Object { $_ -match "Microsoft\.NETCore\.App 9\." -or $_ -match "Microsoft\.AspNetCore\.App 9\." }
if ($hasNet9) {
    Write-OK ".NET 9 runtime found"
} else {
    Write-Err ".NET 9 runtime not found. Install from https://dotnet.microsoft.com/download/dotnet/9.0"
    exit 1
}

# Check admin
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Err "This script must be run as Administrator"
    exit 1
}
Write-OK "Running as Administrator"

# ─── Windows Event Log ───
Write-Step "Configuring Windows Event Log..."
$logName = "DirectoryNET"
foreach ($source in @("DirectoryNET-Server", "DirectoryNET-Web")) {
    if (-not [System.Diagnostics.EventLog]::SourceExists($source)) {
        [System.Diagnostics.EventLog]::CreateEventSource($source, $logName)
        Write-OK "Created Event Log source: $source"
    } else {
        Write-OK "Event Log source exists: $source"
    }
}

# ─── Port conflict scan ───
Write-Step "Scanning for port conflicts..."

$ports = @(
    @{ Port = 389;   Proto = "TCP"; Service = "LDAP" },
    @{ Port = 636;   Proto = "TCP"; Service = "LDAPS" },
    @{ Port = 88;    Proto = "TCP"; Service = "Kerberos" },
    @{ Port = 53;    Proto = "TCP"; Service = "DNS" },
    @{ Port = 53;    Proto = "UDP"; Service = "DNS" },
    @{ Port = 1135;  Proto = "TCP"; Service = "RPC Endpoint Mapper" },
    @{ Port = 49664; Proto = "TCP"; Service = "RPC Service" },
    @{ Port = 3268;  Proto = "TCP"; Service = "Global Catalog" },
    @{ Port = 3269;  Proto = "TCP"; Service = "Global Catalog (TLS)" },
    @{ Port = 464;   Proto = "TCP"; Service = "Kpasswd" },
    @{ Port = 9389;  Proto = "TCP"; Service = "DRS Replication" },
    @{ Port = 6001;  Proto = "TCP"; Service = "Web Management Console" }
)

$conflicts = @()
foreach ($p in $ports) {
    $inUse = $false
    if ($p.Proto -eq "TCP") {
        $conn = Get-NetTCPConnection -LocalPort $p.Port -ErrorAction SilentlyContinue
        $inUse = $null -ne $conn
    } else {
        $conn = Get-NetUDPEndpoint -LocalPort $p.Port -ErrorAction SilentlyContinue
        $inUse = $null -ne $conn
    }

    if ($inUse) {
        $ownerPid = if ($conn) { ($conn | Select-Object -First 1).OwningProcess } else { "unknown" }
        $ownerProc = if ($ownerPid -and $ownerPid -ne "unknown") { (Get-Process -Id $ownerPid -ErrorAction SilentlyContinue).ProcessName } else { "unknown" }
        Write-Warn "Port $($p.Port)/$($p.Proto) ($($p.Service)) is in use by $ownerProc (PID: $ownerPid)"
        $conflicts += $p
    } else {
        Write-OK "Port $($p.Port)/$($p.Proto) ($($p.Service)) is available"
    }
}

# Special note about RPC
$rpc135 = Get-NetTCPConnection -LocalPort 135 -ErrorAction SilentlyContinue
if ($rpc135) {
    Write-Host "`n  ℹ Port 135/TCP is occupied by Windows RPC Endpoint Mapper (RpcSs)." -ForegroundColor Gray
    Write-Host "    Directory.NET uses port 1135 by default to avoid this conflict." -ForegroundColor Gray
}

if ($conflicts.Count -gt 0) {
    Write-Host "`n  ⚠ $($conflicts.Count) port conflict(s) detected. The affected services" -ForegroundColor Yellow
    Write-Host "    may fail to start. Adjust ports in appsettings.json if needed." -ForegroundColor Yellow

    $continue = Read-Host "`n  Continue anyway? (Y/N)"
    if ($continue -ne "Y" -and $continue -ne "y") { exit 0 }
}

# ─── Cosmos DB connectivity ───
Write-Step "Configuring Cosmos DB connection..."
if ($UseLocalEmulator) {
    $CosmosConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="
    Write-OK "Using local Cosmos DB Emulator"
} elseif ([string]::IsNullOrEmpty($CosmosConnectionString)) {
    Write-Warn "No Cosmos DB connection string provided."
    Write-Host "    Configure it later in $InstallPath\appsettings.json" -ForegroundColor Gray
}

# ─── Install files ───
Write-Step "Installing to $InstallPath..."
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
}

# Check if published binaries exist alongside the script
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverPublish = Join-Path $scriptDir "server"
$webPublish = Join-Path $scriptDir "web"

if ((Test-Path $serverPublish) -and (Test-Path $webPublish)) {
    Write-Host "  Copying server files..."
    Copy-Item -Path "$serverPublish\*" -Destination "$InstallPath\server\" -Recurse -Force
    Write-Host "  Copying web files..."
    Copy-Item -Path "$webPublish\*" -Destination "$InstallPath\web\" -Recurse -Force
    Write-OK "Files installed"
} else {
    Write-Warn "Published binaries not found alongside installer."
    Write-Host "    Run 'Build-Package.ps1' first, or manually publish:" -ForegroundColor Gray
    Write-Host "    dotnet publish src/Directory.Server -c Release -o install/server" -ForegroundColor Gray
    Write-Host "    dotnet publish src/Directory.Web -c Release -o install/web" -ForegroundColor Gray
}

# ─── Update configuration ───
Write-Step "Configuring service..."
$serverConfig = Join-Path $InstallPath "server\appsettings.json"
$webConfig = Join-Path $InstallPath "web\appsettings.json"

foreach ($configPath in @($serverConfig, $webConfig)) {
    if (Test-Path $configPath) {
        $config = Get-Content $configPath -Raw | ConvertFrom-Json

        if (-not [string]::IsNullOrEmpty($CosmosConnectionString)) {
            if ($config.CosmosDb) {
                $config.CosmosDb.ConnectionString = $CosmosConnectionString
            }
        }

        $config | ConvertTo-Json -Depth 10 | Set-Content $configPath
        Write-OK "Updated $configPath"
    }
}

# ─── Firewall rules ───
if (-not $SkipFirewall) {
    Write-Step "Creating Windows Firewall rules..."

    $fwRules = @(
        @{ Name = "DirectoryNET-LDAP";     Port = "389";   Proto = "TCP" },
        @{ Name = "DirectoryNET-LDAPS";    Port = "636";   Proto = "TCP" },
        @{ Name = "DirectoryNET-Kerberos"; Port = "88";    Proto = "TCP" },
        @{ Name = "DirectoryNET-KerbUDP";  Port = "88";    Proto = "UDP" },
        @{ Name = "DirectoryNET-DNS-TCP";  Port = "53";    Proto = "TCP" },
        @{ Name = "DirectoryNET-DNS-UDP";  Port = "53";    Proto = "UDP" },
        @{ Name = "DirectoryNET-RPC";      Port = "1135";  Proto = "TCP" },
        @{ Name = "DirectoryNET-RPCSvc";   Port = "49664"; Proto = "TCP" },
        @{ Name = "DirectoryNET-GC";       Port = "3268";  Proto = "TCP" },
        @{ Name = "DirectoryNET-GCTLS";    Port = "3269";  Proto = "TCP" },
        @{ Name = "DirectoryNET-Kpasswd";  Port = "464";   Proto = "TCP" },
        @{ Name = "DirectoryNET-KpwdUDP";  Port = "464";   Proto = "UDP" },
        @{ Name = "DirectoryNET-CLDAP";    Port = "389";   Proto = "UDP" },
        @{ Name = "DirectoryNET-DRS";      Port = "9389";  Proto = "TCP" },
        @{ Name = "DirectoryNET-WebHTTPS"; Port = "6001";  Proto = "TCP" }
    )

    # Remove existing rules first
    Get-NetFirewallRule -DisplayName "DirectoryNET*" -ErrorAction SilentlyContinue | Remove-NetFirewallRule

    foreach ($rule in $fwRules) {
        New-NetFirewallRule `
            -DisplayName $rule.Name `
            -Direction Inbound `
            -Protocol $rule.Proto `
            -LocalPort $rule.Port `
            -Action Allow `
            -Profile Domain,Private `
            -Description "Directory.NET AD Service" | Out-Null
        Write-OK "$($rule.Name) ($($rule.Port)/$($rule.Proto))"
    }
}

# ─── Register Windows services ───
if (-not $SkipServiceRegistration) {
    Write-Step "Registering Windows services..."

    $serverExe = Join-Path $InstallPath "server\Directory.Server.exe"
    $webExe = Join-Path $InstallPath "web\Directory.Web.exe"

    foreach ($svc in @(
        @{ Name = $ServiceNameServer; Exe = $serverExe; Display = "Directory.NET Server"; Desc = "Active Directory compatible protocol server (LDAP, Kerberos, DNS, RPC)" },
        @{ Name = $ServiceNameWeb; Exe = $webExe; Display = "Directory.NET Web Console"; Desc = "Web-based management console for Directory.NET" }
    )) {
        # Remove existing if present
        $existing = Get-Service -Name $svc.Name -ErrorAction SilentlyContinue
        if ($existing) {
            if ($existing.Status -eq "Running") { Stop-Service -Name $svc.Name -Force }
            sc.exe delete $svc.Name | Out-Null
            Start-Sleep -Seconds 1
        }

        if (Test-Path $svc.Exe) {
            sc.exe create $svc.Name binPath= "`"$($svc.Exe)`"" start= delayed-auto DisplayName= $svc.Display | Out-Null
            sc.exe description $svc.Name $svc.Desc | Out-Null
            sc.exe failure $svc.Name reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
            Write-OK "$($svc.Display) registered (delayed auto-start, auto-restart on failure)"
        } else {
            Write-Warn "$($svc.Exe) not found — skipping service registration"
        }
    }
}

# ─── TLS Certificate ───
Write-Step "Checking TLS certificate..."
$certPath = Join-Path $InstallPath "certs"
if (-not (Test-Path $certPath)) {
    New-Item -ItemType Directory -Path $certPath -Force | Out-Null
}

$pfxPath = Join-Path $certPath "directory-service.pfx"
if (-not (Test-Path $pfxPath)) {
    if (-not [string]::IsNullOrEmpty($DomainName)) {
        Write-Host "  Generating self-signed TLS certificate for $DomainName..."
        $cert = New-SelfSignedCertificate `
            -DnsName $DomainName, "localhost", "*.$DomainName" `
            -CertStoreLocation "Cert:\LocalMachine\My" `
            -FriendlyName "Directory.NET Service" `
            -NotAfter (Get-Date).AddYears(5) `
            -KeyAlgorithm RSA `
            -KeyLength 2048

        $pfxPassword = ConvertTo-SecureString -String "DirectoryNET" -Force -AsPlainText
        Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pfxPassword | Out-Null
        Write-OK "Self-signed certificate generated at $pfxPath"
        Write-Host "    ℹ For production, replace with a certificate from a trusted CA." -ForegroundColor Gray
    } else {
        Write-Warn "No domain name provided — skipping TLS certificate generation."
        Write-Host "    Provide -DomainName to generate a certificate, or place your own at $pfxPath" -ForegroundColor Gray
    }
} else {
    Write-OK "TLS certificate exists at $pfxPath"
}

# ─── Summary ───
Write-Host "`n╔══════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║   Installation Complete                       ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "  Install path:    $InstallPath" -ForegroundColor White
Write-Host "  Server config:   $InstallPath\server\appsettings.json" -ForegroundColor Gray
Write-Host "  Web config:      $InstallPath\web\appsettings.json" -ForegroundColor Gray
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor White
Write-Host "    1. Review and update appsettings.json files" -ForegroundColor Gray
Write-Host "    2. Start services:" -ForegroundColor Gray
Write-Host "       Start-Service DirectoryServer" -ForegroundColor Gray
Write-Host "       Start-Service DirectoryWeb" -ForegroundColor Gray
Write-Host "    3. Open https://localhost:6001 to configure the domain" -ForegroundColor Gray
Write-Host ""
