#Requires -Version 7.0
<#
.SYNOPSIS
    Times pakko against NanaZip's console and 7-Zip on the same data (T-F565).
.DESCRIPTION
    For each data set, format and level: create, extract and test with each tool, the tools taking
    turns within a run so that heat and background load fall on all of them alike. Writes one CSV
    line per step (seconds with a dot, whatever the system's culture) and prints a line per run.
    There is no pass/fail: compare two runs taken on the same machine, and compare tools at the
    same archive size, not at the same -mx (Pakko's ZIP levels are zlib's 1/6/9, 7-Zip's Deflate
    searches far longer at mx5 and mx9; docs/DECISIONS.md, "Pakko against NanaZip and 7-Zip").

    -Prepare builds the data under -Root first: src\many (a copy of an installed .NET SDK),
    src\one.tar (the same as one tar file) and src\media (30 files of 10 MB of random bytes).
    Measure with Defender's real-time protection on, as users run, and once on a Dev Drive and
    once on a plain NTFS folder: they differ in how Defender scans and where the quarantine goes.
.PARAMETER Root
    The folder that holds src\ and gets work\ (deleted and made again before each step).
.PARAMETER Csv
    The CSV file the results are appended to; created with a header if it does not exist.
.PARAMETER Prepare
    Build src\ under -Root from the newest installed .NET SDK before measuring.
.PARAMETER Pakko
    pakko.exe to measure; the installed package's alias by default.
#>
param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$Csv,
    [switch]$Prepare,
    [int]$Runs = 3,
    [ValidateSet('many', 'one', 'media')][string[]]$Datasets = @('many', 'one', 'media'),
    [ValidateSet('zip', 'tar', '7z')][string[]]$Formats = @('zip', 'tar', '7z'),
    [ValidateRange(0, 9)][int[]]$Levels = @(1, 5, 9),
    [string]$Pakko = (Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\pakko.exe'),
    [string]$NanaZip = (Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\NanaZipC.exe'),
    [string]$SevenZip = (Join-Path $PSScriptRoot '..\tests\Archiver.Core.PerformanceTests\Tools\7-Zip\x64\7za.exe')
)
$ErrorActionPreference = 'Stop'
$invariant = [Globalization.CultureInfo]::InvariantCulture
$tools = [ordered]@{ pakko = $Pakko; nanazip = $NanaZip; '7za' = $SevenZip }
foreach ($name in $tools.Keys) {
    if (-not (Test-Path -LiteralPath $tools[$name])) { throw "$name not found: $($tools[$name])" }
}
$sources = @{
    many  = Join-Path $Root 'src\many'
    one   = Join-Path $Root 'src\one.tar'
    media = Join-Path $Root 'src\media'
}
$work = Join-Path $Root 'work'

function Build-DataSet {
    $sdk = Get-ChildItem -LiteralPath 'C:\Program Files\dotnet\sdk' -Directory |
        Sort-Object { [version]($_.Name -replace '-.*$', '') } | Select-Object -Last 1
    if (-not $sdk) { throw 'No .NET SDK under C:\Program Files\dotnet\sdk' }
    New-Item -ItemType Directory -Force -Path (Join-Path $Root 'src') | Out-Null
    if (-not (Test-Path -LiteralPath $sources.many)) { Copy-Item -LiteralPath $sdk.FullName -Destination $sources.many -Recurse }
    if (-not (Test-Path -LiteralPath $sources.one)) {
        & C:\Windows\System32\tar.exe -cf $sources.one -C (Join-Path $Root 'src') many
        if ($LASTEXITCODE -ne 0) { throw "tar.exe -cf: exit $LASTEXITCODE" }
    }
    if (-not (Test-Path -LiteralPath $sources.media)) {
        New-Item -ItemType Directory -Path $sources.media | Out-Null
        $buffer = [byte[]]::new(10MB)
        foreach ($i in 1..30) {
            [Security.Cryptography.RandomNumberGenerator]::Fill($buffer)
            [IO.File]::WriteAllBytes((Join-Path $sources.media "clip$i.bin"), $buffer)
        }
    }
    "data from $($sdk.FullName)"
}

# Exit 1 is a warning in both pakko and 7-Zip (a tar test says only the structure was checked).
function Invoke-Timed([string]$Exe, [string[]]$Arguments) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $output = & $Exe @Arguments 2>&1 | Out-String
    $watch.Stop()
    if ($LASTEXITCODE -gt 1) { throw "$Exe $($Arguments -join ' '): exit $LASTEXITCODE`n$output" }
    $watch.Elapsed.TotalSeconds
}

function Clear-WorkFolder {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    New-Item -ItemType Directory -Path $work | Out-Null
}

if ($Prepare) { Build-DataSet }
foreach ($ds in $Datasets) {
    if (-not (Test-Path -LiteralPath $sources[$ds])) { throw "No data set $ds under $Root\src: run with -Prepare" }
}
if (-not (Test-Path -LiteralPath $Csv)) { 'dataset,format,level,op,tool,run,seconds,bytes' | Set-Content -LiteralPath $Csv }

foreach ($ds in $Datasets) {
    foreach ($format in $Formats) {
        $formatLevels = if ($format -eq 'tar') { @(0) } else { $Levels }
        foreach ($level in $formatLevels) {
            for ($run = 1; $run -le $Runs; $run++) {
                foreach ($name in $tools.Keys) {
                    Clear-WorkFolder
                    $archive = Join-Path $work "a.$format"
                    $quiet = if ($name -eq 'pakko') { @() } else { @('-bso0', '-bsp0') }
                    $levelSwitch = if ($format -eq 'tar') { @() } else { @("-mx=$level") }
                    $create = Invoke-Timed $tools[$name] (@('a', "-t$format") + $levelSwitch + $quiet + @($archive, $sources[$ds]))
                    $size = (Get-Item -LiteralPath $archive).Length
                    $extract = Invoke-Timed $tools[$name] (@('x', '-y') + $quiet + @("-o$(Join-Path $work 'x')", $archive))
                    $test = Invoke-Timed $tools[$name] (@('t') + $quiet + @($archive))
                    foreach ($step in @(@('a', $create), @('x', $extract), @('t', $test))) {
                        [string]::Format($invariant, '{0},{1},{2},{3},{4},{5},{6:0.000},{7}', $ds, $format, $level, $step[0], $name, $run, $step[1], $size) |
                            Add-Content -LiteralPath $Csv
                    }
                    [string]::Format($invariant, '{0} {1} mx{2} run{3} {4}: a {5:0.0} s, x {6:0.0} s, t {7:0.0} s, {8:0.0} MB',
                        $ds, $format, $level, $run, $name, $create, $extract, $test, $size / 1MB)
                }
            }
        }
    }
}
Clear-WorkFolder
