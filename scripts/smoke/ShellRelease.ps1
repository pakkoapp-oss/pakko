#Requires -Version 7.0
# Steps of docs/SMOKE.md's SHL and OPW rows of the release tier. Dot-sourced by
# scripts/Invoke-Smoke.ps1. The Shell's texts are in the user's language: the steps look at file
# names, hashes, numbers and the kinds of control in a window.

Add-SmokeStep 'SHL-008' {
    $folder = Get-StepFolder 'shl008'
    $tree = Get-Fixture Tree

    # The antivirus test string, in two halves so that this file does not hold it; in the ZIP it
    # is compressed, and the plain file never exists.
    $testString = 'X5O!P%@AP[4\PZX54(P^)7CC)7}$' + 'EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*'
    $bad = Join-Path $folder 'bad.zip'
    New-ZipFile -Path $bad -Entries @(@{ Name = 'clean.txt'; Content = 'clean' }, @{ Name = 'eicar.txt'; Content = $testString })
    if (-not (Test-Path -LiteralPath $bad)) { Skip-Smoke 'the antivirus removed the ZIP with its test file before Pakko could scan it' }

    $run = Invoke-Shell -Arguments @('--scan', $bad) -Until 'bad\.zip/eicar\.txt' -TimeoutSeconds 60
    Assert-Smoke $run.Matched "--scan did not name the entry with the test string: $($run.Text)"
    Assert-Smoke ($run.Text -notmatch 'bad\.zip/clean\.txt') "--scan names the clean entry as well: $($run.Text)"

    # The two clean archives must get one and the same verdict, which names no entry.
    $verdicts = foreach ($case in @{ File = 'good.zip'; Switch = @() }, @{ File = 'good.tar.gz'; Switch = @('-ttar.gz') }) {
        $archive = Join-Path $folder $case.File
        $made = Invoke-Pakko (@('a') + $case.Switch + @($archive, $tree))
        Assert-Smoke ($made.ExitCode -eq 0) "a $($case.File): exit $($made.ExitCode) $($made.Text)"
        $process = Start-ShellProcess -Arguments @('--scan', $archive)
        try {
            $text = (Wait-ShellResult).Text
            Assert-Smoke ($text -notmatch 'random\.bin|text\.txt|/') "--scan of the clean $($case.File) names an entry: $text"
            $text.Replace($case.File, '*')
        }
        finally {
            Stop-ShellProcess
            $process.Dispose()
        }
    }
    Assert-Smoke ($verdicts[0] -eq $verdicts[1]) "the clean ZIP and the clean tar.gz got different verdicts: $($verdicts -join ' <> ')"
    Assert-Smoke ($run.Text.Replace('bad.zip', '*') -ne $verdicts[0]) 'the ZIP with the test string got the verdict of a clean one'
}

Add-SmokeStep 'SHL-009' {
    $folder = Get-StepFolder 'shl009'
    $archive = Join-Path $folder 'locked.zip'
    $made = Invoke-Pakko @('a', $archive, '-pRight-1', '-mem=AES256', (Join-Path (Get-Fixture Tree) 'text.txt'))
    Assert-Smoke ($made.ExitCode -eq 0) "a: exit $($made.ExitCode) $($made.Text)"

    $prompt = { Get-ShellPasswordPrompt }

    Stop-ShellProcess
    $process = Start-ShellProcess -Arguments @('--test', $archive)
    try {
        Wait-Smoke { & $prompt } 'the password prompt'
        $first = & $prompt
        Assert-Smoke ($first -match 'locked\.zip') "the password prompt does not name the archive: $first"

        Send-ShellPassword 'Wrong-1'
        Wait-Smoke { $now = & $prompt; $now -and $now -ne $first } 'a second prompt that says more than the first'

        Send-ShellPassword 'Right-1'
        $result = (Wait-ShellResult).Text
        Assert-Smoke ($result -match 'locked\.zip' -and $result -notmatch '\b[0-9A-F]{8}\b') "after the right password the test does not end clean: $result"
    }
    finally {
        Stop-ShellProcess
        $process.Dispose()
    }
}

