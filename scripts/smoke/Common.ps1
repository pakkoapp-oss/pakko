#Requires -Version 7.0
# Helpers shared by the steps of scripts/Invoke-Smoke.ps1 (T-F378). Dot-sourced by it: $Smoke is
# the run's context.

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

function Assert-Smoke {
    param([Parameter(Mandatory)][AllowNull()]$Condition, [Parameter(Mandatory)][string]$Message)
    if (-not $Condition) { throw $Message }
}

function Skip-Smoke {
    param([Parameter(Mandatory)][string]$Reason)
    throw [System.OperationCanceledException]::new($Reason)
}

# A fresh folder for one step, under the run's temporary folder.
function Get-StepFolder {
    param([Parameter(Mandatory)][string]$Name)
    $path = Join-Path $Smoke.Work $Name
    $null = [System.IO.Directory]::CreateDirectory($path)
    $path
}

function Invoke-Tool {
    # -CommandLine: the arguments as one string, for a program that parses its own (cmd.exe).
    # A program still running after -TimeoutSeconds is killed with its children, and the step fails.
    param([Parameter(Mandatory)][string]$Exe, [string[]]$Arguments = @(), [string]$WorkingDirectory, [string]$CommandLine, [int]$TimeoutSeconds = 900)
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($Exe)
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
    if ($CommandLine) { $startInfo.Arguments = $CommandLine }
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    if ($WorkingDirectory) { $startInfo.WorkingDirectory = $WorkingDirectory }
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $errorText = $process.StandardError.ReadToEndAsync()
        $outputText = $process.StandardOutput.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            throw "$(Split-Path -Leaf $Exe) $($Arguments -join ' ')$CommandLine did not end in $TimeoutSeconds s"
        }
        $process.WaitForExit()
        $watch.Stop()
        $output = $outputText.GetAwaiter().GetResult()
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output   = $output
            Error    = $errorText.GetAwaiter().GetResult()
            Text     = $output + $errorText.GetAwaiter().GetResult()
            Seconds  = $watch.Elapsed.TotalSeconds
        }
    }
    finally { $process.Dispose() }
}

function Invoke-Pakko {
    param([Parameter(Mandatory)][string[]]$Arguments, [string]$WorkingDirectory, [int]$TimeoutSeconds = 900)
    Invoke-Tool -Exe $Smoke.Pakko -Arguments $Arguments -WorkingDirectory $WorkingDirectory -TimeoutSeconds $TimeoutSeconds
}

