#Requires -Version 7.0
<#
.SYNOPSIS
    Downloads the three PAR2 tools the recovery-data tests check Pakko against (T-F275).
.DESCRIPTION
    par2cmdline (the reference implementation), par2cmdline-turbo and MultiPar's par2j, each
    pinned to one release and its SHA-256 (the digest GitHub publishes for the release asset).
    They are GPL-2.0 programs, so they are downloaded, never committed. Each tool goes into its own
    folder under the output folder; a tool already there with the expected version is kept.
    MultiPar ships x64 only; on an ARM64 machine it runs under emulation.
    What each tool is used for: docs/TESTING.md, "PAR2 Oracles".
.PARAMETER Architecture
    x64 or arm64 for par2cmdline and par2cmdline-turbo. Defaults to the machine's architecture.
.PARAMETER OutputPath
    Defaults to artifacts\par2-oracles in the repository (git-ignored).
.EXAMPLE
    .\Get-Par2Oracles.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string] $Architecture = $(if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }),
    [string] $OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\par2-oracles')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$tools = @(
    @{
        Name   = 'par2cmdline'
        Url    = "https://github.com/Parchive/par2cmdline/releases/download/v1.4.0/par2cmdline-1.4.0-win-$Architecture.zip"
        Sha256 = @{ x64 = '200130143dba1e12f9d1be567d5a446e785d2093e5d735225d8ad0539125b71c'; arm64 = '302967d1cfc5c709a51c4e91727a83a868b91074ee0902c71dcf31abbfeb88be' }[$Architecture]
        Exe    = 'par2.exe'
    },
    @{
        Name   = 'par2cmdline-turbo'
        Url    = "https://github.com/animetosho/par2cmdline-turbo/releases/download/v1.5.0/par2cmdline-turbo-1.5.0-win-$Architecture.zip"
        Sha256 = @{ x64 = '873a6f25822415f432224cc6f734407ca2732004184e002960fc68ed1e99c5d7'; arm64 = 'bb73312747b706dcedd13b8d4de7acd20e17367ebb0b26ebc3ea7d55ea744735' }[$Architecture]
        Exe    = 'par2.exe'
    },
    @{
        Name   = 'multipar'
        Url    = 'https://github.com/Yutaka-Sawada/MultiPar/releases/download/v1.3.3.6/MultiPar1336.zip'
        Sha256 = '7c60412d8ac2183afb1812ac145cbe0d0dce2a8156beaf917f65b1668a1fcc7b'
        Exe    = 'par2j64.exe'
    }
)

$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

foreach ($tool in $tools) {
    $folder = Join-Path $OutputPath $tool.Name
    $stamp = Join-Path $folder 'source.sha256'
    if ((Test-Path -LiteralPath $stamp) -and (Get-Content -LiteralPath $stamp -Raw).Trim() -eq $tool.Sha256 -and
        (Test-Path -LiteralPath (Join-Path $folder $tool.Exe))) {
        Write-Output "$($tool.Name): already present"
        continue
    }

    $zip = Join-Path $OutputPath "$($tool.Name).zip"
    Invoke-WebRequest -Uri $tool.Url -OutFile $zip -UseBasicParsing
    $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $tool.Sha256) {
        Remove-Item -LiteralPath $zip -Force
        throw "$($tool.Name): SHA-256 $actual does not match the pinned $($tool.Sha256) ($($tool.Url))"
    }

    if (Test-Path -LiteralPath $folder) {
        Remove-Item -LiteralPath $folder -Recurse -Force
    }
    Expand-Archive -LiteralPath $zip -DestinationPath $folder
    Remove-Item -LiteralPath $zip -Force
    if (-not (Test-Path -LiteralPath (Join-Path $folder $tool.Exe))) {
        throw "$($tool.Name): $($tool.Exe) not found in the downloaded archive"
    }
    Set-Content -LiteralPath $stamp -Value $tool.Sha256 -NoNewline
    Write-Output "$($tool.Name): downloaded, SHA-256 verified"
}
