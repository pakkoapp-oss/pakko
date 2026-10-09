#Requires -Version 5.1
<#
.SYNOPSIS
    Builds, signs, and installs the Pakko MSIX package for local development.
.DESCRIPTION
    In default (BuildAndDeploy) mode:
      1. Publishes Archiver.Shell, Archiver.OperationUi and Archiver.CLI as Native AOT exes
         for the target architecture (T-F355).
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
    Package at the tracked X.Y.Z.0 instead of one revision past the installed dev package.
    The tracked Package.appxmanifest is never written either way (T-F368).
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

    # T-F355: every satellite is a Native AOT exe - runtime and Archiver.Core compiled in, nothing
    # else of it packaged (the App is AOT too, so the package root has no .NET runtime). Published
    # into the same bin folder Archiver.App.csproj's Content items read (T-F128: keep the paths).
    # ILCompiler's link step finds the C++ toolchain through vswhere.exe on PATH.
    $env:PATH = (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer') + ";$env:PATH"
    $shellExeSourcePath = Join-Path $repoRoot "src\Archiver.Shell\bin\$platform\Release\net10.0-windows\$rid\Archiver.Shell.exe"
    $operationUiExeSourcePath = Join-Path $repoRoot "src\Archiver.OperationUi\bin\$platform\Release\net10.0-windows10.0.17763.0\$rid\Archiver.OperationUi.exe"
    $cliExeSourcePath = Join-Path $repoRoot "src\Archiver.CLI\bin\$platform\Release\net10.0\$rid\pakko.exe"
    foreach ($satellite in @(
        @{ Project = 'src\Archiver.Shell\Archiver.Shell.csproj'; Exe = $shellExeSourcePath },
        @{ Project = 'src\Archiver.OperationUi\Archiver.OperationUi.csproj'; Exe = $operationUiExeSourcePath },
        @{ Project = 'src\Archiver.CLI\Archiver.CLI.csproj'; Exe = $cliExeSourcePath }
    )) {
        & dotnet publish (Join-Path $repoRoot $satellite.Project) /p:Configuration=Release /p:Platform=$platform /p:RuntimeIdentifier=$rid -o (Split-Path $satellite.Exe)
        if ($LASTEXITCODE -ne 0) { Write-Error "$($satellite.Project) publish failed (exit $LASTEXITCODE)."; exit $LASTEXITCODE }
    }

    # ── Build Archiver.ShellExtension (C++ DLL) ───────────────────────────────────
    Write-Host ""
    Write-Host "Building Archiver.ShellExtension ($Architecture)..." -ForegroundColor Cyan

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuildPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\MSBuild.exe' |
        Select-Object -First 1

    if (-not $msbuildPath) {
        Write-Error "MSBuild.exe not found. Install Visual Studio 2026 with the 'Desktop development with C++' workload and the MSVC v143 (14.44) x64/ARM64 build tools."
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
    $shellExtBuildExitCode = $LASTEXITCODE
    if ($shellExtBuildExitCode -ne 0) { Write-Error "Archiver.ShellExtension build failed (exit $shellExtBuildExitCode)."; exit $shellExtBuildExitCode }

    # T-F102: kept for the post-publish completeness check below.
    $shellExtDllSourcePath = Join-Path $shellExtBinDir 'Archiver.ShellExtension.dll'

    # ── dotnet publish: package and sign ─────────────────────────────────────
    # Content Include items in Archiver.App.csproj (conditioned on
    # GenerateAppxPackageOnBuild=true) declare satellite EXEs as package content.
    Write-Host ""
    Write-Host "Publishing Pakko ($Architecture)..." -ForegroundColor Cyan

    # T-F368: the dev revision lives only in a generated copy under obj\ (Archiver.App.csproj's
    # PakkoAppxManifest); the tracked manifest stays at X.Y.Z.0, the Store rule a test enforces.
    $manifestText = [System.IO.File]::ReadAllText($manifestPath)
    $versionMatch = [regex]::Match($manifestText, '(?<![A-Za-z])Version="(\d+\.\d+\.\d+)\.(\d+)"')
    if (-not $versionMatch.Success -or $versionMatch.Groups[2].Value -ne '0') {
        Write-Error "Package.appxmanifest's Identity Version must be X.Y.Z.0 (T-F368)."
        exit 1
    }
    $baseVersion = $versionMatch.Groups[1].Value
    $revision = 0
    if (-not $SkipVersionBump) {
        $revision = 1
        $installedDev = Get-AppxPackage -Name 'PavloRybchenko.Pakko' |
            Where-Object { $_.SignatureKind -eq 'Developer' } |
            Select-Object -First 1
        if ($installedDev) {
            $installedVersion = [version]$installedDev.Version
            $sameBase = "$($installedVersion.Major).$($installedVersion.Minor).$($installedVersion.Build)" -eq $baseVersion
            if ($sameBase -and $installedVersion.Revision -lt 65535) { $revision = $installedVersion.Revision + 1 }
        }
    }
    $packageVersion = "$baseVersion.$revision"
    $devManifestDir = Join-Path $repoRoot 'src\Archiver.App\obj\PakkoDev'
    $devManifestPath = Join-Path $devManifestDir 'Package.appxmanifest'
    New-Item -ItemType Directory -Force -Path $devManifestDir | Out-Null
    $devManifestText = $manifestText.Substring(0, $versionMatch.Index) + "Version=`"$packageVersion`"" +
        $manifestText.Substring($versionMatch.Index + $versionMatch.Length)
    [System.IO.File]::WriteAllText($devManifestPath, $devManifestText, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Package version: $packageVersion"

    $publishStartTime = Get-Date

    $env:PAKKO_DEPLOYING = '1'
    $publishOutput = & dotnet publish $csprojPath `
        /p:Configuration=Release `
        "/p:Platform=$platform" `
        "/p:RuntimeIdentifier=$rid" `
        /p:SelfContained=true `
        /p:GenerateAppxPackageOnBuild=true `
        /p:AppxPackageSigningEnabled=true `
        "/p:PackageCertificateThumbprint=$Thumbprint" `
        "/p:PakkoAppxManifest=$devManifestPath" 2>&1 |
        Tee-Object -Variable publishOutput
    $publishExitCode = $LASTEXITCODE
    $env:PAKKO_DEPLOYING = $null

    if ($publishExitCode -ne 0) {
        # T-F96 (see docs/DECISIONS.md): the MSIX packaging pipeline can fail with
        # MSB3231 "Unable to remove directory ... AppPackages/PackageLayout ..." while
        # cleaning up its own intermediate output -- AFTER a valid .msix/.msixbundle has
        # already been written. Root cause still open (leading theory: Search Indexer
        # racing the cleanup).
        #
        # T-F102: the tolerance gate below used to be regex-on-stdout ("does the error text
        # look like MSB3231"), which is fragile against localized MSBuild output and doesn't
        # actually prove anything about the state that matters -- whether a real, installable
        # package exists. Gate on that directly instead: a fresh package (written after this
        # publish started) with a *valid* Authenticode signature can only exist if packaging
        # and signing both completed -- a real compile error never gets that far, and a real
        # signing error leaves no valid signature. The regex is kept only to make the warning
        # message readable, never as a decision input.
        $matchesKnownCleanupRaceText = ($publishOutput -join "`n") -match
            'MSB3231.*Unable to remove directory.*(AppPackages|PackageLayout)'
        $freshPackage = Get-ChildItem -Path $pkgOutDir -Recurse -Include '*.msix', '*.msixbundle' -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -ge $publishStartTime } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1

        $freshPackageValidlySigned = $false
        if ($freshPackage) {
            $signature = Get-AuthenticodeSignature -FilePath $freshPackage.FullName
            $freshPackageValidlySigned = ($signature.Status -eq 'Valid')
        }

        # T-F102 follow-up: freshness + a valid signature only prove the archive *file* is new
        # and intact -- they say nothing about whether its *contents* are current. Archiver.App
        # .csproj packages Archiver.Shell.exe/Archiver.ShellExtension.dll via
        # CopyToOutputDirectory=PreserveNewest, which silently keeps a stale copy if MSBuild's
        # up-to-date check for that one file gets it wrong -- exactly the mechanism behind the
        # "quick dotnet build can silently install a stale MSIX" gotcha already documented in
        # CLAUDE.md. Compare a SHA256 hash of each packaged entry against the corresponding
        # binary this run just built -- a byte-for-byte check, not a timestamp heuristic: manual
        # inspection during this fix showed both satellite files land in the package with an
        # identical pack-time stamp regardless of their own build time, so a timestamp comparison
        # would have been unable to actually detect a stale copy. (This only verifies the package
        # matches what's in `bin\` right now -- it doesn't verify `bin\` itself is up to date with
        # source, which is dotnet's own incremental-compile correctness, a separate concern.)
        # Only a flat .msix is checked -- once 25+ locales force a .msixbundle (T-F91), these
        # files live inside a nested inner .msix, one zip level deeper, which this check doesn't
        # unpack; that combination just skips the content check below (freshness + signature alone
        # still gate it) rather than guessing at a nested path.
        $contentIsFresh = $true
        $staleEntries = @()
        if ($freshPackage -and $freshPackageValidlySigned -and $freshPackage.Extension -eq '.msix') {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $zip = [System.IO.Compression.ZipFile]::OpenRead($freshPackage.FullName)
            try {
                foreach ($check in @(
                    @{ Name = 'Archiver.Shell.exe'; SourcePath = $shellExeSourcePath },
                    @{ Name = 'Archiver.ShellExtension.dll'; SourcePath = $shellExtDllSourcePath },
                    @{ Name = 'Archiver.OperationUi.exe'; SourcePath = $operationUiExeSourcePath },
                    @{ Name = 'pakko.exe'; SourcePath = $cliExeSourcePath }
                )) {
                    $entry = $zip.Entries | Where-Object { $_.Name -eq $check.Name } | Select-Object -First 1
                    if (-not $entry) {
                        $staleEntries += "$($check.Name): not found inside the package"
                        continue
                    }

                    $entryStream = $entry.Open()
                    $sha256 = [System.Security.Cryptography.SHA256]::Create()
                    try {
                        $packagedHash = [System.BitConverter]::ToString($sha256.ComputeHash($entryStream)) -replace '-', ''
                    } finally {
                        $entryStream.Dispose()
                        $sha256.Dispose()
                    }
                    $sourceHash = (Get-FileHash -Path $check.SourcePath -Algorithm SHA256).Hash

                    if ($packagedHash -ne $sourceHash) {
                        $staleEntries += "$($check.Name): packaged content hash ($packagedHash) does not match the binary just built this run ($sourceHash)"
                    }
                }
            } finally {
                $zip.Dispose()
            }
            $contentIsFresh = ($staleEntries.Count -eq 0)
        }

        if ($freshPackage -and $freshPackageValidlySigned -and $contentIsFresh) {
            $diagnosticText = if ($matchesKnownCleanupRaceText) {
                "matches the known MSB3231 cleanup-race text, T-F96"
            } else {
                "does not match the known MSB3231 text, but a fresh validly-signed package is proof enough"
            }
            Write-Warning "dotnet publish exited $publishExitCode ($diagnosticText), but a freshly-built, validly-signed package exists: $($freshPackage.Name). Continuing with install."
        } elseif ($freshPackage -and $freshPackageValidlySigned -and -not $contentIsFresh) {
            Write-Error "dotnet publish failed (exit $publishExitCode) and the package looks stale/incomplete, not just a tolerable cleanup race: $($staleEntries -join '; ')"
            exit $publishExitCode
        } else {
            Write-Error "dotnet publish failed (exit $publishExitCode)."
            exit $publishExitCode
        }
    }
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
try {
    Add-AppxPackage -Path $msix.FullName -ErrorAction Stop
} catch {
    Write-Error "Add-AppxPackage failed: $_"
    exit 1
}

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

# T-F102: every failure branch above exits explicitly with its own captured code; reaching
# here means every step succeeded, so exit 0 explicitly rather than falling off the end and
# leaking whatever $LASTEXITCODE happened to hold from an earlier tolerated failure.
exit 0