# One line as typed at cmd.exe's prompt: pipes and redirections are cmd's.
function Invoke-CmdLine {
    param([Parameter(Mandatory)][string]$Line, [string]$WorkingDirectory)
    Invoke-Tool -Exe (Join-Path $env:SystemRoot 'System32\cmd.exe') -CommandLine "/d /s /c `"$Line`"" -WorkingDirectory $WorkingDirectory
}

# A ustar file written byte by byte: the names are stored exactly as given, which tar.exe will
# not do for a name that leaves the folder or is not valid UTF-8.
function New-TarFile {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Writes a fixture inside the run''s own temporary folder.')]
    param(
        [Parameter(Mandatory)][string]$Path,
        # Each: @{ Name = [byte[]] or string; Content = string; Modified = [datetime]; Folder = $true }
        [Parameter(Mandatory)][hashtable[]]$Entries
    )
    $stream = [System.IO.File]::Create($Path)
    try {
        foreach ($entry in $Entries) {
            $name = $entry.Name -is [byte[]] ? $entry.Name : [System.Text.Encoding]::UTF8.GetBytes($entry.Name)
            $content = [System.Text.Encoding]::UTF8.GetBytes([string]$entry.Content)
            $modified = $entry.Modified ? [datetime]$entry.Modified : [datetime]::UtcNow
            $header = [byte[]]::new(512)
            [System.Array]::Copy($name, $header, [System.Math]::Min($name.Length, 100))
            $fields = @(
                @{ Offset = 100; Text = '0000644' }, @{ Offset = 108; Text = '0000000' }, @{ Offset = 116; Text = '0000000' }
                @{ Offset = 124; Text = [System.Convert]::ToString($content.Length, 8).PadLeft(11, '0') }
                @{ Offset = 136; Text = [System.Convert]::ToString([System.DateTimeOffset]::new($modified.ToUniversalTime()).ToUnixTimeSeconds(), 8).PadLeft(11, '0') }
                @{ Offset = 148; Text = '        ' }, @{ Offset = 156; Text = $entry.Folder ? '5' : '0' }, @{ Offset = 257; Text = 'ustar' }, @{ Offset = 263; Text = '00' }
            )
            foreach ($field in $fields) {
                $bytes = [System.Text.Encoding]::ASCII.GetBytes($field.Text)
                [System.Array]::Copy($bytes, 0, $header, $field.Offset, $bytes.Length)
            }
            $sum = 0
            foreach ($byte in $header) { $sum += $byte }
            $checksum = [System.Text.Encoding]::ASCII.GetBytes([System.Convert]::ToString($sum, 8).PadLeft(6, '0'))
            [System.Array]::Copy($checksum, 0, $header, 148, 6)
            $header[154] = 0
            $stream.Write($header, 0, 512)
            $stream.Write($content, 0, $content.Length)
            $padding = (512 - $content.Length % 512) % 512
            $stream.Write([byte[]]::new($padding), 0, $padding)
        }
        $stream.Write([byte[]]::new(1024), 0, 1024)
    }
    finally { $stream.Dispose() }
}

# What tar extraction of this run left in %TEMP%: staging folders and quarantined items.
function Get-TarLeftover {
    param([Parameter(Mandatory)][datetime]$Since)
    $temp = [System.IO.Path]::GetTempPath()
    @(Get-ChildItem -LiteralPath $temp -Directory -Filter 'PakkoTarStage_*' -ErrorAction SilentlyContinue | Where-Object CreationTime -ge $Since)
    $sandbox = Join-Path $temp 'PakkoTarSandbox'
    if (Test-Path -LiteralPath $sandbox) {
        @(Get-ChildItem -LiteralPath $sandbox -ErrorAction SilentlyContinue | Where-Object CreationTime -ge $Since)
    }
}

# pakko in a hidden console of its own, as a person's terminal gives it one: the process to send
# Ctrl+C to, or to kill. Its output is not captured.
function Start-PakkoInConsole {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Starts the process a smoke step asked for.')]
    param([Parameter(Mandatory)][string[]]$Arguments)
    $quoted = $Arguments | ForEach-Object { $_ -match '\s' ? "`"$_`"" : $_ }
    $process = Start-Process -FilePath $Smoke.Pakko -ArgumentList $quoted -WindowStyle Hidden -PassThru
    # Without this the exit code of a process started this way cannot be read later.
    $null = $process.Handle
    $process
}

# Ctrl+C for a process from Start-PakkoInConsole. The sender takes a second to start: get it
# with -Prepare before the process to cancel is started, then call again with -Signal.
function Send-CtrlC {
    param([switch]$Prepare, [hashtable]$Signal, [System.Diagnostics.Process]$Process)
    if ($Prepare) {
        $folder = Get-StepFolder ('ctrlc-' + [System.IO.Path]::GetRandomFileName())
        $script = Join-Path $PSScriptRoot 'tools\Send-CtrlC.ps1'
        $helper = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile', '-File', "`"$script`"", '-Folder', "`"$folder`"") -WindowStyle Hidden -PassThru
        $deadline = (Get-Date).AddSeconds(30)
        while (-not (Test-Path -LiteralPath (Join-Path $folder 'ready')) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 50 }
        Assert-Smoke (Test-Path -LiteralPath (Join-Path $folder 'ready')) 'the Ctrl+C sender did not start'
        return @{ Folder = $folder; Helper = $helper }
    }

    Set-Content -LiteralPath (Join-Path $Signal.Folder 'target') -Value $Process.Id -NoNewline
    $sent = Join-Path $Signal.Folder 'sent'
    $deadline = (Get-Date).AddSeconds(10)
    while (-not (Test-Path -LiteralPath $sent) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 20 }
    Assert-Smoke ((Test-Path -LiteralPath $sent) -and (Get-Content -LiteralPath $sent -Raw).Trim() -eq 'True') 'Ctrl+C could not be sent to the process'
}

