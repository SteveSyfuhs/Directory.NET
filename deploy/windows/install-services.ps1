#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs Directory.NET as Windows services.

.DESCRIPTION
    Registers Directory.Server and Directory.Web as Windows services,
    configures automatic start, failure recovery, and firewall rules.

.PARAMETER ServerPath
    Path to the published Directory.Server binaries.

.PARAMETER WebPath
    Path to the published Directory.Web binaries.

.PARAMETER InstallDir
    Installation directory. Defaults to C:\Program Files\Directory.NET.

.PARAMETER ServiceAccount
    Service account to run under. Defaults to LocalSystem.

.EXAMPLE
    .\install-services.ps1 -ServerPath .\server-publish -WebPath .\web-publish
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ServerPath,

    [Parameter(Mandatory = $true)]
    [string]$WebPath,

    [string]$InstallDir = "C:\Program Files\Directory.NET",

    [string]$ServiceAccount = "LocalSystem"
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[*] $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "[+] $Message" -ForegroundColor Green
}

function Write-Failure {
    param([string]$Message)
    Write-Host "[-] $Message" -ForegroundColor Red
}

# Validate paths
if (-not (Test-Path $ServerPath)) {
    Write-Failure "Server package not found: $ServerPath"
    exit 1
}
if (-not (Test-Path $WebPath)) {
    Write-Failure "Web package not found: $WebPath"
    exit 1
}

# Step 1: Create installation directories
Write-Step "Creating installation directory: $InstallDir"
$serverDir = Join-Path $InstallDir "server"
$webDir = Join-Path $InstallDir "web"
$dataDir = Join-Path $InstallDir "data"
$certsDir = Join-Path $InstallDir "certs"

New-Item -ItemType Directory -Force -Path $serverDir | Out-Null
New-Item -ItemType Directory -Force -Path $webDir | Out-Null
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
New-Item -ItemType Directory -Force -Path $certsDir | Out-Null

# Step 2: Copy binaries
Write-Step "Copying Directory.Server binaries..."
Copy-Item -Path "$ServerPath\*" -Destination $serverDir -Recurse -Force

Write-Step "Copying Directory.Web binaries..."
Copy-Item -Path "$WebPath\*" -Destination $webDir -Recurse -Force

# Step 3: Stop existing services if running
$services = @("DirectoryNETServer", "DirectoryNETWeb")
foreach ($svc in $services) {
    $existing = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Step "Stopping existing service: $svc"
        Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        Write-Step "Removing existing service: $svc"
        sc.exe delete $svc | Out-Null
        Start-Sleep -Seconds 1
    }
}

# Step 4: Register Directory.Server service
Write-Step "Registering Directory.Server service..."
$serverExe = Join-Path $serverDir "Directory.Server.exe"

New-Service -Name "DirectoryNETServer" `
    -BinaryPathName $serverExe `
    -DisplayName "Directory.NET Server" `
    -Description "Directory.NET Server - LDAP, Kerberos, DNS, and RPC services" `
    -StartupType Automatic | Out-Null

# Configure failure recovery: restart on first, second, and third failure
sc.exe failure "DirectoryNETServer" reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null

# Set environment variables via registry
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\DirectoryNETServer"
$envMultiSz = @(
    "DOTNET_ENVIRONMENT=Production"
)
Set-ItemProperty -Path $regPath -Name "Environment" -Value $envMultiSz -Type MultiString

Write-Success "Directory.Server service registered."

# Step 5: Register Directory.Web service
Write-Step "Registering Directory.Web service..."
$webExe = Join-Path $webDir "Directory.Web.exe"

New-Service -Name "DirectoryNETWeb" `
    -BinaryPathName $webExe `
    -DisplayName "Directory.NET Web Portal" `
    -Description "Directory.NET Web Management Portal and REST API" `
    -StartupType Automatic | Out-Null

# Configure failure recovery
sc.exe failure "DirectoryNETWeb" reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null

# Set environment variables via registry
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\DirectoryNETWeb"
$envMultiSz = @(
    "DOTNET_ENVIRONMENT=Production",
    "ASPNETCORE_URLS=https://0.0.0.0:443;http://0.0.0.0:80"
)
Set-ItemProperty -Path $regPath -Name "Environment" -Value $envMultiSz -Type MultiString

