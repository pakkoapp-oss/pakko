#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes Archiver.CLI as a standalone, self-contained downloadable artifact.
.DESCRIPTION
    Independent of Deploy.ps1 — Archiver.CLI is never packaged into the MSIX, needs no
    dev-signing certificate, and is not affected by anything in that script.

    For each requested architecture:
      1. dotnet publish with SelfContained=true (runs on a machine with no separate
         .NET install, and without Pakko's GUI/MSIX installed — see CLI.md's
         "Distribution" section).
      2. Zips the publish output as pakko-<rid>.zip.
    Then writes a SHA256SUMS file covering every zip produced, so a script or a user
    can verify the download before running it.
.PARAMETER Architecture
    Target architecture: "x64", "arm64", or "both" (default).
.PARAMETER Configuration
    Build configuration (default: Release).
.PARAMETER OutputRoot
    Directory to publish into (default: artifacts/cli under the repo root).
.EXAMPLE
    .\Publish-Cli.ps1
    .\Publish-Cli.ps1 -Architecture x64
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64', 'both')]
    [string] $Architecture = 'both',
    [string] $Configuration = 'Release',
    [string] $OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Paths ─────────────────────────────────────────────────────────────────────
$repoRoot = Split-Path $PSScriptRoot -Parent
$csproj   = Join-Path $repoRoot 'src\Archiver.CLI\Archiver.CLI.csproj'
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot 'artifacts\cli'
}

if (Test-Path $OutputRoot) {
    Remove-Item -Recurse -Force $OutputRoot
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$architectures = if ($Architecture -eq 'both') { @('x64', 'arm64') } else { @($Architecture) }
$zipPaths = @()

foreach ($arch in $architectures) {
    $rid      = if ($arch -eq 'arm64') { 'win-arm64' } else { 'win-x64' }
    $platform = if ($arch -eq 'arm64') { 'ARM64' }     else { 'x64' }
    $publishDir = Join-Path $OutputRoot $rid

    Write-Host "Publishing Archiver.CLI for $rid ..."
    & dotnet publish $csproj `
        /p:Configuration=$Configuration `
        "/p:Platform=$platform" `
        "/p:RuntimeIdentifier=$rid" `
        /p:SelfContained=true `
        "/p:PublishDir=$publishDir"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid (exit code $LASTEXITCODE)"
    }

    $zipPath = Join-Path $OutputRoot "pakko-$rid.zip"
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force
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
