#Requires -Version 5.1
<#
.SYNOPSIS
    Lists files whose permissions still carry an ACE for Pakko's tar sandbox (T-F233). Read-only.
.DESCRIPTION
    Pakko versions before the T-F233 fix staged a tar-family archive (.tar, .gz, .7z, .rar, ...)
    for its sandbox by hardlinking it into %TEMP%, then granted the sandbox's AppContainer SID
    read access on that link. A hardlink is the same file, so the grant landed on the user's own
    archive, and the permissions it inherited from its folder were recomputed from the temporary
    folder instead: other users/groups could lose access. This script only reads permissions and
    prints every affected file; Repair-PakkoSandboxAce.ps1 fixes one.
.PARAMETER Path
    Folders to search recursively. Defaults to Desktop, Documents and Downloads.
.PARAMETER AllFiles
    Check every file, not only the archive extensions Pakko sends to tar.exe (a renamed archive
    was affected too).
.EXAMPLE
    .\Find-PakkoSandboxAce.ps1
.EXAMPLE
    .\Find-PakkoSandboxAce.ps1 -Path D:\Shared -AllFiles
#>
[CmdletBinding()]
param(
    [string[]]$Path = @(
        [Environment]::GetFolderPath('Desktop'),
        [Environment]::GetFolderPath('MyDocuments'),
        (Join-Path $env:USERPROFILE 'Downloads')
    ),
    [switch]$AllFiles
)

# Pakko's sandbox profile is "Pakko.TarSandbox". Its AppContainer SID differs between the
# standalone pakko.exe and the installed package in the leading sub-authorities, but both end
# with these four (observed 2026-09-24 on both).
$sandboxSidPattern = '^S-1-15-2-.*-3482888831-986213358-2090939803-3632948546$'
$archiveExtensions = @('.rar', '.7z', '.tar', '.gz', '.tgz', '.bz2', '.tbz2', '.xz', '.txz', '.zst', '.tzst', '.lzma')

$found = 0
foreach ($root in $Path) {
    if (-not (Test-Path -LiteralPath $root)) {
        continue
    }
    Get-ChildItem -LiteralPath $root -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $AllFiles -or ($archiveExtensions -contains $_.Extension.ToLowerInvariant()) } |
        ForEach-Object {
            $file = $_
            try {
                $acl = Get-Acl -LiteralPath $file.FullName
            }
            catch {
                Write-Warning "Cannot read permissions: $($file.FullName)"
                return
            }
            $rules = $acl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier]) |
                Where-Object { $_.IdentityReference.Value -match $sandboxSidPattern }
            if ($rules) {
                $found++
                [pscustomobject]@{
                    Path      = $file.FullName
                    Explicit  = @($rules | Where-Object { -not $_.IsInherited }).Count
                    Inherited = @($rules | Where-Object { $_.IsInherited }).Count
                }
            }
        }
}

if ($found -eq 0) {
    Write-Host 'No files with a Pakko sandbox permission entry were found.'
}
else {
    Write-Host "$found affected file(s). Fix one with: .\Repair-PakkoSandboxAce.ps1 -Path '<file>'"
}
