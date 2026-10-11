#Requires -Version 7.0
<#
.SYNOPSIS
    Runs rows of docs/SMOKE.md against the installed Pakko package (T-F378).
.DESCRIPTION
    The catalogue is docs/SMOKE.md; this script is a filter over its rows. A row that a script can
    run has a step in scripts/smoke/, registered under the row's ID. Rows without a step (the ones
    a person or the agent drives through the real UI) are listed with their action and expectation
    and reported as "manual".

    Fixtures are generated into a new temporary folder on every run and removed at the end. The
    result table is printed and written as Markdown; it belongs in the release record
    (docs/DECISIONS.md), never in the catalogue.

    Windows are opened and closed on the desktop while it runs; do not use the machine meanwhile.
.PARAMETER Tier
    quick (default), release or store. A higher tier includes the lower ones.
.PARAMETER Only
    Row IDs to run, wildcards allowed (PAR-*, CLI-00?). Applied after the tier, unless -AnyTier.
.PARAMETER AnyTier
    With -Only: take the named rows whatever their tier.
.PARAMETER PakkoExe
    The pakko.exe to test. Default: the installed package's.
.PARAMETER ReportPath
    Where to write the Markdown table. Default: smoke-report.md in the temporary folder's parent.
.PARAMETER List
    Print the selected rows and stop.
.PARAMETER KeepWork
    Keep the temporary folder (it is always kept when a row fails).
.PARAMETER Hold
    After the scripted checks of a POL row, leave its policy set until a file named "go" appears
    in the temporary folder: the time to look at the App and the Explorer menu under that policy
    (POL-009, POL-010). PKG-009 waits the same way with the package removed (PKG-010).
.PARAMETER PackageLifecycle
    Run PKG-006..009. They remove and replace the installed package and its data; the run ends
    with the package it began with.
.PARAMETER ReleaseFiles
    A folder that holds the bundle under test: before the tag, the one CI built on the release
    commit (gh run download <run> -n pakko-msix-x64 -D <folder>); after it, the release's
    (gh release download <tag> -D <folder>).
.PARAMETER PreviousRelease
    A folder with the files of the release before it (gh release download <tag> -D <folder>):
    PKG-006..008 update from it and try to go back to it.
.EXAMPLE
    .\Invoke-Smoke.ps1
.EXAMPLE
    .\Invoke-Smoke.ps1 -Tier release -Only PAR-*,POL-005
#>
[CmdletBinding()]
param(
    [ValidateSet('quick', 'release', 'store')]
    [string]$Tier = 'quick',

    [string[]]$Only,

    [switch]$AnyTier,

    [string]$PakkoExe,

    [string]$ReportPath,

    [switch]$List,

    [switch]$KeepWork,

    [switch]$Hold,

    [switch]$PackageLifecycle,

    [string]$ReleaseFiles,

    [string]$PreviousRelease
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# --- the catalogue ---------------------------------------------------------------------------
$rows = foreach ($line in Get-Content -LiteralPath (Join-Path $repo 'docs\SMOKE.md') -Encoding utf8) {
    if ($line -notmatch '^\| [A-Z]{3}-\d{3} \|') { continue }
    $cells = $line.Trim().Trim('|').Split('|').ForEach({ $_.Trim() })
    [pscustomobject]@{ Id = $cells[0]; Tier = $cells[1]; By = $cells[2]; On = $cells[3]; Action = $cells[4]; Expect = $cells[5] }
}

# "pwsh -File" passes "-Only A,B" as one string.
$Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

$tiers = @('quick', 'release', 'store')
$selected = @($rows | Where-Object { $_.Tier -ne 'retired' })
if (-not ($AnyTier -and $Only)) {
    $selected = @($selected | Where-Object { $tiers.IndexOf($_.Tier) -le $tiers.IndexOf($Tier) })
}
if ($Only) {
    $selected = @($selected | Where-Object { $id = $_.Id; @($Only | Where-Object { $id -like $_ }).Count -gt 0 })
}
if ($List) {
    $selected | Format-Table Id, Tier, By, On, Action -Wrap
    return
}
if (-not $selected) { throw "No row of docs/SMOKE.md matches -Tier $Tier -Only $($Only -join ',')." }

# --- what is under test ----------------------------------------------------------------------
$package = Get-AppxPackage -Name 'PavloRybchenko.Pakko' | Select-Object -First 1
if (-not $package) { throw 'Pakko is not installed. Run scripts\Deploy.ps1 first.' }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('pakko-smoke-' + [System.IO.Path]::GetRandomFileName())
$null = [System.IO.Directory]::CreateDirectory($work)

# Shared by every step. Steps read it; only the helpers add to Fixtures and Passwords.
$Smoke = @{
    Repo     = $repo
    Package  = $package
    Install  = $package.InstallLocation
    Pakko    = $PakkoExe ? $PakkoExe : (Join-Path $package.InstallLocation 'pakko.exe')
    Shell    = Join-Path $package.InstallLocation 'Archiver.Shell.exe'
    Work     = $work
    SevenZip = Join-Path $repo 'tests\Archiver.Core.PerformanceTests\Tools\7-Zip\x64\7za.exe'
    Par2     = Join-Path $repo 'artifacts\par2-oracles\par2cmdline\par2.exe'
    Tar      = Join-Path $env:SystemRoot 'System32\tar.exe'
    Started  = Get-Date
    Fixtures = @{}
    # Every password a step gave to pakko or typed into a window; APP-018 looks for them.
    Passwords = [System.Collections.Generic.HashSet[string]]::new()
    Elevated = @{}
    Hold     = [bool]$Hold
    Lifecycle = @{ Asked = [bool]$PackageLifecycle; ReleaseFiles = $ReleaseFiles; PreviousRelease = $PreviousRelease }
}

$steps = @{}
$lastSteps = [System.Collections.Generic.HashSet[string]]::new()
function Add-SmokeStep {
    # -AtEnd: the step looks at what the whole run left behind, so it runs after the others.
    param([Parameter(Mandatory)][string]$Id, [Parameter(Mandatory)][scriptblock]$Body, [switch]$AtEnd)
    if ($steps.ContainsKey($Id)) { throw "Two steps are registered for $Id." }
    $steps[$Id] = $Body
    if ($AtEnd) { $null = $lastSteps.Add($Id) }
}

foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'smoke') -Filter '*.ps1' | Sort-Object Name) {
    . $file.FullName
}

