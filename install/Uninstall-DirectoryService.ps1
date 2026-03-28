#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Uninstalls Directory.NET services and firewall rules.
#>
param(
    [string]$InstallPath = "C:\Program Files\DirectoryNET",
    [switch]$RemoveFiles
)

& "$PSScriptRoot\Install-DirectoryService.ps1" -Uninstall -InstallPath $InstallPath

if ($RemoveFiles -and (Test-Path $InstallPath)) {
    Remove-Item $InstallPath -Recurse -Force
    Write-Host "  ✓ Removed $InstallPath" -ForegroundColor Green
}
