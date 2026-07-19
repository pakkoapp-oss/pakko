#Requires -Version 5.1
<#
.SYNOPSIS
    CI-only build+sign of the Pakko MSIX package. No install, no version bump.
.DESCRIPTION
    A sibling to Deploy.ps1 covering just the build+sign steps (T-F122). Intended for a fresh
    CI checkout, so it deliberately skips Deploy.ps1's MSB3231 staleness-tolerance logic — that
    exists only to handle a leftover AppPackages/obj folder from a prior incremental local
    build, which cannot happen here.

    Builds Archiver.Shell, Archiver.ShellExtension.dll, then dotnet publishes Archiver.App with
    AppxPackageSigningEnabled=true. Writes the produced .msix/.msixbundle path to
    $env:GITHUB_OUTPUT (msixPath=...) when running inside GitHub Actions, and always prints it.
.PARAMETER Architecture
    Target architecture: "x64" (default) or "arm64".
.PARAMETER Thumbprint
    Thumbprint of the code-signing certificate. Required.
.PARAMETER MsBuildPath
    Path to msbuild.exe. Defaults to searching Visual Studio 2022's install location the same
    way Deploy.ps1 does; pass this to use whatever microsoft/setup-msbuild resolved instead.
.EXAMPLE
    .\CI-Build-Msix.ps1 -Architecture x64 -Thumbprint D2EC5F2C451ED0EBE94B8168A68E5B813954CC75
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string] $Architecture = 'x64',
    [Parameter(Mandatory = $true)]
    [string] $Thumbprint,
    [string] $MsBuildPath
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

if (Test-Path $pkgOutDir) {
    Remove-Item -Recurse -Force $pkgOutDir -ErrorAction SilentlyContinue
}

# ── Build Archiver.Shell (self-contained satellite EXE) ───────────────────────
Write-Host "Building Archiver.Shell ($Architecture)..." -ForegroundColor Cyan
$shellProj = Join-Path $repoRoot 'src\Archiver.Shell\Archiver.Shell.csproj'
& dotnet build $shellProj /p:Configuration=Release /p:Platform=$platform /p:RuntimeIdentifier=$rid --self-contained
if ($LASTEXITCODE -ne 0) { Write-Error "Archiver.Shell build failed (exit $LASTEXITCODE)."; exit $LASTEXITCODE }

# ── Build Archiver.ShellExtension (C++ COM DLL) ────────────────────────────────
Write-Host ""
Write-Host "Building Archiver.ShellExtension ($Architecture)..." -ForegroundColor Cyan

if (-not $MsBuildPath) {
    $MsBuildPath = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio\2022" `
        -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'Current\\Bin\\MSBuild\.exe$' } |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $MsBuildPath) {
    Write-Error "MSBuild.exe not found. Pass -MsBuildPath explicitly (e.g. the path microsoft/setup-msbuild resolved)."
    exit 1
}

$shellExtProj = Join-Path $repoRoot 'src\Archiver.ShellExtension\Archiver.ShellExtension.vcxproj'
$shellExtObjDir = Join-Path $repoRoot "src\Archiver.ShellExtension\obj\$platform\Release"
$shellExtBinDir = Join-Path $repoRoot "src\Archiver.ShellExtension\bin\$platform\Release"
Remove-Item -Recurse -Force $shellExtObjDir, $shellExtBinDir -ErrorAction SilentlyContinue

& $MsBuildPath $shellExtProj /p:Configuration=Release "/p:Platform=$platform" "/p:SolutionDir=$repoRoot\" /m /nodeReuse:false
if ($LASTEXITCODE -ne 0) { Write-Error "Archiver.ShellExtension build failed (exit $LASTEXITCODE)."; exit $LASTEXITCODE }

# ── dotnet publish: package and sign ──────────────────────────────────────────
# Archiver.App.csproj's DeployMsix target (AfterTargets="Build") tries to Add-AppxPackage the
# freshly built package whenever Configuration=Release, unless PAKKO_DEPLOYING=1 — the same guard
# Deploy.ps1 sets before its own publish call. CI has no LocalMachine\TrustedPeople trust for the
# signing cert, so that auto-install fails outright (0x800B0109) and takes the whole publish down
# with it if this isn't set.
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
$publishExitCode = $LASTEXITCODE
$env:PAKKO_DEPLOYING = $null
if ($publishExitCode -ne 0) { Write-Error "dotnet publish failed (exit $publishExitCode)."; exit $publishExitCode }

# ── Locate the produced package ───────────────────────────────────────────────
# T-F91: 24+ locale resource packages force a .msixbundle instead of a flat .msix.
$msix = Get-ChildItem -Path $pkgOutDir -Recurse -Include '*.msix', '*.msixbundle' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msix) {
    Write-Error "No .msix or .msixbundle file found under $pkgOutDir."
    exit 1
}

Write-Host ""
Write-Host "Package: $($msix.FullName)" -ForegroundColor Green

if ($env:GITHUB_OUTPUT) {
    Add-Content -Path $env:GITHUB_OUTPUT -Value "msixPath=$($msix.FullName)"
}

exit 0
