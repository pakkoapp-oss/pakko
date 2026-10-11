#Requires -Version 7.0
# Steps of docs/SMOKE.md's APP rows that a script can run. Dot-sourced by scripts/Invoke-Smoke.ps1.

Add-SmokeStep 'APP-018' -AtEnd {
    $folder = Get-StepFolder 'app018'

    # A password of this run only, typed into the Shell's prompt: the Shell hands the work to its
    # window process, and neither command line may carry it.
    $secret = 'Pw-' + [guid]::NewGuid().ToString('N')
    $archive = Join-Path $folder 'locked.zip'
    $made = Invoke-Pakko @('a', $archive, "-p$secret", '-mem=AES256', (Join-Path (Get-Fixture Tree) 'text.txt'))
    Assert-Smoke ($made.ExitCode -eq 0) "a: exit $($made.ExitCode) $($made.Text)"
    Stop-ShellProcess
    $process = Start-ShellProcess -Arguments @('--test', $archive)
    try {
        Wait-Smoke { Get-ShellPasswordPrompt } 'the password prompt'
        Send-ShellPassword $secret
        $null = Wait-ShellResult
        $commandLines = @(Get-CimInstance -ClassName Win32_Process -Filter "Name LIKE 'Archiver.%'" | ForEach-Object CommandLine | Where-Object { $_ })
        Assert-Smoke ($commandLines.Count -ge 2) "only $($commandLines.Count) command lines of Archiver processes could be read, so the next check would prove little"
        Assert-Smoke (-not ($commandLines | Where-Object { $_.Contains($secret) })) 'the password typed into the prompt is on a command line of an Archiver process'
    }
    finally {
        Stop-ShellProcess
        $process.Dispose()
    }

    # The App's own rows (APP-002, APP-007) are run by hand with these two.
    $passwords = @($Smoke.Passwords) + 'Right-1', 'Wrong-1' | Select-Object -Unique

    # The App's log, as the package sees it and as an unpackaged build would.
    $logFolders = @(
        Join-Path $env:LOCALAPPDATA "Packages\$($Smoke.Package.PackageFamilyName)\LocalCache\Local\Pakko\logs"
        Join-Path $env:LOCALAPPDATA 'Pakko\logs'
    )
    $logs = @($logFolders | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { Get-ChildItem -LiteralPath $_ -File -Filter 'pakko*' })
    $lines = 0
    $fresh = 0
    foreach ($log in $logs) {
        foreach ($line in [System.IO.File]::ReadLines($log.FullName)) {
            $lines++
            $stamp = [datetime]::MinValue
            if ($line.Length -ge 19 -and [datetime]::TryParse($line.Substring(0, 19), [ref]$stamp) -and $stamp -ge $Smoke.Started) { $fresh++ }
            foreach ($password in $passwords) {
                Assert-Smoke (-not $line.Contains($password)) "$($log.Name) holds a password of the smoke run in the line of $($line.Substring(0, [System.Math]::Min(19, $line.Length)))"
            }
        }
    }

    $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $Smoke.Started } -ErrorAction SilentlyContinue)
    foreach ($entry in $events) {
        foreach ($password in $passwords) {
            Assert-Smoke (-not ([string]$entry.Message).Contains($password)) "the Application event log holds a password of the smoke run: $($entry.ProviderName) at $($entry.TimeCreated)"
        }
    }

    "$($passwords.Count) passwords; $($commandLines.Count) command lines; pakko.log: $lines lines, $fresh since the run began; $($events.Count) events"
}