Add-SmokeStep 'SHL-010' {
    $folder = Get-StepFolder 'shl010'
    $zip = Join-Path $folder 'a.zip'
    $made = Invoke-Pakko @('a', $zip, (Get-Fixture Tree))
    Assert-Smoke ($made.ExitCode -eq 0) "a: exit $($made.ExitCode) $($made.Text)"

    foreach ($arguments in @('--extract-here'), @('--frobnicate', $zip)) {
        $run = Invoke-Shell -Arguments $arguments -TimeoutSeconds 15
        Assert-Smoke ($run.Exited -and $run.ExitCode -ne 0 -and -not $run.Text) "Archiver.Shell $($arguments[0]): exited=$($run.Exited) code=$($run.ExitCode) $($run.Text)"
    }
    Assert-Smoke (@(Get-ChildItem -LiteralPath $folder).Count -eq 1) 'a refused Archiver.Shell call wrote into the folder'

    $missing = Join-Path $folder 'none.zip'
    $process = Start-ShellProcess -Arguments @('--extract-here', $missing)
    try {
        $text = (Wait-ShellResult -TimeoutSeconds 20).Text
        Assert-Smoke ($text -match 'none\.zip: ') "--extract-here on a missing archive does not say which: $text"
    }
    finally {
        Stop-ShellProcess
        $process.Dispose()
    }
    Assert-Smoke (@(Get-ChildItem -LiteralPath $folder).Count -eq 1) '--extract-here on a missing archive wrote into the folder'
}

Add-SmokeStep 'SHL-011' {
    $folder = Get-StepFolder 'shl011'
    $hostile = Join-Path $folder 'hostile'
    $closed = Join-Path $hostile 'closed'
    $null = [System.IO.Directory]::CreateDirectory($closed)
    [System.IO.File]::WriteAllText((Join-Path $hostile 'open.txt'), 'open')
    [System.IO.File]::WriteAllText((Join-Path $closed 'secret.txt'), 'closed')
    $null = New-Item -ItemType Junction -Path (Join-Path $hostile 'loop') -Target $hostile
    $icacls = Join-Path $env:SystemRoot 'System32\icacls.exe'
    $user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $null = Invoke-Tool -Exe $icacls -Arguments @($closed, '/deny', "${user}:(OI)(CI)(RX)")
    $process = Start-ShellProcess -Arguments @('--hash', '--algorithm', 'sha256', $hostile)
    try {
        $text = (Wait-ShellResult).Text
        # One file was read: its hash is the sum of the data.
        Assert-Smoke ($text -match "(?i)\b$(Get-Sha256 (Join-Path $hostile 'open.txt'))\b") "--hash does not show the hash of the readable file: $text"
        Assert-Smoke ($text -match 'hostile\\closed: ' -and $text -match 'hostile\\loop: ') "--hash does not name what it skipped: $text"
        Assert-Smoke ($text -notmatch 'loop\\') "--hash went through the junction: $text"
    }
    finally {
        Stop-ShellProcess
        $process.Dispose()
        $null = Invoke-Tool -Exe $icacls -Arguments @($closed, '/remove:d', $user)
        # The junction first: removing the tree through it would start on the tree itself.
        [System.IO.Directory]::Delete((Join-Path $hostile 'loop'))
    }
}

Add-SmokeStep 'OPW-004' {
    $many = Get-Fixture Many
    $big = Get-Fixture Big
    $zip = Join-Path (Split-Path -Parent $many) ((Split-Path -Leaf (Split-Path -Parent $many)) + '.zip')
    Remove-Item -LiteralPath $zip -ErrorAction SilentlyContinue
    $percent = '\d+\s?%'

    Stop-ShellProcess
    $process = Start-ShellProcess -Arguments @('--archive', $many, $big)
    try {
        Wait-Smoke { (Get-ShellWindowText).Text -match $percent } 'a percentage in the operation window'
        Get-Process -Name 'Archiver.OperationUi' | Stop-Process -Force

        # The Shell's own progress dialog is a Win32 dialog box.
        Wait-Smoke { Get-ShellWindowText | Where-Object { $_.Element.Current.ClassName -eq '#32770' -and $_.Text -match $percent } } 'the Win32 progress dialog with a percentage' 20
        Assert-Smoke ($process.WaitForExit(120000)) 'the operation did not finish within two minutes of the window being killed'
        Assert-Smoke ($process.ExitCode -eq 0) "Archiver.Shell after its window was killed: exit $($process.ExitCode)"

        $listed = Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('l', $zip)
        Assert-Smoke ($listed.Output -match '(?m)\s6001 files') "7za does not count 6001 files: $(($listed.Output -split '\r?\n')[-2])"
    }
    finally {
        Stop-ShellProcess
        $process.Dispose()
        Remove-Item -LiteralPath $zip -ErrorAction SilentlyContinue
    }
}
