<#
.SYNOPSIS
    Builds a self-contained deployment package for Directory.NET.
.DESCRIPTION
    Publishes both server and web projects as self-contained executables
    and packages them with the installer script into a ZIP file.
.PARAMETER Runtime
    Target runtime identifier. Default: win-x64
.PARAMETER Configuration
    Build configuration. Default: Release
.PARAMETER OutputPath
    Output directory for the package. Default: ./dist
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutputPath = "./dist"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

Write-Host "Building Directory.NET deployment package..." -ForegroundColor Cyan
Write-Host "  Runtime: $Runtime" -ForegroundColor Gray
Write-Host "  Configuration: $Configuration" -ForegroundColor Gray

# Clean
$stagingDir = Join-Path $OutputPath "DirectoryNET"
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

# Publish server
Write-Host "`nPublishing Directory.Server..." -ForegroundColor Cyan
dotnet publish "$repoRoot/src/Directory.Server/Directory.Server.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "$stagingDir/server"

# Publish web
Write-Host "`nPublishing Directory.Web..." -ForegroundColor Cyan
dotnet publish "$repoRoot/src/Directory.Web/Directory.Web.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "$stagingDir/web"

# Copy installer scripts
Write-Host "`nPackaging installer..." -ForegroundColor Cyan
$installDir = Join-Path $repoRoot "install"
Copy-Item "$installDir/Install-DirectoryService.ps1" $stagingDir
Copy-Item "$installDir/Uninstall-DirectoryService.ps1" $stagingDir -ErrorAction SilentlyContinue

# Create ZIP
$zipPath = Join-Path $OutputPath "DirectoryNET-$Runtime.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath }
Compress-Archive -Path "$stagingDir\*" -DestinationPath $zipPath

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "`n✓ Package created: $zipPath ($zipSize MB)" -ForegroundColor Green
Write-Host "  Deploy by extracting and running:" -ForegroundColor Gray
Write-Host "  .\Install-DirectoryService.ps1 -DomainName corp.example.com -UseLocalEmulator" -ForegroundColor Gray
