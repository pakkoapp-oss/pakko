#Requires -Version 5.1
<#
.SYNOPSIS
    Writes the winget manifest (version, installer, defaultLocale) for a released pakko CLI zip.
.DESCRIPTION
    T-F317. The CLI zips on a GitHub Release (pakko-win-x64.zip, pakko-win-arm64.zip) are
    self-contained folders with pakko.exe at the root. winget installs them as a portable zip and
    adds the install folder to PATH (ArchiveBinariesDependOnPath), so "pakko" works. The hashes come from the release's own SHA256SUMS, so the
    manifest describes exactly the published files.

    Only writes files. Submitting them to microsoft/winget-pkgs is a separate, manual step (see
    scripts/README.md): it publishes under the project's name.
.PARAMETER Version
    Release version without the leading "v", e.g. 1.6.0.
.PARAMETER Sha256SumsPath
    The release's SHA256SUMS file (default: artifacts/cli/SHA256SUMS, as Publish-Cli.ps1 writes it).
.PARAMETER ReleaseDate
    yyyy-MM-dd (default: today, UTC).
.PARAMETER OutputRoot
    Folder to write into (default: artifacts/winget/<Version>).
.EXAMPLE
    .\New-WingetManifest.ps1 -Version 1.6.0 -Sha256SumsPath .\SHA256SUMS -ReleaseDate 2026-09-30
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,
    [string] $Sha256SumsPath,
    [ValidatePattern('^\d{4}-\d{2}-\d{2}$')]
    [string] $ReleaseDate = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'),
    [string] $OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $Sha256SumsPath) { $Sha256SumsPath = Join-Path $repoRoot 'artifacts\cli\SHA256SUMS' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot "artifacts\winget\$Version" }

$identifier = 'PavloRybchenko.PakkoCLI'
$manifestVersion = '1.12.0'
$releaseUrl = "https://github.com/pakkoapp-oss/pakko/releases/download/v$Version"

# SHA256SUMS lines: "<64 hex>  <file name>"
$hashes = @{}
foreach ($line in Get-Content -LiteralPath $Sha256SumsPath) {
    if ($line -match '^([0-9a-fA-F]{64})\s+\*?(\S+)$') { $hashes[$Matches[2]] = $Matches[1].ToUpperInvariant() }
}
$installers = @(
    @{ Architecture = 'x64'; File = 'pakko-win-x64.zip' },
    @{ Architecture = 'arm64'; File = 'pakko-win-arm64.zip' }
)
foreach ($installer in $installers) {
    if (-not $hashes.ContainsKey($installer.File)) {
        throw "$($installer.File) is not listed in $Sha256SumsPath."
    }
}

$header = "# yaml-language-server: `$schema=https://aka.ms/winget-manifest.{0}.$manifestVersion.schema.json"

$versionYaml = @(
    ($header -f 'version'),
    '',
    "PackageIdentifier: $identifier",
    "PackageVersion: $Version",
    'DefaultLocale: en-US',
    'ManifestType: version',
    "ManifestVersion: $manifestVersion"
)

$installerYaml = @(
    ($header -f 'installer'),
    '',
    "PackageIdentifier: $identifier",
    "PackageVersion: $Version",
    'InstallerType: zip',
    'NestedInstallerType: portable',
    'NestedInstallerFiles:',
    '- RelativeFilePath: pakko.exe',
    # A symlink in WinGet\Links broke the pre-AOT apphost, which looked for pakko.dll next to the
    # link ("The application to execute does not exist", T-F317). So winget puts the install folder
    # itself on PATH; the command is still "pakko". The AOT exe (T-F355) allows a symlink: T-F361.
    'ArchiveBinariesDependOnPath: true',
    'Commands:',
    '- pakko',
    "ReleaseDate: $ReleaseDate",
    'Installers:'
)
foreach ($installer in $installers) {
    $installerYaml += @(
        "- Architecture: $($installer.Architecture)",
        "  InstallerUrl: $releaseUrl/$($installer.File)",
        "  InstallerSha256: $($hashes[$installer.File])"
    )
}
$installerYaml += @('ManifestType: installer', "ManifestVersion: $manifestVersion")

$localeYaml = @(
    ($header -f 'defaultLocale'),
    '',
    "PackageIdentifier: $identifier",
    "PackageVersion: $Version",
    'PackageLocale: en-US',
    'Publisher: Pavlo Rybchenko',
    'PublisherUrl: https://github.com/pakkoapp-oss',
    'PublisherSupportUrl: https://github.com/pakkoapp-oss/pakko/issues',
    'PackageName: Pakko CLI',
    'PackageUrl: https://github.com/pakkoapp-oss/pakko',
    'License: Apache-2.0',
    'LicenseUrl: https://github.com/pakkoapp-oss/pakko/blob/main/LICENSE',
    'ShortDescription: Command-line ZIP and tar archiver with 7-Zip-style commands, built on .NET System.IO.Compression.',
    'Description: pakko is the command line of Pakko, a Windows archiver with a small, auditable attack surface. It creates and extracts ZIP (including WinZip AES) and the tar family, and reads 7z/RAR through the sandboxed Windows tar.exe.',
    'Moniker: pakko-cli',
    'Tags:',
    '- archiver',
    '- zip',
    '- tar',
    '- cli',
    "ReleaseNotesUrl: https://github.com/pakkoapp-oss/pakko/releases/tag/v$Version",
    'ManifestType: defaultLocale',
    "ManifestVersion: $manifestVersion"
)

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$files = @{
    "$identifier.yaml" = $versionYaml
    "$identifier.installer.yaml" = $installerYaml
    "$identifier.locale.en-US.yaml" = $localeYaml
}
foreach ($name in $files.Keys) {
    $text = ($files[$name] -join "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $OutputRoot $name), $text, $utf8NoBom)
}
Write-Host "winget manifest for $identifier $Version written to $OutputRoot"
