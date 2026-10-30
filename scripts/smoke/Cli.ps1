#Requires -Version 7.0
# Steps of docs/SMOKE.md's CLI rows. Dot-sourced by scripts/Invoke-Smoke.ps1.

Add-SmokeStep 'CLI-001' {
    $project = Join-Path $Smoke.Repo 'tests\Archiver.CLI.Tests'
    $before = $env:PAKKO_CLI_EXE
    $env:PAKKO_CLI_EXE = $Smoke.Pakko
    try {
        $run = Invoke-Tool -Exe 'dotnet' -Arguments @('test', $project, '--filter', 'FullyQualifiedName~Subprocess', '--nologo') -WorkingDirectory $Smoke.Repo
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
        # T-F410: x, l, t and (wave C) a filter names; a still refuses what picks archives, -stx
        # and -xtd, and h refuses them all.
        @('a', (Join-Path $folder 'b.zip'), (Get-Fixture Tree), '-ai!*.zip'), @('a', (Join-Path $folder 'b.zip'), (Get-Fixture Tree), '-xtd'),
        @('h', $zip, '-ssc'), @('h', $zip, '-stxzip')
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

    if (Test-Path -LiteralPath $Smoke.SevenZip) {
        $methods = Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('l', '-slt', "-p$password", (Join-Path $folder 'aes.zip'))
        Assert-Smoke ($methods.Output -match 'Method = AES-256') '7za does not report AES-256 for aes.zip'
    }
}

Add-SmokeStep 'CLI-005' {
    $folder = Get-StepFolder 'cli005'

    $missing = Invoke-Pakko @('t', (Join-Path $folder 'none.zip'))
    Assert-Smoke ($missing.ExitCode -eq 2) "a missing archive: exit $($missing.ExitCode), expected 2"

    $source = Join-Path $folder 'payload.bin'
    Write-RandomFile -Path $source -Length 400KB -Seed 7

    $locked = Join-Path $folder 'aes.zip'
    $made = Invoke-Pakko @('a', $locked, '-pRight-1', '-mem=AES256', $source)
    Assert-Smoke ($made.ExitCode -eq 0) "a aes.zip: exit $($made.ExitCode) $($made.Text)"
    $wrong = Invoke-Pakko @('t', $locked, '-pWrong-1')
    Assert-Smoke ($wrong.ExitCode -eq 2) "a wrong password: exit $($wrong.ExitCode), expected 2"
    Assert-Smoke (@($wrong.Text -split '\r?\n' | Where-Object { $_.Trim() }).Count -le 2) "a wrong password takes more than two lines: $($wrong.Text)"

    $damaged = Join-Path $folder 'damaged.zip'
    $made = Invoke-Pakko @('a', '-mx=0', $damaged, $source)
    Assert-Smoke ($made.ExitCode -eq 0) "a damaged.zip: exit $($made.ExitCode) $($made.Text)"
    Set-DamagedRange -Path $damaged -Offset 100KB -Count 16
    $tested = Invoke-Pakko @('t', $damaged)
    Assert-Smoke ($tested.ExitCode -eq 2) "t on a damaged ZIP: exit $($tested.ExitCode), expected 2"
    Assert-Smoke ($tested.Text -match "payload\.bin.*CRC-32.*expected [0-9A-F]{8}, got [0-9A-F]{8}") "t does not name the entry and both CRC values: $($tested.Text)"

    $out = Join-Path $folder 'out'
    $extracted = Invoke-Pakko @('x', $damaged, "-o$out")
    Assert-Smoke ($extracted.ExitCode -eq 2) "x on a damaged ZIP: exit $($extracted.ExitCode), expected 2"
    Assert-Smoke (-not (Test-Path -LiteralPath (Join-Path $out 'payload.bin'))) 'x left the damaged entry on disk'
}

Add-SmokeStep 'CLI-022' {
    $folder = Get-StepFolder 'cli022'
    $archive = Join-Path $folder 'tree.tar.gz'
    $made = Invoke-Pakko @('a', '-ttar.gz', $archive, (Get-Fixture Tree))
    Assert-Smoke ($made.ExitCode -eq 0) "a -ttar.gz: exit $($made.ExitCode) $($made.Text)"

    $icacls = Join-Path $env:SystemRoot 'System32\icacls.exe'
    $out = Join-Path $folder 'shared'
    New-Item -ItemType Directory -Path $out | Out-Null
    $granted = Invoke-Tool -Exe $icacls -Arguments @($out, '/grant', '*S-1-5-11:(OI)(CI)(RX)')
    Assert-Smoke ($granted.ExitCode -eq 0) "icacls /grant: exit $($granted.ExitCode) $($granted.Text)"

    $run = Invoke-Pakko @('x', $archive, "-o$out")
    Assert-Smoke ($run.ExitCode -eq 0) "x: exit $($run.ExitCode) $($run.Text)"
    $files = @(Get-ChildItem -LiteralPath $out -Recurse -File)
    Assert-Smoke ($files.Count -eq 4) "x wrote $($files.Count) files, expected 4"

    # icacls /save writes the SDDL, the integrity label included; names in its plain output are localized.
    $saved = Join-Path $folder 'acl.txt'
    foreach ($file in $files) {
        $save = Invoke-Tool -Exe $icacls -Arguments @($file.FullName, '/save', $saved)
        Assert-Smoke ($save.ExitCode -eq 0) "icacls /save $($file.Name): exit $($save.ExitCode) $($save.Text)"
        $sddl = Get-Content -LiteralPath $saved -Raw -Encoding Unicode
        Assert-Smoke ($sddl -notmatch 'S-1-15-2-') "$($file.Name) keeps the sandbox's entry: $sddl"
        Assert-Smoke ($sddl -notmatch '\(ML;') "$($file.Name) keeps an integrity label: $sddl"
        Assert-Smoke ($sddl -match ';ID;[^)]*;;;AU\)') "$($file.Name) does not inherit from its folder: $sddl"
    }
}

Add-SmokeStep 'CLI-024' {
    $folder = Get-StepFolder 'cli024'
    $present = Join-Path $folder 'present.txt'
    Set-Content -LiteralPath $present -Value 'present'
    $missing = Join-Path $folder 'missing.txt'

    # T-F414: the missing name is a warning (exit 1) and the rest is archived, as in 7-Zip.
    $out = Join-Path $folder 'out.tar.gz'
    $run = Invoke-Pakko @('a', '-ttar.gz', $out, $present, $missing)
    Assert-Smoke ($run.ExitCode -eq 1) "a with a missing name: exit $($run.ExitCode), expected 1: $($run.Text)"
    Assert-Smoke ($run.Text.Contains('pakko: warning:') -and $run.Text.Contains('missing.txt')) "the warning does not name the missing source: $($run.Text)"
    Assert-Smoke ((Invoke-Pakko @('l', '-ba', $out)).Output.Contains('present.txt')) "the archive does not hold present.txt"
    Assert-Smoke ($run.Text -notmatch "Couldn't visit directory|tar\.exe:") "tar.exe's own line is shown: $($run.Text)"
}

Add-SmokeStep 'CLI-025' {
    $folder = Get-StepFolder 'cli025'
    $tree = Join-Path $folder 'tree'
    $outside = Join-Path $folder 'outside'
    New-Item -ItemType Directory -Path (Join-Path $tree 'sub'), $outside | Out-Null
    Set-Content -LiteralPath (Join-Path $tree 'inside.txt') -Value 'inside'
    Set-Content -LiteralPath (Join-Path $outside 'secret.txt') -Value 'outside'
    $link = Join-Path $tree 'sub\link'
    New-Item -ItemType Junction -Path $link -Target $outside | Out-Null

    try {
        $archive = Join-Path $folder 'tree.tar'
        $made = Invoke-Pakko @('a', '-ttar', $archive, $tree)
        Assert-Smoke ($made.ExitCode -eq 1) "a: exit $($made.ExitCode), expected 1 (a skip): $($made.Text)"
        Assert-Smoke ($made.Text -match 'skipped' -and $made.Text.Contains($link)) "a does not name the junction as skipped: $($made.Text)"

        $listed = Invoke-Pakko @('l', $archive)
        Assert-Smoke ($listed.ExitCode -eq 0) "l: exit $($listed.ExitCode) $($listed.Text)"
        Assert-Smoke ($listed.Output -match 'tree/inside\.txt') "the listing lacks tree/inside.txt: $($listed.Output)"
        Assert-Smoke ($listed.Output -notmatch 'secret\.txt|tree/sub/link') "the junction or its target is in the archive: $($listed.Output)"

        $out = Join-Path $folder 'out'
        $extracted = Invoke-Pakko @('x', $archive, "-o$out")
        Assert-Smoke ($extracted.ExitCode -eq 0) "x of Pakko's own archive: exit $($extracted.ExitCode) $($extracted.Text)"
    }
    finally {
        # The link first: a recursive delete of the step folder must not reach its target through it.
        [System.IO.Directory]::Delete($link, $false)
    }
}

# T-F342: the methods 7-Zip names in "7za l -slt" for the entries of a 7z archive (empty files have none).
function Get-SevenZipMethod {
    param([Parameter(Mandatory)][string]$Archive)
    $listed = Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('l', '-slt', $Archive)
    Assert-Smoke ($listed.ExitCode -eq 0) "7za l -slt $(Split-Path -Leaf $Archive): exit $($listed.ExitCode)"
    @($listed.Output -split '\r?\n' | Where-Object { $_ -match '^Method = \S' } | ForEach-Object { ($_ -replace '^Method = ', '') -replace ':.*$', '' } | Sort-Object -Unique)
}

function Test-SevenZipSignature {
    param([Parameter(Mandatory)][string]$Archive)
    $head = [byte[]]::new(6)
    $stream = [System.IO.File]::OpenRead($Archive)
    try { $null = $stream.Read($head, 0, 6) } finally { $stream.Dispose() }
    ($head -join ',') -eq '55,122,188,175,39,28'
}

Add-SmokeStep 'CLI-026' {
    $folder = Get-StepFolder 'cli026'
    if (-not (Test-Path -LiteralPath $Smoke.SevenZip)) { Skip-Smoke 'the vendored 7za.exe is not there' }
    $tree = Get-Fixture Tree
    $name = Split-Path -Leaf $tree
    $expected = Get-TreeHash $tree

    $cases = @(
        @{ File = 'store.7z'; Create = @('-mx=0'); Method = 'Copy' }
        @{ File = 'default.7z'; Create = @(); Method = 'LZMA2' }
        @{ File = 'best.7z'; Create = @('-mx=9'); Method = 'LZMA2' }
    )
    foreach ($case in $cases) {
        $archive = Join-Path $folder $case.File
        $made = Invoke-Pakko (@('a', '-t7z', $archive) + $case.Create + @($tree))
        Assert-Smoke ($made.ExitCode -eq 0) "a -t7z $($case.File): exit $($made.ExitCode) $($made.Text)"
        Assert-Smoke (Test-SevenZipSignature $archive) "$($case.File) does not begin with the 7z signature"

        $reference = Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('t', $archive)
        Assert-Smoke ($reference.ExitCode -eq 0) "7za t $($case.File): exit $($reference.ExitCode)"
        $methods = Get-SevenZipMethod $archive
        Assert-Smoke (($methods -join ',') -eq $case.Method) "$($case.File): 7za names the method(s) '$($methods -join ',')', expected $($case.Method)"

        $listed = Invoke-Pakko @('l', '-sccUTF-8', $archive)
        Assert-Smoke ($listed.ExitCode -eq 0 -and $listed.Output.Contains("$name/sub/$name.txt")) "l $($case.File) does not show the Cyrillic name: $($listed.Text)"

        $out = Join-Path $folder ($case.File + '.out')
        $extracted = Invoke-Pakko @('x', $archive, "-o$out")
        Assert-Smoke ($extracted.ExitCode -eq 0) "x $($case.File): exit $($extracted.ExitCode) $($extracted.Text)"
        Assert-Smoke ((Get-TreeHash (Join-Path $out $name)) -eq $expected) "x $($case.File): the extracted files differ from the sources"
    }
    Assert-Smoke ((Get-Item -LiteralPath (Join-Path $folder 'default.7z')).Length -lt (Get-Item -LiteralPath (Join-Path $folder 'store.7z')).Length) 'the default archive is not smaller than the stored one'
}

Add-SmokeStep 'CLI-027' {
    $folder = Get-StepFolder 'cli027'
    if (-not (Test-Path -LiteralPath $Smoke.SevenZip)) { Skip-Smoke 'the vendored 7za.exe is not there' }
    $tree = Get-Fixture Tree

    $cases = @(
        @{ Switches = @('-m0=lzma'); Method = 'LZMA' }
        @{ Switches = @('-m0=BZip2'); Method = 'BZip2' }
        @{ Switches = @('-m0=deflate'); Method = 'Deflate' }
        @{ Switches = @('-m0=copy'); Method = 'Copy' }
        @{ Switches = @('-m0=bzip2', '-mx=0'); Method = 'Copy' }
    )
    $index = 0
    foreach ($case in $cases) {
        $archive = Join-Path $folder "m$((++$index)).7z"
        $made = Invoke-Pakko (@('a', '-t7z', $archive) + $case.Switches + @($tree))
        Assert-Smoke ($made.ExitCode -eq 0) "a -t7z $($case.Switches -join ' '): exit $($made.ExitCode) $($made.Text)"
        $methods = Get-SevenZipMethod $archive
        Assert-Smoke (($methods -join ',') -eq $case.Method) "$($case.Switches -join ' '): 7za names '$($methods -join ',')', expected $($case.Method)"
        $reference = Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('t', $archive)
        Assert-Smoke ($reference.ExitCode -eq 0) "7za t after $($case.Switches -join ' '): exit $($reference.ExitCode)"
    }

    foreach ($method in 'ppmd', 'zstd') {
        $archive = Join-Path $folder "$method.7z"
        $refused = Invoke-Pakko @('a', '-t7z', "-m0=$method", $archive, $tree)
        Assert-Smoke ($refused.ExitCode -eq 7) "-m0=$method`: exit $($refused.ExitCode), expected 7: $($refused.Text)"
        Assert-Smoke (-not (Test-Path -LiteralPath $archive)) "-m0=$method left a file"
    }
}

Add-SmokeStep 'CLI-028' {
    $folder = Get-StepFolder 'cli028'
    $tree = Get-Fixture Tree

    $cases = @(
        @{ Name = 'locked.7z'; Run = @('-t7z', '-pSmoke-1'); Says = 'encrypt' }
        @{ Name = 'plain.7z'; Run = @(); Says = '-t7z' }
        @{ Name = 'zip.7z'; Run = @('-tzip'); Says = '-tzip' }
        @{ Name = 'seven.zip'; Run = @('-t7z'); Says = '-t7z' }
        @{ Name = 'method.zip'; Run = @('-m0=lzma2'); Says = '-t7z' }
    )
    foreach ($case in $cases) {
        $archive = Join-Path $folder $case.Name
        $refused = Invoke-Pakko (@('a') + $case.Run + @($archive, $tree))
        Assert-Smoke ($refused.ExitCode -eq 7) "a $($case.Run -join ' ') $($case.Name): exit $($refused.ExitCode), expected 7: $($refused.Text)"
        Assert-Smoke ($refused.Text.Contains($case.Says)) "a $($case.Run -join ' ') $($case.Name) does not say '$($case.Says)': $($refused.Text)"
    }
    $left = @(Get-ChildItem -LiteralPath $folder -Force -Name)
    Assert-Smoke ($left.Count -eq 0) "the refused commands left: $($left -join ', ')"
}

Add-SmokeStep 'CLI-029' {
    $info = Invoke-Pakko @('i')
    Assert-Smoke ($info.ExitCode -eq 0) "i: exit $($info.ExitCode) $($info.Text)"
    $line = @($info.Output -split '\r?\n' | Where-Object { $_ -match '^\s+7z\s' })
    Assert-Smoke ($line.Count -eq 1) "i has $($line.Count) lines for 7z: $($info.Output)"
    Assert-Smoke ($line[0] -match 'create, extract, list' -and $line[0] -match '\(supported\)') "the 7z line of i: $($line[0])"
    "7z: $($line[0].Trim())"
}

# T-F397: 'pakko t' on a 7z. tar.exe reads every entry in the sandbox and writes nothing; a 7z
# carries checksums it compares, so a whole one passes with no warning and a changed byte fails.
Add-SmokeStep 'CLI-033' {
    $folder = Get-StepFolder 'cli033'
    $tree = Get-Fixture Tree
    foreach ($method in @('lzma2', 'copy')) {
        $archive = Join-Path $folder "$method.7z"
        $made = Invoke-Pakko @('a', '-t7z', "-m0=$method", $archive, $tree)
        Assert-Smoke ($made.ExitCode -eq 0) "a -t7z -m0=$method`: exit $($made.ExitCode) $($made.Text)"

        $whole = Invoke-Pakko @('t', $archive)
        Assert-Smoke ($whole.ExitCode -eq 0 -and $whole.Error.Trim().Length -eq 0) "t on the whole $method.7z: exit $($whole.ExitCode) $($whole.Text)"

        $flipped = Join-Path $folder "$method-flipped.7z"
        Copy-Item -LiteralPath $archive -Destination $flipped
        Set-DamagedRange -Path $flipped -Offset ([long]((Get-Item -LiteralPath $flipped).Length / 2)) -Count 4
        $damaged = Invoke-Pakko @('t', $flipped)
        Assert-Smoke ($damaged.ExitCode -eq 2 -and $damaged.Error.Contains("$method-flipped.7z")) "t on the damaged $method.7z: exit $($damaged.ExitCode) $($damaged.Text)"

        $cut = Join-Path $folder "$method-cut.7z"
        $bytes = [System.IO.File]::ReadAllBytes($archive)
        [System.IO.File]::WriteAllBytes($cut, $bytes[0..([int]($bytes.Length * 0.6))])
        $truncated = Invoke-Pakko @('t', $cut)
        Assert-Smoke ($truncated.ExitCode -eq 2) "t on the truncated $method.7z: exit $($truncated.ExitCode) $($truncated.Text)"
    }
    Assert-Smoke (@(Get-ChildItem -LiteralPath $folder -Force).Count -eq 6) "t left something behind: $((Get-ChildItem -LiteralPath $folder -Force -Name) -join ', ')"
}

# T-F406: a folder that cannot be made (a file holds its name) is an error with exit code 2, for
# extraction and for a new archive, never the runtime's unhandled-exception crash; the file stays.
Add-SmokeStep 'CLI-036' {
    $folder = Get-StepFolder 'cli036'
    $tree = Get-Fixture Tree
    $blocker = Join-Path $folder 'blocker'
    [System.IO.File]::WriteAllText($blocker, 'keep me')
    $zip = Join-Path $folder 'made.zip'
    $tarGz = Join-Path $folder 'made.tar.gz'
    foreach ($made in @((Invoke-Pakko @('a', $zip, $tree)), (Invoke-Pakko @('a', '-ttar.gz', $tarGz, $tree)))) {
        Assert-Smoke ($made.ExitCode -eq 0) "making the archives: exit $($made.ExitCode) $($made.Text)"
    }

    foreach ($archive in @($zip, $tarGz)) {
        $into = Invoke-Pakko @('x', "-o$blocker", $archive)
        Assert-Smoke ($into.ExitCode -eq 2 -and $into.Error.Contains('pakko: error:') -and -not $into.Error.Contains('Unhandled exception')) "x -o<file> on $(Split-Path -Leaf $archive): exit $($into.ExitCode) $($into.Text)"
    }
    $create = Invoke-Pakko @('a', (Join-Path $blocker 'new.zip'), $tree)
    Assert-Smoke ($create.ExitCode -eq 2 -and -not $create.Error.Contains('Unhandled exception')) "a into a folder that is a file: exit $($create.ExitCode) $($create.Text)"
    Assert-Smoke ([System.IO.File]::ReadAllText($blocker) -eq 'keep me') 'the file holding the folder name changed'
}

# T-F407, T-F408, T-F409, T-F411: the command lines patool and Chocolatey give 7z.exe (docs/CLI.md,
# "Real callers"), on the installed pakko.
Add-SmokeStep 'CLI-037' {
    $folder = Get-StepFolder 'cli037'
    $tree = Get-Fixture Tree
    $zip = Join-Path $folder 'made.zip'
    $made = Invoke-Pakko @('a', $zip, $tree)
    Assert-Smoke ($made.ExitCode -eq 0) "making the ZIP: exit $($made.ExitCode) $($made.Text)"

    $patool = Join-Path $folder 'patool'
    foreach ($line in @(@('x', '-y', '-p-', '-aou', "-o$patool", '--', $zip), @('l', '-y', '-p-', '--', $zip), @('t', '-y', '-p-', '--', $zip))) {
        $run = Invoke-Pakko $line
        Assert-Smoke ($run.ExitCode -eq 0) "patool's $($line[0]): exit $($run.ExitCode) $($run.Text)"
    }
    $sevenZip = Join-Path $folder 'patool.7z'
    $run = Invoke-Pakko @('a', '-y', '-t7z', '-sae', '--', $sevenZip, $tree)
    Assert-Smoke ($run.ExitCode -eq 0 -and (Test-SevenZipSignature $sevenZip)) "patool's a -t7z: exit $($run.ExitCode) $($run.Text)"
    $exact = Join-Path $folder 'no-extension'
    $run = Invoke-Pakko @('a', '-y', '-tzip', '-sae', '--', $exact, $tree)
    Assert-Smoke ($run.ExitCode -eq 0 -and (Test-Path -LiteralPath $exact -PathType Leaf)) "a -sae: exit $($run.ExitCode), the archive is not named exactly as typed"

    $choco = Join-Path $folder 'choco'
    # -sccUTF-8 only so this harness, which reads UTF-8, compares a non-ASCII name exactly. Without it
    # pakko writes the console's code page (T-F238) and 7-Zip the OEM one (docs/CLI.md, Real callers).
    $run = Invoke-Pakko @('x', '-aoa', '-bd', '-bb1', "-o$choco", '-y', '-sccUTF-8', $zip)
    Assert-Smoke ($run.ExitCode -eq 0) "Chocolatey's x: exit $($run.ExitCode) $($run.Text)"
    $named = @($run.Output -split '\r?\n' | Where-Object { $_.StartsWith('- ') } | ForEach-Object { $_.Substring(2) })
    foreach ($name in $named) {
        Assert-Smoke (Test-Path -LiteralPath (Join-Path $choco $name)) "Chocolatey's x names '$name', which is not in the folder"
    }
    foreach ($file in Get-ChildItem -LiteralPath $choco -Recurse -File) {
        $relative = [System.IO.Path]::GetRelativePath($choco, $file.FullName)
        Assert-Smoke ($named -contains $relative) "Chocolatey's x wrote '$relative' without a '- ' line"
    }

    $list = Invoke-Pakko @('l', '-ba', $zip)
    Assert-Smoke ($list.ExitCode -eq 0 -and -not $list.Output.StartsWith('Size')) "l -ba: exit $($list.ExitCode), the header is still there"
    $quiet = Invoke-Pakko @('l', '-bso0', $zip)
    Assert-Smoke ($quiet.ExitCode -eq 0 -and $quiet.Output.Length -eq 0) "l -bso0: exit $($quiet.ExitCode), stdout: $($quiet.Output)"
    $timed = Invoke-Pakko @('t', '-bt', $zip)
    Assert-Smoke ($timed.ExitCode -eq 0 -and $timed.Output.Contains('Global  Time =')) "t -bt: exit $($timed.ExitCode), no time lines"
    $refused = Invoke-Pakko @('x', '-so', '-bse1', $zip)
    Assert-Smoke ($refused.ExitCode -eq 7) "x -so -bse1: exit $($refused.ExitCode), expected 7"
    # T-F411 wave D: -saa is taken now (CLI-040 covers it); two -sa switches are exit 7, as in 7-Zip.
    $refused = Invoke-Pakko @('a', '-sae', '-sas', (Join-Path $folder 'twice'), $tree)
    Assert-Smoke ($refused.ExitCode -eq 7 -and -not (Test-Path -LiteralPath (Join-Path $folder 'twice'))) "a -sae -sas: exit $($refused.ExitCode), expected 7"
}

Add-SmokeStep 'CLI-038' {
    $folder = Get-StepFolder 'cli038'
    $source = Join-Path $folder 'src'
    foreach ($name in @('a.txt', 'b.nsis', 'dir\c.txt', 'dir\sub\d.nsis', 'dir\sub\e.txt', 'Other\f.txt', 'Other\dir\x.nsis')) {
        $path = Join-Path $source $name
        $null = New-Item -ItemType Directory -Force -Path (Split-Path $path)
        Set-Content -LiteralPath $path -Value $name -NoNewline
    }
    # An empty folder is the one folder 'a' writes an entry for (T-F66): -xtd and -xtf see it.
    $null = New-Item -ItemType Directory -Path (Join-Path $source 'empty')
    $zip = Join-Path $folder 'made.zip'
    $made = Invoke-Pakko (@('a', $zip) + @('a.txt', 'b.nsis', 'dir', 'Other', 'empty' | ForEach-Object { Join-Path $source $_ }))
    Assert-Smoke ($made.ExitCode -eq 0) "making the ZIP: exit $($made.ExitCode) $($made.Text)"

    function Get-Relative([string]$Root) {
        @(Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object { [System.IO.Path]::GetRelativePath($Root, $_.FullName) })
    }

    $scoop = Join-Path $folder 'scoop'
    $run = Invoke-Pakko @('x', $zip, "-o$scoop", '-xr!*.nsis', '-y')
    $files = @(Get-Relative $scoop)
    Assert-Smoke ($run.ExitCode -eq 0 -and $files -contains 'dir\c.txt' -and -not ($files -like '*.nsis')) "Scoop's -xr!*.nsis: exit $($run.ExitCode), files: $files"
    $scoopIn = Join-Path $folder 'scoop-in'
    $run = Invoke-Pakko @('x', $zip, "-o$scoopIn", '-ir!dir\*', '-y')
    $files = @(Get-Relative $scoopIn)
    Assert-Smoke ($run.ExitCode -eq 0 -and $files.Count -eq 3 -and -not ($files -notlike 'dir\*')) "Scoop's -ir!dir\*: exit $($run.ExitCode), files: $files"

    $choco = Join-Path $folder 'choco'
    $run = Invoke-Pakko @('x', '-aoa', '-bd', '-bb1', "-o$choco", '-y', $zip, 'dir')
    $files = @(Get-Relative $choco)
    Assert-Smoke ($run.ExitCode -eq 0 -and $files.Count -eq 3 -and -not ($files -notlike 'dir\*')) "Chocolatey's x with a folder: exit $($run.ExitCode), files: $files"
    foreach ($name in @($run.Output -split '\r?\n' | Where-Object { $_.StartsWith('- ') } | ForEach-Object { $_.Substring(2) })) {
        Assert-Smoke (Test-Path -LiteralPath (Join-Path $choco $name)) "Chocolatey's x names '$name', which is not in the folder"
    }

    $list = Join-Path $folder 'names.txt'
    Set-Content -LiteralPath $list -Value "`"a.txt`"`r`n" -NoNewline -Encoding utf8
    foreach ($form in @("@$list", "-i@$list")) {
        $out = Join-Path $folder ('list' + $form.Length)
        $run = Invoke-Pakko @('x', $zip, "-o$out", $form)
        $files = @(Get-Relative $out)
        Assert-Smoke ($run.ExitCode -eq 0 -and $files.Count -eq 1 -and $files[0] -eq 'a.txt') "x $form`: exit $($run.ExitCode), files: $files"
    }

    $none = Join-Path $folder 'none'
    $run = Invoke-Pakko @('x', $zip, "-o$none", 'nothing.zzz')
    Assert-Smoke ($run.ExitCode -eq 0 -and $run.Output.Contains('No files to process') -and -not (Test-Path -LiteralPath $none)) "x nothing.zzz: exit $($run.ExitCode), $($run.Text)"

    $arcs = Join-Path $folder 'arcs'
    $null = New-Item -ItemType Directory -Path $arcs
    Copy-Item -LiteralPath $zip -Destination (Join-Path $arcs 'one.zip')
    Copy-Item -LiteralPath $zip -Destination (Join-Path $arcs 'two.zip')
    $run = Invoke-Pakko @('l', '-ba', (Join-Path $arcs '*.zip'), 'a.txt')
    Assert-Smoke ($run.ExitCode -eq 0 -and @($run.Output -split '\r?\n' | Where-Object { $_ }).Count -eq 2) "l <folder>\*.zip: exit $($run.ExitCode), $($run.Text)"
    $run = Invoke-Pakko @('l', '-ba', '-an', "-ai!$arcs\*.zip", "-ax!$arcs\two.zip", 'a.txt')
    Assert-Smoke ($run.ExitCode -eq 0 -and @($run.Output -split '\r?\n' | Where-Object { $_ }).Count -eq 1) "l -an -ai! -ax!: exit $($run.ExitCode), $($run.Text)"

    $rows = { param($a) @((Invoke-Pakko (@('l', '-ba') + $a + @($zip))).Output -split '\r?\n' | Where-Object { $_ }) }
    $recursive = & $rows @('-r', '-i!*.txt')
    Assert-Smoke ($recursive.Count -eq 4) "l -r -i!*.txt: $($recursive.Count) rows, expected 4"
    Assert-Smoke ((& $rows @('-spd', '-i!*.txt')).Count -eq 0) "l -spd -i!*.txt lists something"
    Assert-Smoke ((& $rows @('-ssc', '-i!A.TXT')).Count -eq 0) "l -ssc -i!A.TXT lists something"
    $utf16 = Join-Path $folder 'utf16.txt'
    [System.IO.File]::WriteAllText($utf16, "b.nsis`r`n", [System.Text.Encoding]::Unicode)
    Assert-Smoke ((& $rows @('-scsUTF-16LE', "-i@$utf16")).Count -eq 1) "l -scsUTF-16LE -i@list: not one row"
    Assert-Smoke (-not ((& $rows @('-xtd')) -match '\tD\t|/$')) "l -xtd lists a folder"
    Assert-Smoke ((& $rows @('-xtf')).Count -ge 1) "l -xtf lists no folder"

    # T-F410 wave B: t filters too, and -stx leaves out a type.
    $tested = Invoke-Pakko @('t', $zip, '-x!*.nsis')
    Assert-Smoke ($tested.ExitCode -eq 0) "t -x!*.nsis: exit $($tested.ExitCode) $($tested.Text)"
    $none = Invoke-Pakko @('t', $zip, 'nothing.zzz')
    Assert-Smoke ($none.ExitCode -eq 0 -and $none.Output.Contains('No files to process')) "t nothing.zzz: exit $($none.ExitCode) $($none.Text)"
    $excluded = Invoke-Pakko @('t', $zip, '-stxzip')
    Assert-Smoke ($excluded.ExitCode -eq 2 -and $excluded.Text.Contains('Cannot open the file as archive')) "t -stxzip: exit $($excluded.ExitCode) $($excluded.Text)"
    $unknown = Invoke-Pakko @('t', $zip, '-stxgz')
    Assert-Smoke ($unknown.ExitCode -eq 2 -and $unknown.Text.Contains('Unsupported archive type')) "t -stxgz: exit $($unknown.ExitCode) $($unknown.Text)"
}

