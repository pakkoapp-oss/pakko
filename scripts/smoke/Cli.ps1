#Requires -Version 7.0
# Steps of docs/SMOKE.md's CLI rows. Dot-sourced by scripts/Invoke-Smoke.ps1.

Add-SmokeStep 'CLI-001' {
    $project = Join-Path $Smoke.Repo 'tests\Archiver.CLI.Tests'
    $before = $env:PAKKO_CLI_EXE
    $env:PAKKO_CLI_EXE = $Smoke.Pakko
    try {
        $run = Invoke-Tool -Exe 'dotnet' -Arguments @('test', $project, '--filter', 'FullyQualifiedName~Subprocess', '--nologo')
    }
    finally { $env:PAKKO_CLI_EXE = $before }

    $summary = [regex]::Match($run.Text, 'Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+)')
    Assert-Smoke $summary.Success "dotnet test printed no summary: $($run.Text[-400..-1] -join '')"
    Assert-Smoke ($run.ExitCode -eq 0 -and $summary.Groups[1].Value -eq '0') "subprocess tests failed: $($summary.Value)"
    "passed $($summary.Groups[2].Value), skipped $($summary.Groups[3].Value)"
}

Add-SmokeStep 'CLI-002' {
    foreach ($flag in '-v', '--version') {
        $version = Invoke-Pakko @($flag)
        Assert-Smoke ($version.ExitCode -eq 0) "$flag exit $($version.ExitCode)"
        Assert-Smoke ($version.Output -match '^pakko \d+\.\d+\.\d+') "$flag printed: $($version.Output)"
        if (-not $PakkoExe) {
            Assert-Smoke ($version.Output.Contains("(package $($Smoke.Package.PackageFullName))")) "$flag does not name the installed package: $($version.Output)"
        }
    }
    foreach ($flag in '-h', '--help') {
        $help = Invoke-Pakko @($flag)
        Assert-Smoke ($help.ExitCode -eq 0) "$flag exit $($help.ExitCode)"
        foreach ($command in 'a', 'x', 't', 'l', 'i', 'h', 'r') {
            Assert-Smoke ($help.Output -match "(?m)^\s+$command\s{2,}\S") "$flag does not list the command '$command'"
        }
    }
}

Add-SmokeStep 'CLI-003' {
    $folder = Get-StepFolder 'cli003'
    $zip = Join-Path $folder 'a.zip'
    $made = Invoke-Pakko @('a', $zip, (Get-Fixture Tree))
    Assert-Smoke ($made.ExitCode -eq 0) "a: exit $($made.ExitCode) $($made.Text)"
    $hash = Get-Sha256 $zip

    $refused = @(
        @('u', $zip, (Get-Fixture Tree)), @('d', $zip, 'text.txt'), @('rn', $zip, 'a', 'b'), @('b'), @('e', $zip),
        @('l', $zip, '-r'), @('l', $zip, '-i!*.txt'), @('l', $zip, '-x!*.txt'), @('l', $zip, '-ssc')
    )
    foreach ($arguments in $refused) {
        $run = Invoke-Pakko $arguments -WorkingDirectory $folder
        Assert-Smoke ($run.ExitCode -eq 7) "pakko $($arguments[0]) $($arguments[-1]): exit $($run.ExitCode), expected 7"
        Assert-Smoke ($run.Text -match 'not supported') "pakko $($arguments[0]) $($arguments[-1]) does not say 'not supported': $($run.Text)"
    }

    Assert-Smoke ((Get-Sha256 $zip) -eq $hash) 'a refused command changed the archive'
    Assert-Smoke (@(Get-ChildItem -LiteralPath $folder).Count -eq 1) "a refused command wrote into the folder: $((Get-ChildItem -LiteralPath $folder -Name) -join ', ')"
}

