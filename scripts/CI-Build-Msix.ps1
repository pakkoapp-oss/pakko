#Requires -Version 5.1
<#
.SYNOPSIS
    CI-only build+sign of the Pakko MSIX package. No install, no version bump.
.DESCRIPTION
    A sibling to Deploy.ps1 covering just the build+sign steps (T-F122). Intended for a fresh
    CI checkout, so it deliberately skips Deploy.ps1's MSB3231 staleness-tolerance logic — that
    exists only to handle a leftover AppPackages/obj folder from a prior incremental local
    build, which cannot happen here.

    Publishes Archiver.Shell, Archiver.OperationUi and Archiver.CLI as Native AOT exes (T-F355),
    builds Archiver.ShellExtension.dll, then dotnet publishes Archiver.App (AOT too) with
    AppxPackageSigningEnabled=true. Writes the produced .msix/.msixbundle path to
    $env:GITHUB_OUTPUT (msixPath=...) when running inside GitHub Actions, and always prints it.
.PARAMETER Architecture
    Target architecture: "x64" (default) or "arm64".
.PARAMETER Thumbprint
    Thumbprint of the code-signing certificate. Required.
.PARAMETER MsBuildPath
    Path to msbuild.exe. Defaults to the newest Visual Studio with MSBuild (vswhere -latest), the
    same way Deploy.ps1 does; pass this to use whatever microsoft/setup-msbuild resolved instead.
.PARAMETER CliVersion
    Release version for the packaged pakko.exe (T-F317), e.g. 1.7.0. Omit outside a release.
.EXAMPLE
    .\CI-Build-Msix.ps1 -Architecture x64 -Thumbprint D2EC5F2C451ED0EBE94B8168A68E5B813954CC75
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string] $Architecture = 'x64',
    [Parameter(Mandatory = $true)]
    [string] $Thumbprint,
    [string] $MsBuildPath,
    [string] $CliVersion
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

# ── Publish the satellites as Native AOT exes (T-F355) ─────────────────────────
# Into the bin folders Archiver.App.csproj's Content items read (T-F128: keep the paths). ILCompiler
# finds the C++ toolchain through vswhere.exe on PATH. A release passes -CliVersion (the tag without
# "v") so the packaged pakko -v prints the release, as Publish-Cli.ps1 -Version does for the zip;
# otherwise the csproj's 0.0.0-dev marker stays (T-F222).
$env:PATH = (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer') + ";$env:PATH"
foreach ($satellite in @(
    @{ Project = 'src\Archiver.Shell\Archiver.Shell.csproj'; Out = "src\Archiver.Shell\bin\$platform\Release\net10.0-windows\$rid" },
    @{ Project = 'src\Archiver.OperationUi\Archiver.OperationUi.csproj'; Out = "src\Archiver.OperationUi\bin\$platform\Release\net10.0-windows10.0.17763.0\$rid" },
    @{ Project = 'src\Archiver.CLI\Archiver.CLI.csproj'; Out = "src\Archiver.CLI\bin\$platform\Release\net10.0\$rid" }
)) {
    Write-Host "Publishing $($satellite.Project) ($Architecture, Native AOT)..." -ForegroundColor Cyan
    $satelliteArgs = @((Join-Path $repoRoot $satellite.Project), '/p:Configuration=Release', "/p:Platform=$platform", "/p:RuntimeIdentifier=$rid", '-o', (Join-Path $repoRoot $satellite.Out))
    if ($CliVersion -and $satellite.Project -like '*Archiver.CLI*') { $satelliteArgs += "/p:InformationalVersion=$CliVersion" }
    & dotnet publish @satelliteArgs
    if ($LASTEXITCODE -ne 0) { Write-Error "$($satellite.Project) publish failed (exit $LASTEXITCODE)."; exit $LASTEXITCODE }
}

# ── Build Archiver.ShellExtension (C++ COM DLL) ────────────────────────────────
Write-Host ""
Write-Host "Building Archiver.ShellExtension ($Architecture)..." -ForegroundColor Cyan

if (-not $MsBuildPath) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $MsBuildPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\MSBuild.exe' |
        Select-Object -First 1
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

# ── T-F317/T-F355: check the built package itself carries the AOT exes and pakko's alias ────
# The source manifest and Archiver.App.csproj are checked by PackagingManifestTests; this checks
# what the packaging pipeline actually produced: the three satellite exes, pakko's alias, and no
# JIT runtime or managed satellite left over (each would mean a non-AOT build slipped in). A .msixbundle holds the app package as an inner
# .msix next to the resource packages, so look inside every inner package for the one with pakko.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Test-PakkoInPackage([System.IO.Compression.ZipArchive] $package) {
    $names = @($package.Entries | ForEach-Object { $_.FullName })
    $required = @('pakko.exe', 'Archiver.Shell.exe', 'Archiver.OperationUi.exe', 'AppxManifest.xml')
    foreach ($name in $required) {
        if ($names -notcontains $name) { return $false }
    }
    foreach ($name in @('onnxruntime.dll', 'DirectML.dll', 'Microsoft.Windows.AI.MachineLearning.dll')) {
        if ($names -contains $name) { throw "The package carries $name - Windows ML's natives are excluded (T-F363)." }
    }
    foreach ($name in @('coreclr.dll', 'pakko.dll', 'Archiver.Shell.dll', 'Archiver.OperationUi.dll')) {
        if ($names -contains $name) { throw "The package carries $name - a non-AOT build reached it (T-F355)." }
    }
    $reader = New-Object System.IO.StreamReader(($package.GetEntry('AppxManifest.xml')).Open())
    try { $manifestText = $reader.ReadToEnd() } finally { $reader.Dispose() }
    return $manifestText.Contains('Alias="pakko.exe"')
}