Add-SmokeStep 'CLI-039' {
    # T-F414 and T-F410 wave C: what 'a' archives and under which name, as 7za 26.02 measured.
    $folder = Get-StepFolder 'cli039'
    $tree = Join-Path $folder 'tree'
    foreach ($name in @('t.txt', 'dir\c.txt', 'dir\c.log', 'dir\sub\s.txt')) {
        $path = Join-Path $tree $name
        $null = New-Item -ItemType Directory -Force -Path (Split-Path $path)
        Set-Content -LiteralPath $path -Value $name -NoNewline
    }
    function Get-ZipName([string]$Zip) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($Zip)
        try { @($archive.Entries | ForEach-Object { $_.FullName } | Sort-Object) } finally { $archive.Dispose() }
    }
    $cases = @(
        @{ Args = @('dir\sub'); Names = @('dir/sub/s.txt') },
        @{ Args = @('.'); Names = @('dir/c.log', 'dir/c.txt', 'dir/sub/s.txt', 't.txt') },
        @{ Args = @('dir', '-xr!*.log'); Names = @('dir/c.txt', 'dir/sub/s.txt') },
        @{ Args = @('*.txt', '-r'); Names = @('dir/c.txt', 'dir/sub/s.txt', 't.txt') },
        @{ Args = @('dir', '*.txt', '-r'); Names = @('dir/c.log', 'dir/c.txt', 'dir/sub/s.txt', 't.txt') },
        @{ Args = @('t.txt', '-i!dir\*.log'); Names = @('dir/c.log', 't.txt') }
    )
    $i = 0
    foreach ($case in $cases) {
        $zip = Join-Path $folder "case$i.zip"
        $i++
        $run = Invoke-Pakko -Arguments (@('a', $zip) + $case.Args) -WorkingDirectory $tree
        $names = if (Test-Path -LiteralPath $zip) { Get-ZipName $zip } else { @() }
        Assert-Smoke ($run.ExitCode -eq 0 -and (($names -join '|') -eq ($case.Names -join '|'))) "a $($case.Args -join ' '): exit $($run.ExitCode), entries: $($names -join ', ') $($run.Text)"
    }

    $zip = Join-Path $folder 'missing.zip'
    $run = Invoke-Pakko -Arguments @('a', $zip, 'missing.txt', 't.txt') -WorkingDirectory $tree
    Assert-Smoke ($run.ExitCode -eq 1 -and $run.Text.Contains('pakko: warning: missing.txt:') -and ((Get-ZipName $zip) -join '|') -eq 't.txt') "a missing.txt t.txt: exit $($run.ExitCode) $($run.Text)"
    $zip = Join-Path $folder 'nothing.zip'
    $run = Invoke-Pakko -Arguments @('a', $zip, 'missing.txt') -WorkingDirectory $tree
    Assert-Smoke ($run.ExitCode -eq 2 -and $run.Text.Contains('nothing to archive') -and -not (Test-Path -LiteralPath $zip)) "a missing.txt: exit $($run.ExitCode) $($run.Text)"

    $tar = Join-Path $folder 'made.tar'
    $run = Invoke-Pakko -Arguments @('a', '-ttar', $tar, 'dir\sub', 'dir', '-xr!*.log') -WorkingDirectory $tree
    $listed = Invoke-Pakko @('l', '-ba', $tar)
    Assert-Smoke ($run.ExitCode -eq 0 -and $listed.Output.Contains('c.txt') -and -not $listed.Output.Contains('c.log')) "a -ttar dir -xr!*.log: exit $($run.ExitCode) $($run.Text) $($listed.Output)"
}

