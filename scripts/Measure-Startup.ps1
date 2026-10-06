#Requires -Version 5.1
<#
.SYNOPSIS
    Measures how fast the installed Pakko package starts (T-F346).
.DESCRIPTION
    Runs each scenario several times against the installed package and prints the median and
    the minimum, so a change can be compared with the build before it. The first run of every
    scenario is dropped (it warms the file cache). There is no pass/fail threshold: the numbers
    depend on the machine, so compare two runs taken on the same one.

    Scenarios:
      App          Archiver.App.exe start -> main window visible; then working set, private
                   bytes, threads and CPU two seconds later.
      Open         Archiver.Shell.exe --open-ui --browse <zip> (Explorer's "Open") -> App window
                   visible.
      ExtractHere  Archiver.Shell.exe --extract-here on a 200-file ZIP: last file written, and
                   Shell exit.
      Cli          pakko --help and pakko l <zip> from the package.
      LongExtract  Archiver.Shell.exe --extract-here on a 4000-file ZIP -> the operation window
                   (Archiver.OperationUi) visible.
      Conflict     Archiver.Shell.exe --extract-here onto an existing file -> the conflict prompt
                   visible.

    Windows are opened and closed on the desktop while it runs; do not use the machine meanwhile.
.PARAMETER Runs
    Launches per scenario, the dropped first one included (default 8).
.PARAMETER Scenario
    Which scenarios to run (default: all).
.EXAMPLE
    .\Measure-Startup.ps1
.EXAMPLE
    .\Measure-Startup.ps1 -Runs 12 -Scenario App,Open
#>
[CmdletBinding()]
param(
    [ValidateRange(3, 100)]
    [int]$Runs = 8,

    [ValidateSet('App', 'Open', 'ExtractHere', 'Cli', 'LongExtract', 'Conflict')]
    [string[]]$Scenario = @('App', 'Open', 'ExtractHere', 'Cli', 'LongExtract', 'Conflict')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -Namespace PakkoMeasure -Name Native -MemberDefinition @'
[DllImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
public static extern bool IsWindowVisible(IntPtr hWnd);
'@

$package = Get-AppxPackage -Name '*Pakko*' | Select-Object -First 1
if (-not $package) {
    throw 'Pakko is not installed. Run Deploy.ps1 first.'
}
$installDir = $package.InstallLocation
$appExe = Join-Path $installDir 'Archiver.App.exe'
$shellExe = Join-Path $installDir 'Archiver.Shell.exe'
$cliExe = Join-Path $installDir 'pakko.exe'

$workDir = Join-Path ([IO.Path]::GetTempPath()) ('PakkoMeasure-' + [Guid]::NewGuid().ToString('N'))
$timeoutMs = 20000

function Get-Summary {
    param([string]$Name, [double[]]$Values)

    # The first run warms the cache and is not counted.
    $kept = @($Values | Select-Object -Skip 1 | Sort-Object)
    [pscustomobject]@{
        Measure = $Name
        Median  = [math]::Round($kept[[int][math]::Floor($kept.Count / 2)])
        Min     = [math]::Round($kept[0])
        Max     = [math]::Round($kept[$kept.Count - 1])
        Runs    = $kept.Count
    }
}

function Get-NewAppProcess {
    param([int[]]$KnownIds, [string]$Name = 'Archiver.App')

    Get-Process -Name $Name -ErrorAction SilentlyContinue |
        Where-Object { $KnownIds -notcontains $_.Id } |
        Select-Object -First 1
}

function Wait-VisibleWindow {
    param([System.Diagnostics.Process]$Process, [System.Diagnostics.Stopwatch]$Watch)

    while ($Watch.ElapsedMilliseconds -lt $timeoutMs) {
        $Process.Refresh()
        if ($Process.MainWindowHandle -ne [IntPtr]::Zero -and
            [PakkoMeasure.Native]::IsWindowVisible($Process.MainWindowHandle)) {
            return $Watch.Elapsed.TotalMilliseconds
        }
        Start-Sleep -Milliseconds 5
    }
    throw "No visible window within $timeoutMs ms."
}

function Close-App {
    param([System.Diagnostics.Process]$Process)

    # A normal close, so the tray icon goes with the window.
    $Process.Refresh()
    $null = $Process.CloseMainWindow()
    if (-not $Process.WaitForExit(5000)) {
        Stop-Process -Id $Process.Id -Force
    }
    Start-Sleep -Milliseconds 500
}

function Initialize-SampleZip {
    $source = Join-Path $workDir 'src'
    $null = New-Item -ItemType Directory -Force -Path $source
    foreach ($i in 1..200) {
        [IO.File]::WriteAllText((Join-Path $source "f$i.txt"), ("line $i " * 200))
    }
    $zip = Join-Path $workDir 't.zip'
    Compress-Archive -Path (Join-Path $source 'f*.txt') -DestinationPath $zip -Force
    $zip
}

function Initialize-EntriesZip {
    param([string]$Name, [int]$Entries)

    Add-Type -AssemblyName System.IO.Compression
    $bytes = [Text.Encoding]::ASCII.GetBytes(('line of sample text ' * 800))
    $zip = Join-Path $workDir $Name
    $file = [IO.File]::Create($zip)
    try {
        $archive = New-Object IO.Compression.ZipArchive($file, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($i in 1..$Entries) {
                $stream = $archive.CreateEntry("f$i.txt", [IO.Compression.CompressionLevel]::Fastest).Open()
                try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $file.Dispose() }
    $zip
}

function Measure-OperationWindow {
    param([string]$Zip, [int]$Count, [string]$Label, [switch]$Conflict)

    $window = @()
    foreach ($run in 1..$Count) {
        $folder = Join-Path $workDir "$Label$run"
        $null = New-Item -ItemType Directory -Force -Path $folder
        $copy = Join-Path $folder 't.zip'
        Copy-Item -LiteralPath $Zip -Destination $copy
        if ($Conflict) {
            # A one-file archive lands next to itself, so this file is the conflict.
            [IO.File]::WriteAllText((Join-Path $folder 'f1.txt'), 'already here')
        }

        $known = @(Get-Process -Name 'Archiver.OperationUi' -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        $shell = Start-Process -FilePath $shellExe -ArgumentList @('--extract-here', "`"$copy`"") -PassThru
        $shownAt = $null
        $helper = $null
        while ($null -eq $shownAt -and -not $shell.HasExited -and $watch.ElapsedMilliseconds -lt $timeoutMs) {
            if (-not $helper) { $helper = Get-NewAppProcess -KnownIds $known -Name 'Archiver.OperationUi' }
            if ($helper) {
                $helper.Refresh()
                if (-not $helper.HasExited -and $helper.MainWindowHandle -ne [IntPtr]::Zero -and
                    [PakkoMeasure.Native]::IsWindowVisible($helper.MainWindowHandle)) {
                    $shownAt = $watch.Elapsed.TotalMilliseconds
                }
            }
            if ($null -eq $shownAt) { Start-Sleep -Milliseconds 5 }
        }

        if ($Conflict) {
            # Shell first: with the helper gone first it would fall back to a Win32 prompt.
            if (-not $shell.HasExited) { Stop-Process -Id $shell.Id -Force }
            if ($helper -and -not $helper.WaitForExit(3000)) { Stop-Process -Id $helper.Id -Force }
        }
        elseif (-not $shell.WaitForExit($timeoutMs)) {
            Stop-Process -Id $shell.Id -Force
            throw "Archiver.Shell did not exit within $timeoutMs ms."
        }
        if ($null -eq $shownAt) { throw "$Label`: the operation window never became visible." }
        $window += $shownAt
        Start-Sleep -Milliseconds 500
    }
    Get-Summary -Name "$Label`: Shell start -> operation window visible, ms" -Values $window
}

function Measure-App {
    param([int]$Count)

    $window = @(); $cpu = @(); $workingSet = @(); $private = @(); $threads = @()
    foreach ($run in 1..$Count) {
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        $process = Start-Process -FilePath $appExe -PassThru
        $window += Wait-VisibleWindow -Process $process -Watch $watch
        Start-Sleep -Milliseconds 2000
        $process.Refresh()
        $cpu += $process.TotalProcessorTime.TotalMilliseconds
        $workingSet += $process.WorkingSet64 / 1MB
        $private += $process.PrivateMemorySize64 / 1MB
        $threads += $process.Threads.Count
        Close-App -Process $process
    }
    Get-Summary -Name 'App: start -> window visible, ms' -Values $window
    Get-Summary -Name 'App: CPU after 2 s, ms' -Values $cpu
    Get-Summary -Name 'App: working set, MB' -Values $workingSet
    Get-Summary -Name 'App: private bytes, MB' -Values $private
    Get-Summary -Name 'App: threads' -Values $threads
}

function Measure-Open {
    param([string]$Zip, [int]$Count)

    $window = @()
    foreach ($run in 1..$Count) {
        $known = @(Get-Process -Name 'Archiver.App' -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        $null = Start-Process -FilePath $shellExe -ArgumentList @('--open-ui', '--browse', "`"$Zip`"") -PassThru
        $app = $null
        while (-not $app -and $watch.ElapsedMilliseconds -lt $timeoutMs) {
            $app = Get-NewAppProcess -KnownIds $known
            if (-not $app) { Start-Sleep -Milliseconds 5 }
        }
        if (-not $app) { throw "The App did not start within $timeoutMs ms." }
        $window += Wait-VisibleWindow -Process $app -Watch $watch
        Start-Sleep -Milliseconds 1500
        Close-App -Process $app
    }
    Get-Summary -Name 'Explorer Open: Shell start -> App window visible, ms' -Values $window
}

function Measure-ExtractHere {
    param([string]$Zip, [int]$Count)

    $written = @(); $exited = @()
    foreach ($run in 1..$Count) {
        $folder = Join-Path $workDir "x$run"
        $null = New-Item -ItemType Directory -Force -Path $folder
        $copy = Join-Path $folder 't.zip'
        Copy-Item -LiteralPath $Zip -Destination $copy
        $lastFile = Join-Path $folder 't\f200.txt'

        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        $shell = Start-Process -FilePath $shellExe -ArgumentList @('--extract-here', "`"$copy`"") -PassThru
        $writtenAt = $null
        while (-not $shell.HasExited -and $watch.ElapsedMilliseconds -lt $timeoutMs) {
            if ($null -eq $writtenAt -and (Test-Path -LiteralPath $lastFile)) {
                $writtenAt = $watch.Elapsed.TotalMilliseconds
            }
            Start-Sleep -Milliseconds 5
        }
        if (-not $shell.HasExited) {
            Stop-Process -Id $shell.Id -Force
            throw "Archiver.Shell did not exit within $timeoutMs ms (a dialog may be waiting)."
        }
        $exitedAt = $watch.Elapsed.TotalMilliseconds
        if ($null -eq $writtenAt) {
            if (-not (Test-Path -LiteralPath $lastFile)) { throw "Extraction wrote no files into $folder." }
            $writtenAt = $exitedAt
        }
        $written += $writtenAt
        $exited += $exitedAt
        Start-Sleep -Milliseconds 300
    }
    Get-Summary -Name 'Explorer Extract here (200 files): files written, ms' -Values $written
    Get-Summary -Name 'Explorer Extract here (200 files): Shell exit, ms' -Values $exited
}

function Measure-Cli {
    param([string]$Zip, [int]$Count)

    $sink = Join-Path $workDir 'cli-out.txt'
    $cases = [ordered]@{
        'pakko --help, ms'  = @('--help')
        'pakko l <zip>, ms' = @('l', "`"$Zip`"")
    }
    foreach ($name in $cases.Keys) {
        $times = @()
        foreach ($run in 1..$Count) {
            $watch = [System.Diagnostics.Stopwatch]::StartNew()
            $process = Start-Process -FilePath $cliExe -ArgumentList $cases[$name] -PassThru -WindowStyle Hidden -RedirectStandardOutput $sink
            $process.WaitForExit()
            $times += $watch.Elapsed.TotalMilliseconds
        }
        Get-Summary -Name $name -Values $times
    }
}

$null = New-Item -ItemType Directory -Force -Path $workDir
try {
    $zip = Initialize-SampleZip
    $rows = @()
    if ($Scenario -contains 'App') { $rows += Measure-App -Count $Runs }
    if ($Scenario -contains 'Open') { $rows += Measure-Open -Zip $zip -Count $Runs }
    if ($Scenario -contains 'ExtractHere') { $rows += Measure-ExtractHere -Zip $zip -Count $Runs }
    if ($Scenario -contains 'Cli') { $rows += Measure-Cli -Zip $zip -Count $Runs }
    if ($Scenario -contains 'LongExtract') {
        $rows += Measure-OperationWindow -Zip (Initialize-EntriesZip -Name 'long.zip' -Entries 4000) -Count $Runs -Label 'Long extraction'
    }
    if ($Scenario -contains 'Conflict') {
        $rows += Measure-OperationWindow -Zip (Initialize-EntriesZip -Name 'one.zip' -Entries 1) -Count $Runs -Label 'Conflict prompt' -Conflict
    }

    Write-Output "Package: $($package.PackageFullName)"
    Write-Output "App build: $((Get-Item -LiteralPath (Join-Path $installDir 'Archiver.App.dll')).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"
    $rows | Format-Table -AutoSize | Out-String | Write-Output
}
finally {
    if (Test-Path -LiteralPath $workDir) {
        [IO.Directory]::Delete($workDir, $true)
    }
}
