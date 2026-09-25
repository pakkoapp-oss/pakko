#Requires -Version 5.1
<#
.SYNOPSIS
    Restores the permissions of one file changed by Pakko's old tar sandbox staging (T-F233).
.DESCRIPTION
    Saves the file's current permissions first (icacls /save, next to this script's run in the
    given backup folder), then:
      1. removes only the explicit entries for Pakko's sandbox SID;
      2. if the file inherits permissions: drops the inherited entries, which came from Pakko's
         temporary folder, not from the file's own folder, and inherits again from the folder
         it is in. A file with inheritance turned off keeps it off.
    Entries the file had explicitly for anyone else are kept. This is not "icacls /reset",
    which would also drop them. Restore the backup with:
      icacls "<folder of the file>" /restore "<backup file>"
    Run Find-PakkoSandboxAce.ps1 first to see which files are affected.
.PARAMETER Path
    The affected file.
.PARAMETER BackupFolder
    Where the permission backup is written. Defaults to the current folder.
.EXAMPLE
    .\Repair-PakkoSandboxAce.ps1 -Path 'C:\Users\me\Downloads\report.tar.gz' -WhatIf
.EXAMPLE
    .\Repair-PakkoSandboxAce.ps1 -Path 'C:\Users\me\Downloads\report.tar.gz'
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [string]$BackupFolder = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
$sandboxSidPattern = '^S-1-15-2-.*-3482888831-986213358-2090939803-3632948546$'
$icacls = Join-Path $env:SystemRoot 'System32\icacls.exe'

$file = Get-Item -LiteralPath $Path
if ($file.PSIsContainer) {
    throw "Not a file: $Path"
}

$acl = Get-Acl -LiteralPath $file.FullName
$sandboxRules = @($acl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier]) |
    Where-Object { $_.IdentityReference.Value -match $sandboxSidPattern })
if ($sandboxRules.Count -eq 0) {
    Write-Host "No Pakko sandbox permission entry on $($file.FullName) - nothing to do."
    return
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $BackupFolder ("{0}.acl-backup-{1}.txt" -f $file.Name, $stamp)

if (-not $PSCmdlet.ShouldProcess($file.FullName, 'Remove Pakko sandbox entries and re-inherit from its folder')) {
    return
}

& $icacls $file.FullName /save $backup | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "icacls /save failed ($LASTEXITCODE); nothing was changed."
}
Write-Host "Saved the current permissions to $backup"

foreach ($sid in ($sandboxRules | ForEach-Object { $_.IdentityReference.Value } | Sort-Object -Unique)) {
    & $icacls $file.FullName /remove:g "*$sid" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "icacls /remove:g failed ($LASTEXITCODE). Restore with: icacls `"$($file.DirectoryName)`" /restore `"$backup`""
    }
}

# A file whose inheritance was turned off on purpose kept it off - the old grant only added an
# explicit entry there - so only an inheriting file gets its inherited entries recomputed.
if (-not $acl.AreAccessRulesProtected) {
    & $icacls $file.FullName /inheritance:r | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "icacls /inheritance:r failed ($LASTEXITCODE). Restore with: icacls `"$($file.DirectoryName)`" /restore `"$backup`""
    }
    & $icacls $file.FullName /inheritance:e | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "icacls /inheritance:e failed ($LASTEXITCODE). Restore with: icacls `"$($file.DirectoryName)`" /restore `"$backup`""
    }
}

Write-Host "Repaired: $($file.FullName)"
& $icacls $file.FullName