Add-SmokeStep 'CLI-040' {
    # T-F411 wave D: 7-Zip's spellings, as 7za 26.02 measured.
    $folder = Get-StepFolder 'cli040'
    $zip = Join-Path $folder 'in.zip'
    $file = Join-Path $folder 'a.txt'
    Set-Content -LiteralPath $file -Value 'a' -NoNewline
    $made = Invoke-Pakko @('A', '-TZIP', '-MX9', $zip, $file)
    Assert-Smoke ($made.ExitCode -eq 0 -and (Test-Path -LiteralPath $zip)) "A -TZIP -MX9: exit $($made.ExitCode) $($made.Text)"

    $out = Join-Path $folder 'out'
    $run = Invoke-Pakko @('-Y', 'X', $zip, "-O$out")
    Assert-Smoke ($run.ExitCode -eq 0 -and (Test-Path -LiteralPath (Join-Path $out 'a.txt'))) "-Y X -O: exit $($run.ExitCode) $($run.Text)"

    $here = Join-Path $folder 'here'
    $null = New-Item -ItemType Directory -Path $here
    $run = Invoke-Pakko -Arguments @('-y', '--', 'x', $zip) -WorkingDirectory $here
    Assert-Smoke ($run.ExitCode -eq 0 -and (Test-Path -LiteralPath (Join-Path $here 'a.txt'))) "-y -- x: exit $($run.ExitCode) $($run.Text)"

    $run = Invoke-Pakko -Arguments @('a', '-ttar', '-saa', 'n.tar', $file) -WorkingDirectory $folder
    Assert-Smoke ($run.ExitCode -eq 0 -and (Test-Path -LiteralPath (Join-Path $folder 'n.tar.tar'))) "a -saa n.tar: exit $($run.ExitCode) $($run.Text)"

    $first = Join-Path $folder 'first'
    $second = Join-Path $folder 'second'
    $run = Invoke-Pakko @('x', $zip, "-o$first", "-o$second")
    Assert-Smoke ($run.ExitCode -eq 7 -and $run.Text.Contains('Multiple instances for switch') -and -not (Test-Path -LiteralPath $first) -and -not (Test-Path -LiteralPath $second)) "x -o -o: exit $($run.ExitCode) $($run.Text)"
    $run = Invoke-Pakko @('x', $zip, "-o$out", '-AOA')
    Assert-Smoke ($run.ExitCode -eq 7) "x -AOA: exit $($run.ExitCode), expected 7"
}