$outer = [System.IO.Compression.ZipFile]::OpenRead($msix.FullName)
try {
    $pakkoFound = $false
    if ($msix.Extension -eq '.msix') {
        $pakkoFound = Test-PakkoInPackage $outer
    } else {
        foreach ($inner in @($outer.Entries | Where-Object { $_.FullName -like '*.msix' })) {
            $buffer = New-Object System.IO.MemoryStream
            try {
                $innerStream = $inner.Open()
                try { $innerStream.CopyTo($buffer) } finally { $innerStream.Dispose() }
                $buffer.Position = 0
                $innerZip = New-Object System.IO.Compression.ZipArchive($buffer, [System.IO.Compression.ZipArchiveMode]::Read)
                try {
                    if (Test-PakkoInPackage $innerZip) { $pakkoFound = $true; break }
                } finally { $innerZip.Dispose() }
            } finally { $buffer.Dispose() }
        }
    }
} finally {
    $outer.Dispose()
}
if (-not $pakkoFound) {
    Write-Error "The built package $($msix.Name) has no pakko.exe, Archiver.Shell.exe, Archiver.OperationUi.exe and the pakko.exe execution alias (T-F317, T-F355)."
    exit 1
}

# ── T-F355: native symbols of the four AOT exes, for crash dumps; never packaged ──
$pdbOutDir = Join-Path $repoRoot "artifacts\pdb\$Architecture"
New-Item -ItemType Directory -Force -Path $pdbOutDir | Out-Null
foreach ($nativeDir in @(
    "src\Archiver.App\bin\$platform\Release\net10.0-windows10.0.17763.0\$rid\native",
    "src\Archiver.Shell\bin\$platform\Release\net10.0-windows\$rid\native",
    "src\Archiver.OperationUi\bin\$platform\Release\net10.0-windows10.0.17763.0\$rid\native",
    "src\Archiver.CLI\bin\$platform\Release\net10.0\$rid\native"
)) {
    $pdb = Get-ChildItem -LiteralPath (Join-Path $repoRoot $nativeDir) -Filter '*.pdb' | Select-Object -First 1
    if (-not $pdb) { Write-Error "No native .pdb under $nativeDir (T-F355)."; exit 1 }
    Copy-Item -LiteralPath $pdb.FullName -Destination $pdbOutDir
}

Write-Host ""
Write-Host "Package: $($msix.FullName)" -ForegroundColor Green

if ($env:GITHUB_OUTPUT) {
    Add-Content -Path $env:GITHUB_OUTPUT -Value "msixPath=$($msix.FullName)"
}

exit 0