# --- the run ---------------------------------------------------------------------------------
$ordered = @($selected | Where-Object { -not $lastSteps.Contains($_.Id) }) + @($selected | Where-Object { $lastSteps.Contains($_.Id) })
$results = foreach ($row in $ordered) {
    $step = $steps[$row.Id]
    if (-not $step) {
        [pscustomobject]@{ Id = $row.Id; Result = 'manual'; Seconds = ''; Note = "by $($row.By): $($row.Action) => $($row.Expect)" }
        continue
    }

    Write-Host ("{0} ..." -f $row.Id) -NoNewline
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $result = 'OK'
    $note = ''
    try {
        $returned = & $step
        if ($returned) { $note = ($returned | Out-String).Trim() }
    }
    catch [System.OperationCanceledException] {
        # Skip-Smoke: the row could not run here, and says why.
        $result = 'skipped'
        $note = $_.Exception.Message
    }
    catch {
        $result = 'FAIL'
        # Where in the step it failed, not the line of Assert-Smoke itself.
        $frame = @($_.ScriptStackTrace -split '\r?\n' | Where-Object { $_ -notmatch '^at (Assert-\w+|Skip-Smoke),' })[0]
        $note = "$($_.Exception.Message) [$($frame -replace '^.*\\', '')]"
    }
    $watch.Stop()
    Write-Host (" {0} ({1:n1} s)" -f $result, $watch.Elapsed.TotalSeconds)
    [pscustomobject]@{ Id = $row.Id; Result = $result; Seconds = '{0:n1}' -f $watch.Elapsed.TotalSeconds; Note = $note }
}

Get-Process -Name 'Archiver.OperationUi', 'Archiver.Shell' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$null = Stop-SmokeElevated
# A life-cycle row that failed half way may have left another package, or none.
try { Restore-SmokePackage }
catch { Write-Warning "The package this run began with could not be put back ($($Smoke.Lifecycle.Original)): $_" }

# --- the report ------------------------------------------------------------------------------
$failed = @($results | Where-Object Result -eq 'FAIL')
$total = (Get-Date) - $Smoke.Started
$report = [System.Collections.Generic.List[string]]::new()
$report.Add("Smoke run: tier ``$Tier``, package $($package.Version), $(Get-Date -Format 'yyyy-MM-dd HH:mm'), $([int]$total.TotalSeconds) s.")
$report.Add('')
$report.Add('| ID | Result | s | Note |')
$report.Add('|---|---|---|---|')
foreach ($r in $results) {
    $report.Add("| $($r.Id) | $($r.Result) | $($r.Seconds) | $(($r.Note -replace '\r?\n', ' ' -replace '\|', '/')) |")
}
if (-not $ReportPath) { $ReportPath = Join-Path (Split-Path -Parent $work) 'pakko-smoke-report.md' }
Set-Content -LiteralPath $ReportPath -Value $report -Encoding utf8

$results | Format-Table Id, Result, Seconds, Note -Wrap
Write-Host ("{0} rows: {1} OK, {2} FAIL, {3} skipped, {4} manual; {5:n0} s. Report: {6}" -f
    $results.Count, @($results | Where-Object Result -eq 'OK').Count, $failed.Count,
    @($results | Where-Object Result -eq 'skipped').Count, @($results | Where-Object Result -eq 'manual').Count,
    $total.TotalSeconds, $ReportPath)

if ($failed -or $KeepWork) {
    Write-Host "Fixtures kept in $work"
}
else {
    # A scanner or the indexer may still hold a file for a moment: one more try, then say so.
    foreach ($attempt in 1..2) {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path -LiteralPath $work)) { break }
        Start-Sleep -Seconds 2
    }
    if (Test-Path -LiteralPath $work) { Write-Warning "The fixtures could not be removed: $work" }
}

if ($failed) { exit 1 }
