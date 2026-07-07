#Requires -Version 5.1
<#
.SYNOPSIS
    Builds, signs, and installs the Pakko MSIX package for local development.
.DESCRIPTION
    In default (BuildAndDeploy) mode:
      1. Builds Archiver.Shell for the target architecture.
      2. Runs dotnet publish on Archiver.App with GenerateAppxPackageOnBuild=true.
         Content Include items in Archiver.App.csproj declare the satellite EXE as
         package content, so the packaging pipeline includes it automatically.
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
.PARAMETER SkipVersionBump
    Do not increment Package.appxmanifest's Version after a successful build+install.
    Has no effect in -DeployOnly mode (which never bumps).
.EXAMPLE
    .\Deploy.ps1
    .\Deploy.ps1 -Architecture arm64
    .\Deploy.ps1 -Thumbprint "ABCDEF1234567890..."
    .\Deploy.ps1 -DeployOnly
    .\Deploy.ps1 -SkipVersionBump
#>
[CmdletBinding()]
param(
    [switch] $DeployOnly,
    [ValidateSet('x64', 'arm64')]
    [string] $Architecture = 'x64',
    [string] $Thumbprint,
    [switch] $SkipVersionBump
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Paths ─────────────────────────────────────────────────────────────────────
$repoRoot     = Split-Path $PSScriptRoot -Parent
$csprojPath   = Join-Path $repoRoot 'src\Archiver.App\Archiver.App.csproj'
$pkgOutDir    = Join-Path $repoRoot 'src\Archiver.App\AppPackages'
$manifestPath = Join-Path $repoRoot 'src\Archiver.App\Package.appxmanifest'

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

    # Self-contained: this apphost runs inside the MSIX package with no globally installed
    # .NET runtime to fall back on. A framework-dependent apphost fails at launch with
    # "You must install or update .NET to run this application" (a modal dialog — the process
    # never exits, which looked like the context menu silently doing nothing). Self-contained
    # apphosts probe their own directory first, where Archiver.App's self-contained publish
    # already deposits the matching hostfxr/coreclr/hostpolicy native files at the package root.
    & dotnet build $shellProj    /p:Configuration=Release /p:Platform=$platform /p:RuntimeIdentifier=$rid --self-contained
    if ($LASTEXITCODE -ne 0) { Write-Error "Archiver.Shell build failed (exit $LASTEXITCODE)."; exit $LASTEXITCODE }

    # ── Build Archiver.ShellExtension (C++ DLL) ───────────────────────────────────
    Write-Host ""
    Write-Host "Building Archiver.ShellExtension ($Architecture)..." -ForegroundColor Cyan

    $msbuildPath = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio\2022" `
        -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'Current\\Bin\\MSBuild\.exe$' } |
        Select-Object -First 1 -ExpandProperty FullName

    if (-not $msbuildPath) {
        Write-Error "MSBuild.exe not found. Install Visual Studio 2022 with the 'Desktop development with C++' workload."
        exit 1
    }

    $shellExtProj = Join-Path $repoRoot 'src\Archiver.ShellExtension\Archiver.ShellExtension.vcxproj'
    $shellExtObjDir = Join-Path $repoRoot "src\Archiver.ShellExtension\obj\$platform\Release"
    $shellExtBinDir = Join-Path $repoRoot "src\Archiver.ShellExtension\bin\$platform\Release"

    # Stale obj/ (e.g. from a prior VS IDE build with a different compiler/toolset state,
    # or an interrupted build) can leave a corrupted or mismatched .pch that fails with
    # C1853 ("precompiled header file is from a different version of the compiler") when
    # picked up by this headless MSBuild invocation. Always start from a clean intermediate
    # directory for this project.
    Remove-Item -Recurse -Force $shellExtObjDir, $shellExtBinDir -ErrorAction SilentlyContinue

    # SolutionDir is only auto-populated by MSBuild when building through a .sln — building
    # the .vcxproj directly (as here) leaves it undefined, and the project's OutDir/IntDir
    # (which are relative to $(SolutionDir)) silently fall back to a doubled, wrong path
    # (src\Archiver.ShellExtension\src\Archiver.ShellExtension\bin\...). Pass it explicitly.
    & $msbuildPath $shellExtProj /p:Configuration=Release "/p:Platform=$platform" "/p:SolutionDir=$repoRoot\" /m /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { Write-Error "Archiver.ShellExtension build failed (exit $LASTEXITCODE)."; exit $LASTEXITCODE }

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

# ── Locate the final .msix/.msixbundle ────────────────────────────────────────
# T-F91: once enough per-language resource packages exist (24+ locale folders under
# Strings/), the packaging pipeline emits a .msixbundle instead of a flat .msix — a
# bundle is required to carry multiple resource-qualified sub-packages. Add-AppxPackage
# installs either directly, so accept both and take whichever is newest.
$msix = Get-ChildItem -Path $pkgOutDir -Recurse -Include '*.msix', '*.msixbundle' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msix) {
    Write-Error "No .msix or .msixbundle file found under $pkgOutDir."
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

# ── Bump Package.appxmanifest's Version for the next deploy ──────────────────
# Only after a real build+install (never in -DeployOnly, which reinstalls an
# already-built package) and only the last segment, per CLAUDE.md's versioning rule.
if (-not $DeployOnly -and -not $SkipVersionBump -and $installed) {
    $manifestText = [System.IO.File]::ReadAllText($manifestPath)
    $versionPattern = '(?<![A-Za-z])Version="(\d+)\.(\d+)\.(\d+)\.(\d+)"'
    $match = [regex]::Match($manifestText, $versionPattern)

    if ($match.Success) {
        $nextPatch = [int]$match.Groups[4].Value + 1
        $oldVersion = $match.Value
        $newVersion = 'Version="{0}.{1}.{2}.{3}"' -f `
            $match.Groups[1].Value, $match.Groups[2].Value, $match.Groups[3].Value, $nextPatch

        $manifestText = $manifestText.Substring(0, $match.Index) + $newVersion +
            $manifestText.Substring($match.Index + $match.Length)
        [System.IO.File]::WriteAllText($manifestPath, $manifestText, (New-Object System.Text.UTF8Encoding($false)))

        Write-Host ""
        Write-Host "Bumped Package.appxmanifest: $oldVersion -> $newVersion" -ForegroundColor Cyan
    } else {
        Write-Warning "Could not find Version attribute in Package.appxmanifest - skipped version bump."
    }
}
