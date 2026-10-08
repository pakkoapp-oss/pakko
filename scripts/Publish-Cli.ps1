#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes Archiver.CLI as a standalone, self-contained downloadable artifact.
.DESCRIPTION
    Independent of Deploy.ps1 — Archiver.CLI is never packaged into the MSIX, needs no
    dev-signing certificate, and is not affected by anything in that script.

    For each requested architecture:
      1. dotnet publish as Native AOT (T-F355): one native pakko.exe that runs on a machine
         with no .NET install and without Pakko's GUI/MSIX installed — see docs/CLI.md's
         "Distribution" section. Needs the Visual Studio C++ build tools for the target
         architecture.
      2. Zips pakko.exe alone as pakko-<rid>.zip; its pakko.pdb (native symbols, for crash
         dumps) stays next to it in the publish folder, outside the zip.
    Then writes a SHA256SUMS file covering every zip produced, so a script or a user
    can verify the download before running it.
.PARAMETER Architecture
    Target architecture: "x64", "arm64", or "both" (default).
.PARAMETER Configuration
    Build configuration (default: Release).
.PARAMETER OutputRoot
    Directory to publish into (default: artifacts/cli under the repo root). Its old contents
    are deleted first, so it must be new, empty, the default, or a previous output of this
    script; any other non-empty folder is refused.
.PARAMETER Version
    Overrides Archiver.CLI.csproj's <Version> for this publish (drives `pakko --version`'s
    output). CI passes the pushed git tag here (stripped of its leading "v") so a released
    pakko.exe always reports the exact tag it shipped under, regardless of the csproj's own
    checked-in default. Omit for a local/dev publish to just use the csproj's value.
.EXAMPLE
    .\Publish-Cli.ps1
    .\Publish-Cli.ps1 -Architecture x64
    .\Publish-Cli.ps1 -Version 1.4.2
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64', 'both')]
    [string] $Architecture = 'both',
    [string] $Configuration = 'Release',
    [string] $OutputRoot,
    [string] $Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Paths ─────────────────────────────────────────────────────────────────────
$repoRoot = Split-Path $PSScriptRoot -Parent
$csproj   = Join-Path $repoRoot 'src\Archiver.CLI\Archiver.CLI.csproj'
$defaultOutputRoot = Join-Path $repoRoot 'artifacts\cli'
if (-not $OutputRoot) {
    $OutputRoot = $defaultOutputRoot
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$ownerMarker = Join-Path $OutputRoot '.pakko-cli-output'

# T-F259: -OutputRoot is caller-supplied, so only a folder this script owns is ever wiped.
if (Test-Path -LiteralPath $OutputRoot) {
    $isDefault = $OutputRoot.TrimEnd('\') -ieq $defaultOutputRoot.TrimEnd('\')
    $isOwned   = Test-Path -LiteralPath $ownerMarker
    $isEmpty   = -not (Get-ChildItem -LiteralPath $OutputRoot -Force | Select-Object -First 1)
    if (-not ($isDefault -or $isOwned -or $isEmpty)) {
        throw "OutputRoot '$OutputRoot' is not empty and was not created by this script; refusing to delete it. Pass an empty or new folder."
    }
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
New-Item -ItemType File -Path $ownerMarker | Out-Null

# ILCompiler's link step finds the C++ toolchain through vswhere.exe on PATH.
$env:PATH = (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer') + ";$env:PATH"

$architectures = if ($Architecture -eq 'both') { @('x64', 'arm64') } else { @($Architecture) }
$zipPaths = @()

foreach ($arch in $architectures) {
    $rid      = if ($arch -eq 'arm64') { 'win-arm64' } else { 'win-x64' }
    $platform = if ($arch -eq 'arm64') { 'ARM64' }     else { 'x64' }
    $publishDir = Join-Path $OutputRoot $rid

    Write-Host "Publishing Archiver.CLI for $rid ..."
    $publishArgs = @(
        "/p:Configuration=$Configuration",
        "/p:Platform=$platform",
        "/p:RuntimeIdentifier=$rid",
        "/p:PublishDir=$publishDir"
    )
    if ($Version) {
        $publishArgs += "/p:Version=$Version"
    }
    & dotnet publish $csproj @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid (exit code $LASTEXITCODE)"
    }

    $zipPath = Join-Path $OutputRoot "pakko-$rid.zip"
    Compress-Archive -Path (Join-Path $publishDir 'pakko.exe') -DestinationPath $zipPath -Force
    $zipPaths += $zipPath
    Write-Host "  -> $zipPath"
}

# ── SHA256SUMS ──────────────────────────────────────────────────────────────
$sumsPath = Join-Path $OutputRoot 'SHA256SUMS'
$lines = $zipPaths | ForEach-Object {
    $hash = (Get-FileHash -Path $_ -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $_ -Leaf)"
}
[System.IO.File]::WriteAllLines($sumsPath, $lines)

Write-Host ""
Write-Host "Done. Artifacts in $OutputRoot :"
Get-ChildItem $OutputRoot -File | ForEach-Object { Write-Host "  $($_.Name)" }
