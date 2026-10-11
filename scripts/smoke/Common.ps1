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
    param([Parameter(Mandatory)][string]$Exe, [string[]]$Arguments = @(), [string]$WorkingDirectory)
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($Exe)
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
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
        $output = $process.StandardOutput.ReadToEnd()
        $process.WaitForExit()
        $watch.Stop()
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
    param([Parameter(Mandatory)][string[]]$Arguments, [string]$WorkingDirectory)
    Invoke-Tool -Exe $Smoke.Pakko -Arguments $Arguments -WorkingDirectory $WorkingDirectory
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
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($Smoke.Shell)
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
    $startInfo.UseShellExecute = $false
    if ($PSBoundParameters.ContainsKey('StandardInput')) { $startInfo.RedirectStandardInput = $true }
    # Not $watch: variable names ignore case, and -Watch is a parameter here.
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        if ($PSBoundParameters.ContainsKey('StandardInput')) {
            $bytes = [System.Text.Encoding]::Unicode.GetBytes($StandardInput)
            $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
            $process.StandardInput.Close()
        }

        $text = ''
        $firstWindow = $null
        $matched = $false
        $watched = $false
        while ($clock.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
            $windows = @(Get-ShellWindowText)
            if ($windows) {
                if ($null -eq $firstWindow) { $firstWindow = $clock.Elapsed.TotalSeconds }
                $text = ($windows | ForEach-Object { "[$($_.Title)] $($_.Text)" }) -join ' || '
                if ($Watch -and $text -match $Watch) { $watched = $true }
                if ($Until -and $text -match $Until) { $matched = $true; break }
            }
            if ($process.HasExited) { break }
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