Add-SmokeStep 'CLI-041' {
    # T-F411 wave E: 7-Zip's switches that Core carries out, as 7za 26.02 measured.
    $folder = Get-StepFolder 'cli041'
    $src = Join-Path $folder 'src'
    $null = New-Item -ItemType Directory -Path (Join-Path $src 'd')
    Set-Content -LiteralPath (Join-Path $src 'a.txt') -Value 'new' -NoNewline
    Set-Content -LiteralPath (Join-Path $src 'd\b.txt') -Value 'b' -NoNewline
    $stamp = [datetime]::new(2020, 1, 1, 10, 0, 0, [DateTimeKind]::Utc)
    foreach ($item in @((Join-Path $src 'a.txt'), (Join-Path $src 'd\b.txt'), (Join-Path $src 'd'), $src)) {
        (Get-Item -LiteralPath $item).LastWriteTimeUtc = $stamp
    }

    $zip = Join-Path $folder 'src.zip'
    $run = Invoke-Pakko -Arguments @('a', '-stl', '-mmt=1', '-ssw', "-w$folder", $zip, 'src') -WorkingDirectory $folder
    Assert-Smoke ($run.ExitCode -eq 0 -and (Get-Item -LiteralPath $zip).LastWriteTimeUtc -eq $stamp) "a -stl -mmt=1 -ssw -w: exit $($run.ExitCode) $($run.Text)"

    $o1 = Join-Path $folder 'o1\src'
    $run = Invoke-Pakko @('x', $zip, "-o$o1", '-spe')
    Assert-Smoke ($run.ExitCode -eq 0 -and (Test-Path -LiteralPath (Join-Path $o1 'a.txt')) -and -not (Test-Path -LiteralPath (Join-Path $o1 'src'))) "x -spe: exit $($run.ExitCode) $($run.Text)"

    $o2 = Join-Path $folder 'o2'
    $null = New-Item -ItemType Directory -Path (Join-Path $o2 'src')
    Set-Content -LiteralPath (Join-Path $o2 'src\a.txt') -Value 'old' -NoNewline
    $run = Invoke-Pakko @('x', $zip, "-o$o2", '-aot')
    $new = Get-Content -LiteralPath (Join-Path $o2 'src\a.txt') -Raw
    $old = Get-Content -LiteralPath (Join-Path $o2 'src\a (1).txt') -Raw -ErrorAction SilentlyContinue
    Assert-Smoke ($run.ExitCode -eq 0 -and $new -eq 'new' -and $old -eq 'old') "x -aot: exit $($run.ExitCode), a.txt '$new', a (1).txt '$old' $($run.Text)"

    $copy = Join-Path $folder 'copy'
    Copy-Item -LiteralPath $src -Destination $copy -Recurse
    $run = Invoke-Pakko @('a', '-sdel', (Join-Path $folder 'copy.zip'), $copy)
    Assert-Smoke ($run.ExitCode -eq 0 -and -not (Test-Path -LiteralPath $copy) -and (Test-Path -LiteralPath (Join-Path $folder 'copy.zip'))) "a -sdel: exit $($run.ExitCode) $($run.Text)"

    $stopped = Join-Path $folder 'stopped.zip'
    $lock = [System.IO.File]::Open((Join-Path $src 'a.txt'), 'Open', 'ReadWrite', 'None')
    try { $run = Invoke-Pakko @('a', '-sse', $stopped, $src) } finally { $lock.Dispose() }
    Assert-Smoke ($run.ExitCode -eq 2 -and $run.Text.Contains('-sse stops at a file') -and -not (Test-Path -LiteralPath $stopped)) "a -sse with a locked file: exit $($run.ExitCode) $($run.Text)"

    $run = Invoke-Pakko @('a', '-ssp', (Join-Path $folder 'ssp.zip'), $src)
    Assert-Smoke ($run.ExitCode -eq 7) "a -ssp: exit $($run.ExitCode), expected 7"
}
