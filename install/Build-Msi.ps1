<#
.SYNOPSIS
    Builds the Directory.NET MSI installer package.
.DESCRIPTION
    Publishes both Directory.Server and Directory.Web projects as non-single-file
    self-contained deployments, then invokes the WiX v5 toolset to produce an MSI.
.PARAMETER Configuration
    Build configuration. Default: Release
.PARAMETER Runtime
    Target runtime identifier. Default: win-x64
.PARAMETER OutputPath
    Output directory for the MSI. Default: ./dist
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputPath = "./dist"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$msiProjectDir = Join-Path $repoRoot "install/msi"

Write-Host "`nBuilding Directory.NET MSI Installer..." -ForegroundColor Cyan
Write-Host "  Configuration: $Configuration" -ForegroundColor Gray
Write-Host "  Runtime:       $Runtime" -ForegroundColor Gray

# ── Publish Directory.Server ──
$serverOut = Join-Path $msiProjectDir "publish/server"
if (Test-Path $serverOut) { Remove-Item $serverOut -Recurse -Force }

Write-Host "`nPublishing Directory.Server..." -ForegroundColor Cyan
dotnet publish "$repoRoot/src/Directory.Server/Directory.Server.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $serverOut

if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to publish Directory.Server" -ForegroundColor Red
    exit 1
}

# ── Publish Directory.Web ──
$webOut = Join-Path $msiProjectDir "publish/web"
if (Test-Path $webOut) { Remove-Item $webOut -Recurse -Force }

Write-Host "`nPublishing Directory.Web..." -ForegroundColor Cyan
dotnet publish "$repoRoot/src/Directory.Web/Directory.Web.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $webOut

if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to publish Directory.Web" -ForegroundColor Red
    exit 1
}

# ── Build MSI ──
$outputDir = Join-Path $repoRoot $OutputPath
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

Write-Host "`nBuilding MSI package..." -ForegroundColor Cyan
dotnet build "$msiProjectDir/DirectoryNET.Installer.wixproj" `
    -c $Configuration `
    -p:ServerPublishDir="$serverOut" `
    -p:WebPublishDir="$webOut" `
    -o $outputDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to build MSI" -ForegroundColor Red
    exit 1
}

# ── Summary ──
Write-Host "`n========================================" -ForegroundColor Green
Write-Host " MSI built successfully" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

$msi = Get-ChildItem "$outputDir/*.msi" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($msi) {
    $size = [math]::Round($msi.Length / 1MB, 1)
    Write-Host "  Output: $($msi.FullName) ($size MB)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Install (default):" -ForegroundColor White
    Write-Host "    msiexec /i `"$($msi.Name)`"" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Install (silent with config):" -ForegroundColor White
    Write-Host "    msiexec /i `"$($msi.Name)`" /qn COSMOS_CONNECTION_STRING=`"...`" DOMAIN_NAME=`"corp.example.com`"" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Uninstall:" -ForegroundColor White
    Write-Host "    msiexec /x `"$($msi.Name)`" /qn" -ForegroundColor Gray
} else {
    Write-Host "  Warning: No .msi file found in $outputDir" -ForegroundColor Yellow
}
