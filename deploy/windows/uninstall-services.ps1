#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Uninstalls Directory.NET Windows services.

.DESCRIPTION
    Stops and removes Directory.Server and Directory.Web Windows services,
    removes firewall rules, and optionally removes installed binaries.

.PARAMETER RemoveFiles
    If specified, removes the installation directory and all binaries.

.PARAMETER InstallDir
    Installation directory. Defaults to C:\Program Files\Directory.NET.

.EXAMPLE
    .\uninstall-services.ps1
    .\uninstall-services.ps1 -RemoveFiles
#>

param(
    [switch]$RemoveFiles,
    [string]$InstallDir = "C:\Program Files\Directory.NET"
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

# Step 1: Stop and remove services
$services = @("DirectoryNETWeb", "DirectoryNETServer")

foreach ($svc in $services) {
    $existing = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Step "Stopping service: $svc"
        Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2

        Write-Step "Removing service: $svc"
        sc.exe delete $svc | Out-Null
        Write-Success "Service removed: $svc"
    } else {
        Write-Step "Service not found (already removed): $svc"
    }
}

# Step 2: Remove firewall rules
Write-Step "Removing firewall rules..."

$ruleNames = @(
    "Directory.NET - LDAP",
    "Directory.NET - LDAPS",
    "Directory.NET - Kerberos",
    "Directory.NET - Kerberos UDP",
    "Directory.NET - DNS",
    "Directory.NET - DNS UDP",
    "Directory.NET - GC",
    "Directory.NET - GCS",
    "Directory.NET - Kpasswd",
    "Directory.NET - Kpasswd UDP",
    "Directory.NET - Web HTTPS",
    "Directory.NET - Web HTTP"
)

foreach ($name in $ruleNames) {
    Remove-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue
}

Write-Success "Firewall rules removed."

# Step 3: Optionally remove files
if ($RemoveFiles) {
    if (Test-Path $InstallDir) {
        Write-Step "Removing installation directory: $InstallDir"
        Remove-Item -Path $InstallDir -Recurse -Force
        Write-Success "Installation directory removed."
    } else {
        Write-Step "Installation directory not found: $InstallDir"
    }
} else {
    Write-Host ""
    Write-Host "  Binaries were not removed. To remove them, run:"
    Write-Host "    .\uninstall-services.ps1 -RemoveFiles"
}

Write-Host ""
Write-Success "Uninstallation complete."