Add-SmokeStep 'CLI-004' {
    $folder = Get-StepFolder 'cli004'
    $tree = Get-Fixture Tree
    $name = Split-Path -Leaf $tree
    $expected = Get-TreeHash $tree
    $password = 'Smoke-' + [System.IO.Path]::GetRandomFileName()

    $cases = @(
        @{ File = 'store.zip'; Create = @('-mx=0'); Password = $null }
        @{ File = 'best.zip'; Create = @('-mx=9'); Password = $null }
        @{ File = 'tree.tar.gz'; Create = @('-ttar.gz'); Password = $null }
        @{ File = 'aes.zip'; Create = @("-p$password", '-mem=AES256'); Password = $password }
    )
    foreach ($case in $cases) {
        $archive = Join-Path $folder $case.File
        $secret = $case.Password ? @("-p$($case.Password)") : @()

        $made = Invoke-Pakko (@('a', $archive) + $case.Create + @($tree))
        Assert-Smoke ($made.ExitCode -eq 0) "a $($case.File): exit $($made.ExitCode) $($made.Text)"

        $listed = Invoke-Pakko @('l', '-sccUTF-8', $archive)
        Assert-Smoke ($listed.ExitCode -eq 0 -and $listed.Output.Contains("$name/sub/$name.txt")) "l $($case.File) does not show the Cyrillic name: $($listed.Text)"
        if ($case.Password) {
            Assert-Smoke ($listed.Output -match '\tf\tAES-256\t') "l $($case.File) does not mark the entries as AES-256"
        }

        if ($case.File -like '*.zip') {
            $tested = Invoke-Pakko (@('t', $archive) + $secret)
            Assert-Smoke ($tested.ExitCode -eq 0) "t $($case.File): exit $($tested.ExitCode) $($tested.Text)"
        }

        $out = Join-Path $folder ($case.File + '.out')
        $extracted = Invoke-Pakko (@('x', $archive, "-o$out") + $secret)
        Assert-Smoke ($extracted.ExitCode -eq 0) "x $($case.File): exit $($extracted.ExitCode) $($extracted.Text)"
        Assert-Smoke ((Get-TreeHash (Join-Path $out $name)) -eq $expected) "x $($case.File): the extracted files differ from the sources"

        if (Test-Path -LiteralPath $Smoke.SevenZip) {
            $reference = Invoke-Tool -Exe $Smoke.SevenZip -Arguments (@('t', $archive) + $secret)
            Assert-Smoke ($reference.ExitCode -eq 0) "7za t $($case.File): exit $($reference.ExitCode)"
        }
    }

    $methods = Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('l', '-slt', "-p$password", (Join-Path $folder 'aes.zip'))
    Assert-Smoke ($methods.Output -match 'Method = AES-256') '7za does not report AES-256 for aes.zip'
}

Add-SmokeStep 'CLI-005' {
    $folder = Get-StepFolder 'cli005'

    $missing = Invoke-Pakko @('t', (Join-Path $folder 'none.zip'))
    Assert-Smoke ($missing.ExitCode -eq 2) "a missing archive: exit $($missing.ExitCode), expected 2"

    $source = Join-Path $folder 'payload.bin'
    Write-RandomFile -Path $source -Length 400KB -Seed 7

    $locked = Join-Path $folder 'aes.zip'
    $null = Invoke-Pakko @('a', $locked, '-pRight-1', '-mem=AES256', $source)
    $wrong = Invoke-Pakko @('t', $locked, '-pWrong-1')
    Assert-Smoke ($wrong.ExitCode -eq 2) "a wrong password: exit $($wrong.ExitCode), expected 2"
    Assert-Smoke (@($wrong.Text -split '\r?\n' | Where-Object { $_.Trim() }).Count -le 2) "a wrong password takes more than two lines: $($wrong.Text)"

    $damaged = Join-Path $folder 'damaged.zip'
    $null = Invoke-Pakko @('a', '-mx=0', $damaged, $source)
    Set-DamagedRange -Path $damaged -Offset 100KB -Count 16
    $tested = Invoke-Pakko @('t', $damaged)
    Assert-Smoke ($tested.ExitCode -eq 2) "t on a damaged ZIP: exit $($tested.ExitCode), expected 2"
    Assert-Smoke ($tested.Text -match "payload\.bin.*CRC-32.*expected [0-9A-F]{8}, got [0-9A-F]{8}") "t does not name the entry and both CRC values: $($tested.Text)"

    $out = Join-Path $folder 'out'
    $extracted = Invoke-Pakko @('x', $damaged, "-o$out")
    Assert-Smoke ($extracted.ExitCode -eq 2) "x on a damaged ZIP: exit $($extracted.ExitCode), expected 2"
    Assert-Smoke (-not (Test-Path -LiteralPath (Join-Path $out 'payload.bin'))) 'x left the damaged entry on disk'
}
