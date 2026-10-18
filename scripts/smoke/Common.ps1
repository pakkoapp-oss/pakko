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
    foreach ($argument in $Arguments) {
        # T-F411: the switch name is read without case (-PSecret), the password as typed.
        if ($argument -imatch '^-p(.+)$') { $null = $Smoke.Passwords.Add($Matches[1]) }
    }
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

# A ZIP written by .NET, entry by entry: @{ Name; Content; Modified }. A name that ends in "/" is
# a folder entry.
function New-ZipFile {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Writes a fixture inside the run''s own temporary folder.')]
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][hashtable[]]$Entries)
    $archive = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $Entries) {
            $item = $archive.CreateEntry($entry.Name)
            if ($entry.Modified) { $item.LastWriteTime = [System.DateTimeOffset]::new([datetime]$entry.Modified) }
            if ($entry.Name.EndsWith('/')) { continue }
            $writer = [System.IO.StreamWriter]::new($item.Open())
            try { $writer.Write([string]$entry.Content) }
            finally { $writer.Dispose() }
        }
    }
    finally { $archive.Dispose() }
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

# The first element under a Shell window with this control type or class name, or nothing.
function Find-ShellElement {
    param(
        [Parameter(Mandatory)][System.Windows.Automation.AutomationElement]$Window,
        [System.Windows.Automation.ControlType]$ControlType,
        [string]$ClassName,
        [switch]$All
    )
    $condition = $ControlType ?
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ControlType) :
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ClassNameProperty, $ClassName)
    if ($All) { return @($Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) }
    $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

# The Shell's result window: one that shows no progress bar and asks for no password, with the
# same text on two looks in a row (a window is up before its content is).
function Wait-ShellResult {
    param([int]$TimeoutSeconds = 60)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $before = $null
    while ((Get-Date) -lt $deadline) {
        $window = Get-ShellWindowText | Where-Object {
            -not (Find-ShellElement -Window $_.Element -ControlType ([System.Windows.Automation.ControlType]::ProgressBar)) -and
            -not (Find-ShellElement -Window $_.Element -ClassName 'PasswordBox')
        } | Select-Object -First 1
        if ($window -and $window.Text -eq $before) { return $window }
        $before = $window ? $window.Text : $null
        Start-Sleep -Milliseconds 300
    }
    throw "Archiver.Shell showed no result window in $TimeoutSeconds s"
}

# The text of the Shell's password prompt (the window with a password box), or nothing.
function Get-ShellPasswordPrompt {
    (Get-ShellWindowText | Where-Object { Find-ShellElement -Window $_.Element -ClassName 'PasswordBox' } | Select-Object -First 1).Text
}

# Types into the prompt's password box and presses its first button.
function Send-ShellPassword {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingPlainTextForPassword', '',
        Justification = 'A made-up password for an archive the run itself created.')]
    param([Parameter(Mandatory)][string]$Password)
    $null = $Smoke.Passwords.Add($Password)
    $window = Get-ShellWindowText | Where-Object { Find-ShellElement -Window $_.Element -ClassName 'PasswordBox' } | Select-Object -First 1
    $box = Find-ShellElement -Window $window.Element -ClassName 'PasswordBox'
    $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Password)
    $button = @(Find-ShellElement -Window $window.Element -ClassName 'Button' -All)[0]
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
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

# --- the elevated half (scripts/smoke/elevated/PolicyHost.ps1) --------------------------------
# One elevation prompt per run, at the first step that needs it. A declined prompt skips every
# such step with that reason.

function Export-PolicyKey {
    param([Parameter(Mandatory)][string]$Name)
    $reg = Join-Path $env:SystemRoot 'System32\reg.exe'
    $keys = @{ pakko = 'HKLM\Software\Policies\Pakko'; fips = 'HKLM\SYSTEM\CurrentControlSet\Control\Lsa\FipsAlgorithmPolicy' }
    foreach ($key in $keys.Keys) {
        $file = Join-Path $Smoke.Elevated.Folder "$Name-$key.reg"
        $run = Invoke-Tool -Exe $reg -Arguments @('export', $keys[$key], $file, '/y')
        # A key that does not exist is a state too.
        if ($run.ExitCode -ne 0) { Set-Content -LiteralPath $file -Value 'absent' -Encoding utf8 }
    }
}