# Add dependency on Directory.Server
sc.exe config "DirectoryNETWeb" depend= "DirectoryNETServer" | Out-Null

Write-Success "Directory.Web service registered."

# Step 6: Configure service account
if ($ServiceAccount -ne "LocalSystem") {
    Write-Step "Configuring service account: $ServiceAccount"
    sc.exe config "DirectoryNETServer" obj= $ServiceAccount | Out-Null
    sc.exe config "DirectoryNETWeb" obj= $ServiceAccount | Out-Null
}

# Step 7: Configure firewall rules
Write-Step "Configuring Windows Firewall rules..."

$firewallRules = @(
    @{ Name = "Directory.NET - LDAP";     Port = "389";       Protocol = "TCP" },
    @{ Name = "Directory.NET - LDAPS";    Port = "636";       Protocol = "TCP" },
    @{ Name = "Directory.NET - Kerberos"; Port = "88";        Protocol = "TCP" },
    @{ Name = "Directory.NET - Kerberos UDP"; Port = "88";    Protocol = "UDP" },
    @{ Name = "Directory.NET - DNS";      Port = "53";        Protocol = "TCP" },
    @{ Name = "Directory.NET - DNS UDP";  Port = "53";        Protocol = "UDP" },
    @{ Name = "Directory.NET - GC";       Port = "3268";      Protocol = "TCP" },
    @{ Name = "Directory.NET - GCS";      Port = "3269";      Protocol = "TCP" },
    @{ Name = "Directory.NET - Kpasswd";  Port = "464";       Protocol = "TCP" },
    @{ Name = "Directory.NET - Kpasswd UDP"; Port = "464";    Protocol = "UDP" },
    @{ Name = "Directory.NET - Web HTTPS"; Port = "443";      Protocol = "TCP" },
    @{ Name = "Directory.NET - Web HTTP";  Port = "80";       Protocol = "TCP" }
)

foreach ($rule in $firewallRules) {
    # Remove existing rule if present
    Remove-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue

    New-NetFirewallRule `
        -DisplayName $rule.Name `
        -Direction Inbound `
        -Action Allow `
        -Protocol $rule.Protocol `
        -LocalPort $rule.Port `
        -Profile Domain, Private `
        -Enabled True | Out-Null
}

Write-Success "Firewall rules configured."

# Step 8: Start services
Write-Step "Starting Directory.Server..."
Start-Service -Name "DirectoryNETServer"
Start-Sleep -Seconds 3

Write-Step "Starting Directory.Web..."
Start-Service -Name "DirectoryNETWeb"
Start-Sleep -Seconds 2

# Step 9: Verify
$serverStatus = (Get-Service -Name "DirectoryNETServer").Status
$webStatus = (Get-Service -Name "DirectoryNETWeb").Status

if ($serverStatus -eq "Running") {
    Write-Success "Directory.Server is running."
} else {
    Write-Failure "Directory.Server status: $serverStatus"
}

if ($webStatus -eq "Running") {
    Write-Success "Directory.Web is running."
} else {
    Write-Failure "Directory.Web status: $webStatus"
}

Write-Host ""
Write-Success "Installation complete!"
Write-Host ""
Write-Host "  Install directory: $InstallDir"
Write-Host "  Services:"
Write-Host "    DirectoryNETServer - $serverStatus"
Write-Host "    DirectoryNETWeb    - $webStatus"
Write-Host ""
Write-Host "  Management:"
Write-Host "    Get-Service DirectoryNETServer, DirectoryNETWeb"
Write-Host "    Get-EventLog -LogName Application -Source Directory*"
Write-Host ""
Write-Host "  Ports:"
Write-Host "    LDAP:     389 / 636 (TLS)"
Write-Host "    Kerberos: 88"
Write-Host "    DNS:      53"
Write-Host "    GC:       3268 / 3269 (TLS)"
Write-Host "    Web:      443 (HTTPS) / 80 (HTTP)"