# Waits until -Condition is true; fails the step after -TimeoutSeconds.
function Wait-Smoke {
    param([Parameter(Mandatory)][scriptblock]$Condition, [Parameter(Mandatory)][string]$What, [int]$TimeoutSeconds = 30)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (& $Condition)) {
        if ((Get-Date) -gt $deadline) { throw "$What did not happen in $TimeoutSeconds s" }
        Start-Sleep -Milliseconds 20
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string]$Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

# Relative path -> hash for every file under a folder; two trees are equal when these are.
function Get-TreeHash {
    param([Parameter(Mandatory)][string]$Path)
    $map = [ordered]@{}
    foreach ($file in Get-ChildItem -LiteralPath $Path -Recurse -File | Sort-Object FullName) {
        $map[$file.FullName.Substring($Path.Length).TrimStart('\')] = Get-Sha256 $file.FullName
    }
    ($map.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "`n"
}

function Write-RandomFile {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][long]$Length, [int]$Seed = 1)
    $bytes = [byte[]]::new($Length)
    [System.Random]::new($Seed).NextBytes($bytes)
    [System.IO.File]::WriteAllBytes($Path, $bytes)
}

function Set-DamagedRange {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Damages a fixture inside the run''s own temporary folder.')]
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][long]$Offset, [Parameter(Mandatory)][int]$Count)
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite)
    try {
        $stream.Position = $Offset
        $stream.Write([byte[]]::new($Count), 0, $Count)
    }
    finally { $stream.Dispose() }
}

# Fixtures are made on first use and shared by the steps of one run.
function Get-Fixture {
    param([Parameter(Mandatory)][ValidateSet('Tree', 'Big', 'Many', 'ManyZip', 'BigZipWithSet')][string]$Name)
    if ($Smoke.Fixtures.ContainsKey($Name)) { return $Smoke.Fixtures[$Name] }

    $root = Get-StepFolder 'fixtures'
    switch ($Name) {
        'Tree' {
            # A folder with a Cyrillic name, a nested folder, an empty file and a compressible one.
            $cyrillic = -join [char[]](0x41F, 0x430, 0x43F, 0x43A, 0x430)
            $value = Join-Path $root $cyrillic
            $null = [System.IO.Directory]::CreateDirectory((Join-Path $value 'sub'))
            Write-RandomFile -Path (Join-Path $value 'random.bin') -Length 300KB -Seed 2
            [System.IO.File]::WriteAllText((Join-Path $value 'text.txt'), ('pakko smoke ' * 4000))
            [System.IO.File]::WriteAllBytes((Join-Path $value 'sub\empty.dat'), [byte[]]::new(0))
            [System.IO.File]::WriteAllText((Join-Path $value "sub\$cyrillic.txt"), $cyrillic)
        }
        'Big' {
            $value = Join-Path $root 'big.bin'
            Write-RandomFile -Path $value -Length 200MB -Seed 5
        }
        'Many' {
            $value = Join-Path $root 'many'
            $null = [System.IO.Directory]::CreateDirectory($value)
            # 6000 files of 16 KiB that do not compress: enough work for a progress window.
            $bytes = [byte[]]::new(16KB)
            [System.Random]::new(3).NextBytes($bytes)
            foreach ($i in 0..5999) {
                [System.BitConverter]::GetBytes($i).CopyTo($bytes, 0)
                [System.IO.File]::WriteAllBytes((Join-Path $value ('f{0:d4}.bin' -f $i)), $bytes)
            }
        }
        'ManyZip' {
            $value = Join-Path $root 'many-cli.zip'
            $made = Invoke-Pakko @('a', $value, (Get-Fixture Many))
            Assert-Smoke ($made.ExitCode -eq 0) "fixture many.zip: exit $($made.ExitCode) $($made.Text)"
        }
        'BigZipWithSet' {
            $value = Join-Path $root 'bigset\big.zip'
            $null = [System.IO.Directory]::CreateDirectory((Split-Path -Parent $value))
            $made = Invoke-Pakko @('a', '-rr5', '-mx=0', '-tzip', $value, (Get-Fixture Big))
            Assert-Smoke ($made.ExitCode -eq 0) "fixture big.zip with a set: exit $($made.ExitCode) $($made.Text)"
        }
    }

    $Smoke.Fixtures[$Name] = $value
    $value
}