function Start-SmokeElevated {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'The elevation prompt is the confirmation.')]
    param()
    if ($Smoke.Elevated.Declined) { Skip-Smoke $Smoke.Elevated.Declined }
    if ($Smoke.Elevated.Process) { return }

    $folder = Get-StepFolder 'elevated'
    $Smoke.Elevated = @{ Folder = $folder; Next = 1 }
    Export-PolicyKey 'before'
    $hostScript = Join-Path $PSScriptRoot 'elevated\PolicyHost.ps1'
    $arguments = @('-NoProfile', '-File', "`"$hostScript`"", '-Folder', "`"$folder`"", '-ParentId', $PID)
    try {
        $process = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru
    }
    catch {
        $Smoke.Elevated.Declined = "the elevation prompt was not accepted: $($_.Exception.Message)"
        Skip-Smoke $Smoke.Elevated.Declined
    }

    $ready = Join-Path $folder 'ready.json'
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path -LiteralPath $ready) -and -not $process.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    Assert-Smoke (Test-Path -LiteralPath $ready) "the elevated helper did not start; see $folder\host.log"
    $Smoke.Elevated.Process = $process
}

function Send-ElevatedRequest {
    param([Parameter(Mandatory)][hashtable]$Request, [int]$TimeoutSeconds = 30)
    Start-SmokeElevated
    $number = $Smoke.Elevated.Next++
    $path = Join-Path $Smoke.Elevated.Folder "request-$number.json"
    Set-Content -LiteralPath "$path.tmp" -Value ($Request | ConvertTo-Json -Depth 5) -Encoding utf8
    Move-Item -LiteralPath "$path.tmp" -Destination $path

    $replyPath = Join-Path $Smoke.Elevated.Folder "reply-$number.json"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-Path -LiteralPath $replyPath) -and -not $Smoke.Elevated.Process.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 150 }
    Assert-Smoke (Test-Path -LiteralPath $replyPath) "the elevated helper did not answer request $number ($($Request.Kind))"
    $reply = Get-Content -LiteralPath $replyPath -Raw -Encoding utf8 | ConvertFrom-Json
    Assert-Smoke $reply.Ok "the elevated helper refused request $number ($($Request.Kind)): $($reply.Error)"
    $reply
}

# Asks the helper to stop; it puts the registry back first. Safe to call when it never started.
function Stop-SmokeElevated {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Ends the helper this run started.')]
    param()
    $process = $Smoke.Elevated.Process
    if (-not $process) { return $false }
    if (-not $process.HasExited) {
        $null = Send-ElevatedRequest @{ Kind = 'stop' }
        $null = $process.WaitForExit(15000)
    }
    $Smoke.Elevated.Process = $null
    Export-PolicyKey 'after'
    $true
}

# With -Hold: waits in the state a step made, until a file named "go" appears in the run's folder.
function Wait-SmokeHold {
    param([Parameter(Mandatory)][string]$What)
    if (-not $Smoke.Hold) { return }
    $go = Join-Path $Smoke.Work 'go'
    Write-Host "`n  $What. Create the file $go to go on." -NoNewline
    while (-not (Test-Path -LiteralPath $go)) { Start-Sleep -Milliseconds 500 }
    Remove-Item -LiteralPath $go
}

# Runs -Body with the machine policy set to -Values (and the FIPS flag to -Fips), then puts the
# registry back. Everything Pakko reads the policy with starts after the values are written:
# every pakko and Archiver.Shell process reads it once, at its start.
function Use-SmokePolicy {
    param([hashtable]$Values = @{}, [Nullable[int]]$Fips, [Parameter(Mandatory)][scriptblock]$Body)
    $null = Send-ElevatedRequest @{ Kind = 'policy'; Values = $Values; Fips = $Fips }
    try {
        & $Body
        Wait-SmokeHold "policy held: $(($Values.Keys | ForEach-Object { "$_=$($Values[$_] -join ',')" }) -join '; ') fips=$Fips"
    }
    finally { $null = Send-ElevatedRequest @{ Kind = 'policy'; Values = @{} } }
}
