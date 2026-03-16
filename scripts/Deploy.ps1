#Requires -Version 5.1
<#
.SYNOPSIS
    Builds, signs, and installs the Pakko MSIX package for local development.
.DESCRIPTION
    In default (BuildAndDeploy) mode:
      1. Builds Archiver.Shell and Archiver.ProgressWindow for the target architecture.
      2. Runs dotnet publish on Archiver.App with GenerateAppxPackageOnBuild=true.
         Content Include items in Archiver.App.csproj declare the satellite EXEs as
         package content, so the packaging pipeline includes them automatically.
      3. Uninstalls any existing Pakko package, then installs the new one.

    In -DeployOnly mode, skips all build/package steps and installs
    the most recently built .msix from AppPackages/.
.PARAMETER DeployOnly
    Skip build and package; install the most recently built .msix from AppPackages/.
.PARAMETER Architecture
    Target architecture: "x64" (default) or "arm64".
.PARAMETER Thumbprint
    Thumbprint of the code-signing certificate (BuildAndDeploy mode only).
    If omitted, the script searches Cert:\CurrentUser\My for CN=Pakko Dev.
.EXAMPLE
    .\Deploy.ps1
    .\Deploy.ps1 -Architecture arm64
    .\Deploy.ps1 -Thumbprint "ABCDEF1234567890..."
    .\Deploy.ps1 -DeployOnly
#>
[CmdletBinding()]
param(
    [switch] $DeployOnly,
    [ValidateSet('x64', 'arm64')]
    [string] $Architecture = 'x64',
    [string] $Thumbprint
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Paths ─────────────────────────────────────────────────────────────────────
$repoRoot   = Split-Path $PSScriptRoot -Parent
$csprojPath = Join-Path $repoRoot 'src\Archiver.App\Archiver.App.csproj'
$pkgOutDir  = Join-Path $repoRoot 'src\Archiver.App\AppPackages'

# ── Derive platform/RID from Architecture ─────────────────────────────────────
if ($Architecture -eq 'arm64') {
    $platform = 'ARM64'
    $rid      = 'win-arm64'
} else {
    $platform = 'x64'
    $rid      = 'win-x64'
}

if (-not $DeployOnly) {
    # ── Resolve certificate thumbprint ────────────────────────────────────────
    if (-not $Thumbprint) {
        Write-Host "No thumbprint provided -- searching Cert:\CurrentUser\My for CN=Pakko Dev..."
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

    # ── Clean old AppPackages output ──────────────────────────────────────────
    if (Test-Path $pkgOutDir) {
        Write-Host "Removing old AppPackages output..."
        Remove-Item -Recurse -Force $pkgOutDir -ErrorAction SilentlyContinue
    }

    # ── Build satellite projects ───────────────────────────────────────────────
    Write-Host ""
    Write-Host "Building satellite projects..." -ForegroundColor Cyan

    $shellProj    = Join-Path $repoRoot 'src\Archiver.Shell\Archiver.Shell.csproj'
    $progressProj = Join-Path $repoRoot 'src\Archiver.ProgressWindow\Archiver.ProgressWindow.csproj'

    & dotnet build $shellProj    /p:Configuration=Release /p:Platform=$platform /p:RuntimeIdentifier=$rid --no-self-contained
    if ($LASTEXITCODE -ne 0) { Write-Error "Archiver.Shell build failed (exit $LASTEXITCODE)."; exit $LASTEXITCODE }

    & dotnet build $progressProj /p:Configuration=Release /p:Platform=$platform /p:RuntimeIdentifier=$rid --no-self-contained
    if ($LASTEXITCODE -ne 0) { Write-Error "Archiver.ProgressWindow build failed (exit $LASTEXITCODE)."; exit $LASTEXITCODE }

    # ── dotnet publish: package and sign ─────────────────────────────────────
    # Content Include items in Archiver.App.csproj (conditioned on
    # GenerateAppxPackageOnBuild=true) declare satellite EXEs as package content.
    Write-Host ""
    Write-Host "Publishing Pakko ($Architecture)..." -ForegroundColor Cyan

    $env:PAKKO_DEPLOYING = '1'
    & dotnet publish $csprojPath `
        /p:Configuration=Release `
        "/p:Platform=$platform" `
        "/p:RuntimeIdentifier=$rid" `
        /p:SelfContained=true `
        /p:GenerateAppxPackageOnBuild=true `
        /p:AppxPackageSigningEnabled=true `
        "/p:PackageCertificateThumbprint=$Thumbprint"
    $env:PAKKO_DEPLOYING = $null
    if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed (exit $LASTEXITCODE)."; exit $LASTEXITCODE }
}

# ── Locate the final .msix ────────────────────────────────────────────────────
$msix = Get-ChildItem -Path $pkgOutDir -Recurse -Filter '*.msix' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msix) {
    Write-Error "No .msix file found under $pkgOutDir."
    exit 1
}

Write-Host "Package: $($msix.FullName)"

# ── Uninstall existing Pakko package ──────────────────────────────────────────
Write-Host ""
Write-Host "Uninstalling existing Pakko package (if any)..."
Get-AppxPackage *Pakko* | Remove-AppxPackage -ErrorAction SilentlyContinue

# ── Install new package ───────────────────────────────────────────────────────
Write-Host "Installing $($msix.Name)..."
Add-AppxPackage -Path $msix.FullName

# ── Report installed version ──────────────────────────────────────────────────
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
