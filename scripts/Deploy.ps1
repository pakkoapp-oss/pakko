#Requires -Version 5.1
<#
.SYNOPSIS
    Builds, signs, and installs the Pakko MSIX package for local development.
.DESCRIPTION
    Publishes a signed Release x64 MSIX, uninstalls any existing Pakko package,
    then installs the new one. Run after every build.
.PARAMETER Thumbprint
    Thumbprint of the code-signing certificate to use. If omitted, the script
    searches Cert:\CurrentUser\My for a certificate with Subject "CN=Pakko Dev".
.EXAMPLE
    .\Deploy.ps1
    .\Deploy.ps1 -Thumbprint "ABCDEF1234567890..."
#>
[CmdletBinding()]
param(
    [string] $Thumbprint
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Resolve certificate thumbprint ──────────────────────────────────────────
if (-not $Thumbprint) {
    Write-Host "No thumbprint provided — searching Cert:\CurrentUser\My for CN=Pakko Dev..."
    $cert = Get-ChildItem 'Cert:\CurrentUser\My' |
        Where-Object { $_.Subject -eq 'CN=Pakko Dev' } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $cert) {
        Write-Error "No certificate with Subject 'CN=Pakko Dev' found in Cert:\CurrentUser\My.`nRun Setup-DevCert.ps1 first."
        exit 1
    }

    $Thumbprint = $cert.Thumbprint
    Write-Host "Found certificate: $Thumbprint (expires $($cert.NotAfter.ToString('yyyy-MM-dd')))"
}

# ── Paths ────────────────────────────────────────────────────────────────────
$repoRoot  = Split-Path $PSScriptRoot -Parent
$csprojPath = Join-Path $repoRoot 'src\Archiver.App\Archiver.App.csproj'
$pkgOutDir  = Join-Path $repoRoot 'src\Archiver.App\AppPackages'

# ── Clean old AppPackages output ─────────────────────────────────────────────
if (Test-Path $pkgOutDir) {
    Write-Host "Removing old AppPackages output..."
    Remove-Item -Recurse -Force $pkgOutDir -ErrorAction SilentlyContinue
}

# ── Build & package ──────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Building and packaging Pakko..." -ForegroundColor Cyan

$publishArgs = @(
    'publish', $csprojPath,
    '/p:Configuration=Release',
    '/p:Platform=x64',
    '/p:RuntimeIdentifier=win-x64',
    '/p:SelfContained=true',
    '/p:GenerateAppxPackageOnBuild=true',
    '/p:AppxPackageSigningEnabled=true',
    "/p:PackageCertificateThumbprint=$Thumbprint"
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed (exit code $LASTEXITCODE)."
    exit $LASTEXITCODE
}

# ── Locate the generated .msix ───────────────────────────────────────────────
$msix = Get-ChildItem -Path $pkgOutDir -Recurse -Filter '*.msix' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msix) {
    Write-Error "No .msix file found under $pkgOutDir after publish."
    exit 1
}

Write-Host "Package: $($msix.FullName)"

# ── Uninstall existing Pakko package ────────────────────────────────────────
Write-Host ""
Write-Host "Uninstalling existing Pakko package (if any)..."
Get-AppxPackage *Pakko* | Remove-AppxPackage -ErrorAction SilentlyContinue

# ── Install new package ──────────────────────────────────────────────────────
Write-Host "Installing $($msix.Name)..."
Add-AppxPackage -Path $msix.FullName

# ── Report installed version ─────────────────────────────────────────────────
$installed = Get-AppxPackage *Pakko* | Select-Object -First 1
if ($installed) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host " Pakko installed successfully" -ForegroundColor Green
    Write-Host " Version: $($installed.Version)" -ForegroundColor Green
    Write-Host " Package: $($installed.PackageFullName)" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
} else {
    Write-Warning "Package installed but could not be verified via Get-AppxPackage."
}