# Top-level windows of the Shell and of its operation window, with the text of everything in them.
function Get-ShellWindowText {
    $ids = @(Get-Process -Name 'Archiver.OperationUi', 'Archiver.Shell' -ErrorAction SilentlyContinue | ForEach-Object Id)
    if (-not $ids) { return @() }
    $all = [System.Windows.Automation.Condition]::TrueCondition
    foreach ($window in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $all)) {
        if ($ids -notcontains $window.Current.ProcessId) { continue }
        $names = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $all) |
            ForEach-Object { $_.Current.Name } | Where-Object { $_ } | Select-Object -Unique
        [pscustomobject]@{ Title = $window.Current.Name; Text = ($names -join ' | '); Element = $window }
    }
}

function Stop-ShellProcess {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Closes the windows this run opened.')]
    param()
    Get-Process -Name 'Archiver.OperationUi', 'Archiver.Shell' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

# Starts Archiver.Shell.exe with the arguments; -StandardInput is written as UTF-16, then closed.
function Start-ShellProcess {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Starts the process a smoke step asked for.')]
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [AllowNull()][string]$StandardInput
    )
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($Smoke.Shell)
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $PSBoundParameters.ContainsKey('StandardInput')
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($startInfo.RedirectStandardInput) {
        $bytes = [System.Text.Encoding]::Unicode.GetBytes($StandardInput)
        $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
        $process.StandardInput.Close()
    }
    $process
}

# Starts Archiver.Shell.exe the way Explorer does and waits until it exits, or until the windows'
# text matches -Until (a result window stays open until someone closes it). -Watch is a pattern
# to look for while waiting, without stopping. Texts are in the user's language: match numbers,
# names and hashes, not words.
function Invoke-Shell {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [string]$Until,
        [string]$Watch,
        [int]$TimeoutSeconds = 60,
        [string]$StandardInput
    )
    Stop-ShellProcess
    $start = @{ Arguments = $Arguments }
    if ($PSBoundParameters.ContainsKey('StandardInput')) { $start.StandardInput = $StandardInput }
    # Not $watch: variable names ignore case, and -Watch is a parameter here.
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-ShellProcess @start
    try {
        $text = ''
        $firstWindow = $null
        $matched = $false
        $watched = $false
        while ($clock.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
            $seen = (Get-ShellWindowText | ForEach-Object { "[$($_.Title)] $($_.Text)" }) -join ' || '
            if ($seen) {
                $text = $seen
                $firstWindow ??= $clock.Elapsed.TotalSeconds
                $watched = $watched -or ($Watch -and $text -match $Watch)
                $matched = $Until -and $text -match $Until
            }
            if ($matched -or $process.HasExited) { break }
            Start-Sleep -Milliseconds 250
        }

        [pscustomobject]@{
            Text         = $text
            Matched      = $matched
            Watched      = $watched
            Exited       = $process.HasExited
            ExitCode     = $process.HasExited ? $process.ExitCode : $null
            FirstWindow  = $firstWindow
            Seconds      = $clock.Elapsed.TotalSeconds
        }
    }
    finally {
        Stop-ShellProcess
        $process.Dispose()
    }
}
