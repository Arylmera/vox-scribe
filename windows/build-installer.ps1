#!/usr/bin/env pwsh
# Build VoxScribe installer. Extracts version from Directory.Version.props
# and passes it to Inno Setup, ensuring version sync.
#
# Usage: ./build-installer.ps1 [-Configuration Release] [-PublishDir ./publish]

param(
    [string]$Configuration = 'Release',
    [string]$PublishDir = './publish'
)

$ErrorActionPreference = 'Stop'

# Extract version from Directory.Version.props
$versionFile = Resolve-Path 'Directory.Version.props'
$versionContent = Get-Content $versionFile -Raw
$versionMatch = $versionContent -match '<Version>([^<]+)</Version>'
if (-not $versionMatch) {
    Write-Error "Could not find version in Directory.Version.props"
    exit 1
}
$version = $matches[1]
Write-Host "Building VoxScribe version $version" -ForegroundColor Green

# Publish
Write-Host "Publishing app ($Configuration)..." -ForegroundColor Blue
dotnet publish src/VoxScribe.App `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed"
    exit 1
}

# Build installer
Write-Host "Building installer..." -ForegroundColor Blue
$isccPath = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
if (-not (Test-Path $isccPath)) {
    Write-Error "Inno Setup 6 not found at $isccPath. Install from https://jrsoftware.org/isdl.php"
    exit 1
}

& $isccPath installer\voxscribe.iss "/DPublishDir=$(Resolve-Path $PublishDir)" "/DAppVersion=$version"

if ($LASTEXITCODE -ne 0) {
    Write-Error "ISCC failed"
    exit 1
}

Write-Host "Installer built successfully: installer/Output/voxscribe-setup-$version.exe" -ForegroundColor Green
