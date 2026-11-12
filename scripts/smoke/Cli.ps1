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

Add-SmokeStep 'CLI-044' {
    # T-F441/T-F424: tar.exe lists a name outside the user's ANSI page by best fit, so an archive with
    # an accented name and its plain twin lists one line twice. Built from char codes, never typed
    # glyphs (.claude/rules/text-encoding.md): e-acute collapses on 1251 and 932, a-macron on 1252.
    $folder = Get-StepFolder 'cli044'
    $tarExe = Get-TarExePakko $folder
    $candidates = @(@{ Accent = [char]0xE9; Plain = 'e' }, @{ Accent = [char]0x101; Plain = 'a' })
    $chosen = $null
    foreach ($candidate in $candidates) {
        $accented = "caf$($candidate.Accent).txt"
        $plain = "caf$($candidate.Plain).txt"
        $tar = Join-Path $folder "pair-$([int]$candidate.Accent).tar"
        New-TarFile -Path $tar -Entries @(@{ Name = $accented; Content = 'ACCENT' }, @{ Name = $plain; Content = 'PLAIN' })
        $listed = Invoke-Pakko -Exe $tarExe @('l', $tar, '-ba')
        if ($listed.ExitCode -eq 0 -and ([regex]::Matches($listed.Output, [regex]::Escape($plain))).Count -eq 2) {
            $chosen = @{ Tar = $tar; Accented = $accented; Plain = $plain }
            break
        }
    }
    if (-not $chosen) { Skip-Smoke 'this machine''s code page carries both candidate names' }

    $o1 = Join-Path $folder 'o1'
    $run = Invoke-Pakko -Exe $tarExe @('x', $chosen.Tar, "-o$o1", $chosen.Plain)
    $written = @(Get-ChildItem -LiteralPath $o1 -Recurse -File -ErrorAction SilentlyContinue)
    Assert-Smoke ($run.ExitCode -ne 0 -and $run.Text.Contains('tar.exe') -and -not $run.Text.Contains('ZIP') -and $written.Count -eq 0) "x <the collapsed name>: exit $($run.ExitCode), $($written.Count) files $($run.Text)"

    $o2 = Join-Path $folder 'o2'
    $run = Invoke-Pakko -Exe $tarExe @('x', $chosen.Tar, "-o$o2", "-x!$($chosen.Plain)")
    $written = @(Get-ChildItem -LiteralPath $o2 -Recurse -File -ErrorAction SilentlyContinue)
    Assert-Smoke ($run.ExitCode -ne 0 -and $run.Text.Contains('tar.exe') -and -not $run.Text.Contains('ZIP') -and $written.Count -eq 0) "x -x!<the collapsed name>: exit $($run.ExitCode), $($written.Count) files $($run.Text)"

    $o3 = Join-Path $folder 'o3'
    $run = Invoke-Pakko -Exe $tarExe @('x', $chosen.Tar, "-o$o3")
    $accentText = Get-Content -LiteralPath (Join-Path $o3 $chosen.Accented) -Raw -ErrorAction SilentlyContinue
    $plainText = Get-Content -LiteralPath (Join-Path $o3 $chosen.Plain) -Raw -ErrorAction SilentlyContinue
    Assert-Smoke ($run.ExitCode -eq 0 -and $accentText -eq 'ACCENT' -and $plainText -eq 'PLAIN') "x everything: exit $($run.ExitCode), '$accentText' '$plainText' $($run.Text)"

    # T-F443: a folder listed by best fit, with an empty subfolder - only the true folder arrives.
    $trueFolder = $chosen.Accented.Substring(0, $chosen.Accented.Length - 4)
    $folderTar = Join-Path $folder 'folder.tar'
    New-TarFile -Path $folderTar -Entries @(
        @{ Name = "$trueFolder/"; Folder = $true }, @{ Name = "$trueFolder/x.txt"; Content = 'X' }, @{ Name = "$trueFolder/empty/"; Folder = $true })
    $o4 = Join-Path $folder 'o4'
    $run = Invoke-Pakko -Exe $tarExe @('x', $folderTar, "-o$o4")
    $folders = @(Get-ChildItem -LiteralPath $o4 -Recurse -Directory -ErrorAction SilentlyContinue | ForEach-Object { [System.IO.Path]::GetRelativePath($o4, $_.FullName) }) | Sort-Object
    $expected = @($trueFolder, (Join-Path $trueFolder 'empty')) | Sort-Object
    Assert-Smoke ($run.ExitCode -eq 0 -and ($folders -join '|') -ceq ($expected -join '|')) "x a best-fit folder: exit $($run.ExitCode), folders '$($folders -join '|')' $($run.Text)"
    "$($chosen.Plain) twice in the listing; selection and filter refused, everything extracted, no folder twin"
}

Add-SmokeStep 'CLI-045' {
    # T-F442: read-only formats tar.exe opens, each expected by what this tar.exe reads (pakko i).
    $folder = Get-StepFolder 'cli045'
    $tarExe = Join-Path $env:SystemRoot 'System32\tar.exe'
    $src = Join-Path $folder 'src'
    New-Item -ItemType Directory -Path $src -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $src 'a.txt') -Value 'alpha' -NoNewline -Encoding ascii

    $info = Invoke-Pakko @('i')
    $lines = @{}
    foreach ($name in 'iso', 'cab', 'cpio', 'ar (.deb)', 'tar.Z', 'tar.lz') {
        $line = ($info.Output -split "`r?`n") | Where-Object { $_.TrimStart().StartsWith($name + ' ') } | Select-Object -First 1
        Assert-Smoke ($null -ne $line) "i has no '$name' line: $($info.Output)"
        $lines[$name] = $line
    }
    $reads = { param($name) $lines[$name] -match '\bsupported' -and $lines[$name] -notmatch 'not supported' }

    $samples = @(
        @{ Line = 'iso'; File = 'a.iso'; Args = @('--format=iso9660', '-cf') },
        @{ Line = 'cpio'; File = 'a.cpio'; Args = @('--format=newc', '-cf') },
        @{ Line = 'ar (.deb)'; File = 'a.ar'; Args = @('--format=ar', '-cf') },
        @{ Line = 'tar.Z'; File = 'a.tar.Z'; Args = @('-cZf') },
        @{ Line = 'tar.lz'; File = 'a.tar.lz'; Args = @('--lzip', '-cf') })
    foreach ($sample in $samples) {
        $archive = Join-Path $folder $sample.File
        Push-Location $src
        try { & $tarExe @($sample.Args + @($archive, 'a.txt')) 2>$null | Out-Null } finally { Pop-Location }
        if (-not (Test-Path -LiteralPath $archive)) {
            Assert-Smoke (-not (& $reads $sample.Line)) "$($sample.File): this tar.exe reads it but could not write it"
            continue
        }
        $out = Join-Path $folder ('o-' + $sample.File)
        $x = Invoke-Pakko @('x', $archive, "-o$out")
        $text = Get-Content -LiteralPath (Join-Path $out 'a.txt') -Raw -ErrorAction SilentlyContinue
        if (& $reads $sample.Line) {
            $l = Invoke-Pakko @('l', $archive, '-ba')
            $t = Invoke-Pakko @('t', $archive)
            Assert-Smoke ($x.ExitCode -eq 0 -and $text -eq 'alpha' -and $l.ExitCode -eq 0 -and $l.Output.Contains('a.txt') -and $t.ExitCode -le 1) "$($sample.File): x $($x.ExitCode) '$text', l $($l.ExitCode), t $($t.ExitCode) $($x.Text) $($t.Text)"
        } else {
            Assert-Smoke ($x.ExitCode -ne 0 -and -not (Test-Path -LiteralPath (Join-Path $out 'a.txt'))) "$($sample.File) on a tar.exe that does not read it: exit $($x.ExitCode) $($x.Text)"
        }
    }

    $cab = Join-Path $folder 'a.cab'
    $ddf = Join-Path $folder 'a.ddf'
    Set-Content -LiteralPath $ddf -Encoding ascii -Value @(
        '.Set CabinetNameTemplate=a.cab', ".Set DiskDirectoryTemplate=$folder", '.Set InfFileName=NUL', '.Set RptFileName=NUL', "`"$(Join-Path $src 'a.txt')`" a.txt")
    & (Join-Path $env:SystemRoot 'System32\makecab.exe') /F $ddf | Out-Null
    $out = Join-Path $folder 'o-cab'
    $x = Invoke-Pakko @('x', $cab, "-o$out")
    $text = Get-Content -LiteralPath (Join-Path $out 'a.txt') -Raw -ErrorAction SilentlyContinue
    if (& $reads 'cab') {
        Assert-Smoke ($x.ExitCode -eq 0 -and $text -eq 'alpha') "a.cab: exit $($x.ExitCode) '$text' $($x.Text)"
    } else {
        Assert-Smoke ($x.ExitCode -ne 0 -and $null -eq $text) "a.cab on a tar.exe that does not read it: exit $($x.ExitCode)"
    }
    $out = Join-Path $folder 'o-stx'
    $x = Invoke-Pakko @('x', $cab, "-o$out", '-stxcab')
    Assert-Smoke ($x.ExitCode -ne 0 -and -not (Test-Path -LiteralPath (Join-Path $out 'a.txt'))) "x -stxcab: exit $($x.ExitCode) $($x.Text)"

    # A UDF descriptor patched in after the ISO 9660 ones: libarchive would read only the stub.
    $iso = Join-Path $folder 'a.iso'
    if (Test-Path -LiteralPath $iso) {
        $bytes = [System.IO.File]::ReadAllBytes($iso)
        $sector = 16
        while ([System.Text.Encoding]::ASCII.GetString($bytes, $sector * 2048 + 1, 5) -eq 'CD001') { $sector++ }
        foreach ($id in 'BEA01', 'NSR02', 'TEA01') {
            [Array]::Clear($bytes, $sector * 2048, 2048)
            [System.Text.Encoding]::ASCII.GetBytes($id).CopyTo($bytes, $sector * 2048 + 1)
            $bytes[$sector * 2048 + 6] = 1
            $sector++
        }
        $udf = Join-Path $folder 'udf.iso'
        [System.IO.File]::WriteAllBytes($udf, $bytes)
        $out = Join-Path $folder 'o-udf'
        $x = Invoke-Pakko @('x', $udf, "-o$out")
        Assert-Smoke ($x.ExitCode -ne 0 -and $x.Text.Contains('UDF') -and -not (Test-Path -LiteralPath (Join-Path $out 'a.txt'))) "x udf.iso: exit $($x.ExitCode) $($x.Text)"
        # T-F457: -stxudf is a type 7-Zip knows; the image is left out, not refused as unknown.
        $x = Invoke-Pakko @('x', $udf, "-o$out", '-stxudf')
        Assert-Smoke ($x.ExitCode -eq 2 -and $x.Text.Contains('Cannot open the file as archive')) "x -stxudf: exit $($x.ExitCode) $($x.Text)"

        # T-F457: an ISO inside gzip comes out as the image, which then takes the bare image's checks.
        $isoGz = Join-Path $folder 'a.iso.gz'
        $output = [System.IO.File]::Create($isoGz)
        try {
            $gzip = [System.IO.Compression.GZipStream]::new($output, [System.IO.Compression.CompressionLevel]::Optimal)
            try { $gzip.Write([System.IO.File]::ReadAllBytes($iso)) } finally { $gzip.Dispose() }
        } finally { $output.Dispose() }
        $out = Join-Path $folder 'o-isogz'
        $x = Invoke-Pakko @('x', $isoGz, "-o$out")
        $image = Join-Path $out 'a.iso'
        $same = (Test-Path -LiteralPath $image) -and ((Get-FileHash -LiteralPath $image).Hash -eq (Get-FileHash -LiteralPath $iso).Hash)
        Assert-Smoke ($x.ExitCode -eq 0 -and $same) "x a.iso.gz: exit $($x.ExitCode) image same: $same $($x.Text)"
    }

    # T-F457: Slackware's .tlz is tar.lzma.
    $tlz = Join-Path $folder 's.tlz'
    Push-Location $src
    try { & $tarExe --lzma -cf $tlz a.txt 2>$null | Out-Null } finally { Pop-Location }
    if (Test-Path -LiteralPath $tlz) {
        $out = Join-Path $folder 'o-tlz'
        $x = Invoke-Pakko @('x', $tlz, "-o$out")
        $text = Get-Content -LiteralPath (Join-Path $out 'a.txt') -Raw -ErrorAction SilentlyContinue
        Assert-Smoke ($x.ExitCode -eq 0 -and $text -eq 'alpha') "s.tlz (tar.lzma): exit $($x.ExitCode) '$text' $($x.Text)"
    }
}

Add-SmokeStep 'CLI-046' {
    # T-F455: folder entries stored without a trailing '/' (type-5 headers "emptydir" and "full").
    $folder = Get-StepFolder 'cli046'
    $tar = Join-Path $folder 'noslash.tar'
    New-TarFile -Path $tar -Entries @(
        @{ Name = 'emptydir'; Folder = $true }, @{ Name = 'full'; Folder = $true }, @{ Name = 'full/x.txt'; Content = 'X' })
    $o1 = Join-Path $folder 'o1'
    $run = Invoke-Pakko @('x', $tar, "-o$o1")
    $items = @(Get-ChildItem -LiteralPath $o1 -Recurse -ErrorAction SilentlyContinue | ForEach-Object { [System.IO.Path]::GetRelativePath($o1, $_.FullName) }) | Sort-Object
    Assert-Smoke ($run.ExitCode -eq 0 -and ($items -join '|') -ceq 'emptydir|full|full\x.txt') "x everything: exit $($run.ExitCode), '$($items -join '|')' $($run.Text)"
    $o2 = Join-Path $folder 'o2'
    $run = Invoke-Pakko @('x', $tar, "-o$o2", 'emptydir')
    $items = @(Get-ChildItem -LiteralPath $o2 -Recurse -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
    Assert-Smoke ($run.ExitCode -eq 0 -and ($items -join '|') -ceq 'emptydir') "x emptydir: exit $($run.ExitCode), '$($items -join '|')' $($run.Text)"
    'both folders arrive; the empty one also alone'
}

Add-SmokeStep 'CLI-047' {
    # T-F460/T-F454: folders whose names end in a space or a dot - implicit, explicit, and both.
    $folder = Get-StepFolder 'cli047'
    $cases = @(
        @{ Name = 'implicit-space'; Entries = @(@{ Name = 'd /x.txt'; Content = 'X' }) },
        @{ Name = 'explicit-space'; Entries = @(@{ Name = 'd /'; Folder = $true }, @{ Name = 'd /x.txt'; Content = 'X' }) },
        @{ Name = 'implicit-dot-space'; Entries = @(@{ Name = 'd. /x.txt'; Content = 'X' }) },
        @{ Name = 'explicit-dot'; Entries = @(@{ Name = 'd./'; Folder = $true }, @{ Name = 'd./x.txt'; Content = 'X' }) })
    foreach ($case in $cases) {
        $tar = Join-Path $folder "$($case.Name).tar"
        New-TarFile -Path $tar -Entries $case.Entries
        $out = Join-Path $folder "o-$($case.Name)"
        $run = Invoke-Pakko @('x', $tar, "-o$out")
        $text = Get-Content -LiteralPath (Join-Path $out 'd\x.txt') -Raw -ErrorAction SilentlyContinue
        Assert-Smoke ($run.ExitCode -eq 0 -and $text -eq 'X') "x $($case.Name): exit $($run.ExitCode), d\x.txt '$text' $($run.Text)"
    }
    'every case delivers d\x.txt'
}
Add-SmokeStep 'CLI-048' {
    # T-F462: every listed file is accounted for - extracted, kept apart, or reported; never lost silently.
    $folder = Get-StepFolder 'cli048'
    $tarExe = Get-TarExePakko $folder
    $tar = Join-Path $folder 'three.tar'
    New-TarFile -Path $tar -Entries @(@{ Name = 'x.txt'; Content = 'one' }, @{ Name = 'x.txt'; Content = 'two' }, @{ Name = 'x.txt'; Content = 'three' })
    $o1 = Join-Path $folder 'o1'
    $run = Invoke-Pakko -Exe $tarExe @('x', $tar, "-o$o1", '-aou')
    $texts = @(Get-ChildItem -LiteralPath $o1 -File -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) | Sort-Object
    Assert-Smoke ($run.ExitCode -le 1 -and ($texts -join '|') -ceq 'one|three' -and $run.Text.Contains('not extracted (1)')) "three copies through tar.exe: exit $($run.ExitCode), '$($texts -join '|')' $($run.Text)"

    # T-F470: the helper writes each copy apart, so every copy meets the conflict rule.
    $o5 = Join-Path $folder 'o5'
    $run = Invoke-Pakko @('x', $tar, "-o$o5", '-aou')
    $texts = @(Get-ChildItem -LiteralPath $o5 -File -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) | Sort-Object
    Assert-Smoke ($run.ExitCode -eq 0 -and ($texts -join '|') -ceq 'one|three|two') "three copies through the helper: exit $($run.ExitCode), '$($texts -join '|')' $($run.Text)"

    # tar.exe replaces the file "x" with the folder "x/" after it and exits 0: the file is lost.
    $shadow = Join-Path $folder 'shadow.tar'
    New-TarFile -Path $shadow -Entries @(@{ Name = 'x'; Content = 'file' }, @{ Name = 'x/'; Folder = $true }, @{ Name = 'y.txt'; Content = 'y' })
    $o3 = Join-Path $folder 'o3'
    $run = Invoke-Pakko -Exe $tarExe @('x', $shadow, "-o$o3")
    $written = @(Get-ChildItem -LiteralPath $o3 -Recurse -File -ErrorAction SilentlyContinue)
    Assert-Smoke ($run.ExitCode -eq 2 -and $run.Text.Contains('1 of 2') -and $written.Count -eq 0) "file under a folder of its name: exit $($run.ExitCode), $($written.Count) written $($run.Text)"

    # Built from char codes, as CLI-044 does: e-acute collapses on 1251 and 932, a-macron on 1252.
    $chosen = $null
    foreach ($candidate in @(@{ Accent = [char]0xE9; Plain = 'e' }, @{ Accent = [char]0x101; Plain = 'a' })) {
        $accented = "caf$($candidate.Accent).txt"
        $twice = Join-Path $folder "twice-$([int]$candidate.Accent).tar"
        New-TarFile -Path $twice -Entries @(@{ Name = $accented; Content = 'first' }, @{ Name = $accented; Content = 'last' })
        $listed = Invoke-Pakko -Exe $tarExe @('l', $twice, '-ba')
        if ($listed.ExitCode -eq 0 -and ([regex]::Matches($listed.Output, [regex]::Escape("caf$($candidate.Plain).txt"))).Count -eq 2) { $chosen = $twice; break }
    }
    if (-not $chosen) { 'three copies and a shadowed file accounted for; this code page carries both accented candidates'; return }
    $o2 = Join-Path $folder 'o2'
    $run = Invoke-Pakko -Exe $tarExe @('x', $chosen, "-o$o2")
    $written = @(Get-ChildItem -LiteralPath $o2 -Recurse -File -ErrorAction SilentlyContinue)
    Assert-Smoke ($run.ExitCode -eq 2 -and $run.Text.Contains('1 of 2') -and $written.Count -eq 0) "accented twice: exit $($run.ExitCode), $($written.Count) written $($run.Text)"
    'three copies and a shadowed file accounted for; the accented name stored twice fails naming 1 of 2, nothing written'
}

Add-SmokeStep 'CLI-049' {
    # T-F463: tar.exe writes "a.txt." and "a.txt " as they are; both reach the destination, the trimmed one numbered.
    $folder = Get-StepFolder 'cli049'
    foreach ($name in 'a.txt.', 'a.txt ') {
        $code = [int][char]$name[-1]
        $tar = Join-Path $folder "trim$code.tar"
        New-TarFile -Path $tar -Entries @(@{ Name = 'a.txt'; Content = 'fine' }, @{ Name = $name; Content = 'other' })
        $o = Join-Path $folder "o$code"
        $run = Invoke-Pakko @('x', $tar, "-o$o")
        $texts = @(Get-ChildItem -LiteralPath $o -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { "$($_.Name)=$(Get-Content -LiteralPath $_.FullName -Raw)" })
        Assert-Smoke ($run.ExitCode -eq 0 -and ($texts -join '|') -ceq 'a (1).txt=other|a.txt=fine') "'$name': exit $($run.ExitCode), '$($texts -join '|')' $($run.Text)"
    }
    'a.txt. and a.txt beside a.txt both extracted, the trimmed one numbered'
}

# T-F470: on a native x64 Windows with a measured libarchive the tar-family reader is the archive
# helper over archiveint.dll, and names outside the ANSI page arrive exactly (tar.exe lists them
# by best fit, CLI-044). Pakko's own 'a -ttar' refuses such names, so the archive is written here,
# each name in a pax 'path' record (UTF-8).
Add-SmokeStep 'CLI-050' {
    $info = Invoke-Pakko @('i')
    $line = @($info.Output -split '\r?\n' | Where-Object { $_ -match '^tar-family reader:' })
    Assert-Smoke ($info.ExitCode -eq 0 -and $line.Count -eq 1) "i: exit $($info.ExitCode), $($line.Count) reader lines: $($info.Text)"
    Assert-Smoke ($line[0] -match '^tar-family reader: archive helper; archiveint\.dll: libarchive 3\.[5-8]\.\d+.*; the probe passed$') "the reader line of i: $($line[0])"

    $folder = Get-StepFolder 'cli050'
    # Japanese, Greek, Hebrew and an emoji (a surrogate pair): none is on a Western or Cyrillic page.
    $names = @(
        ([string][char]0x65E5 + [char]0x672C + [char]0x8A9E + '.txt')
        ([string][char]0x0395 + [char]0x03BB + [char]0x03BB + [char]0x03B7 + [char]0x03BD + '.txt')
        ([string][char]0x05E2 + [char]0x05D1 + [char]0x05E8 + '.txt')
        ('emoji-' + [char]::ConvertFromUtf32(0x1F600) + '.txt')
    )
    $entries = @()
    for ($i = 0; $i -lt $names.Count; $i++) {
        $body = " path=$($names[$i])`n"
        $length = [System.Text.Encoding]::UTF8.GetByteCount($body)
        $digits = "$length".Length
        while ("$($length + $digits)".Length -ne $digits) { $digits++ }
        $entries += @{ Name = "PaxHeaders/$i"; Type = 'x'; Content = "$($length + $digits)$body" }
        $entries += @{ Name = "plain$i.txt"; Content = "content of $($names[$i])" }
    }
    $tar = Join-Path $folder 'names.tar'
    New-TarFile -Path $tar -Entries $entries

    $list = Invoke-Pakko @('l', '-sccUTF-8', $tar)
    Assert-Smoke ($list.ExitCode -eq 0) "l: exit $($list.ExitCode) $($list.Text)"
    foreach ($name in $names) { Assert-Smoke ($list.Output.Contains($name)) "l does not show $name exactly: $($list.Output)" }

    $o = Join-Path $folder 'o'
    $run = Invoke-Pakko @('x', $tar, "-o$o")
    $texts = @(Get-ChildItem -LiteralPath $o -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { "$($_.Name)=$([System.IO.File]::ReadAllText($_.FullName))" })
    $expected = @($names | Sort-Object | ForEach-Object { "$_=content of $_" })
    Assert-Smoke ($run.ExitCode -eq 0 -and ($texts -join '|') -ceq ($expected -join '|')) "x: exit $($run.ExitCode), '$($texts -join '|')' $($run.Text)"
    "$($line[0]); $($names.Count) names outside the ANSI page listed and extracted exactly"
}

# T-F470: without the helper beside it Pakko reads tar-family archives through tar.exe, quietly.
# A copy of pakko.exe alone in a folder stands in for a build with no helper (ARM64 takes the same
# path through its own check, covered by ArchiveintGateTests).
Add-SmokeStep 'CLI-051' {
    $folder = Get-StepFolder 'cli051'
    $alone = Join-Path $folder 'bin\pakko.exe'
    $null = [System.IO.Directory]::CreateDirectory((Split-Path $alone))
    Copy-Item -LiteralPath $Smoke.Pakko -Destination $alone
    $info = Invoke-Tool -Exe $alone -Arguments @('i')
    $line = @($info.Output -split '\r?\n' | Where-Object { $_ -match '^tar-family reader:' })
    Assert-Smoke ($info.ExitCode -eq 0 -and $line.Count -eq 1) "i: exit $($info.ExitCode), $($line.Count) reader lines: $($info.Text)"
    Assert-Smoke ($line[0] -ceq 'tar-family reader: tar.exe; archiveint.dll: not probed; no archive helper beside Pakko') "the reader line of i: $($line[0])"

    $tar = Join-Path $folder 'plain.tar'
    New-TarFile -Path $tar -Entries @(@{ Name = 'd/a.txt'; Content = 'A' }, @{ Name = 'b.txt'; Content = 'B' })
    $o = Join-Path $folder 'o'
    $run = Invoke-Tool -Exe $alone -Arguments @('x', $tar, "-o$o")
    $texts = @(Get-ChildItem -LiteralPath $o -File -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName | ForEach-Object { "$($_.FullName.Substring($o.Length + 1))=$([System.IO.File]::ReadAllText($_.FullName))" })
    Assert-Smoke ($run.ExitCode -eq 0 -and ($texts -join '|') -ceq 'b.txt=B|d\a.txt=A') "x through tar.exe: exit $($run.ExitCode), '$($texts -join '|')' $($run.Text)"
    $test = Invoke-Tool -Exe $alone -Arguments @('t', $tar)
    Assert-Smoke ($test.ExitCode -le 1) "t through tar.exe: exit $($test.ExitCode) $($test.Text)"
    "$($line[0]); a tar extracted and tested through tar.exe"
}

Add-SmokeStep 'CLI-052' {
    $folder = Get-StepFolder 'cli052'
    $many = Get-Fixture Many
    $hashes = foreach ($run in @('a1', 'a2', 'f1', 'f2')) {
        $zip = Join-Path $folder "$run.zip"
        $switches = if ($run -like 'f*') { @('-mx1') } else { @() }
        $null = Assert-Exit (Invoke-Pakko (@('a') + $switches + @($zip, $many))) "pakko a $run"
        Get-Sha256 $zip
    }
    Assert-Smoke ($hashes[0] -eq $hashes[1]) "two pakko a of the same folder differ: $($hashes[0]) $($hashes[1])"
    Assert-Smoke ($hashes[2] -eq $hashes[3]) "two pakko a -mx1 of the same folder differ: $($hashes[2]) $($hashes[3])"
    'both pairs byte-identical'
}

# T-F475: a device name as the archive. On Windows 10 and Server 2022 "CON.zip" in any folder is
# the console, and a folder "x\CON" is too; on Windows 11 both are ordinary names.
Add-SmokeStep 'CLI-053' {
    $results = foreach ($case in @(@('a', 'CON.zip', 'b.txt'), @('a', '-sae', 'x\CON\o.zip', 'b.txt'))) {
        $folder = Get-StepFolder ('cli053-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
        [System.IO.File]::WriteAllText((Join-Path $folder 'b.txt'), 'B')
        $run = Invoke-Pakko $case -WorkingDirectory $folder
        $archive = Join-Path $folder $case[-2]
        $device = [System.IO.Path]::GetFullPath($archive).StartsWith('\\.\') -or
            [System.IO.Path]::GetFullPath([System.IO.Path]::GetDirectoryName($archive)).StartsWith('\\.\')
        Assert-Smoke ($run.Text -notmatch 'Exception|Unexpected error') "pakko $($case -join ' '): $($run.Text)"
        $names = @([System.IO.Directory]::EnumerateFileSystemEntries('\\?\' + $folder, '*', 'AllDirectories') | ForEach-Object { [System.IO.Path]::GetFileName($_) })
        if ($device) {
            Assert-Smoke ($run.ExitCode -eq 2) "pakko $($case -join ' '): a device here, exit $($run.ExitCode) $($run.Text)"
            Assert-Smoke (($names -join ',') -eq 'b.txt') "pakko $($case -join ' '): left $($names -join ',')"
            "$($case[-2]): a device here, refused"
        }
        else {
            Assert-Smoke ($run.ExitCode -eq 0) "pakko $($case -join ' '): exit $($run.ExitCode) $($run.Text)"
            Assert-Smoke ([System.IO.File]::Exists('\\?\' + $archive)) "pakko $($case -join ' '): no archive"
            "$($case[-2]): an ordinary file here, written"
        }
    }
    $results -join '; '
}

# T-F509: a folder holding "a.txt " (made through \\?\) beside "a.txt". Its plain path is the twin's,
# so the file is refused: the archive never holds the twin's bytes under the spaced name, and -sdel
# deletes nothing.
Add-SmokeStep 'CLI-054' {
    $folder = Get-StepFolder 'cli054'
    $source = Join-Path $folder 'tw'
    $null = [System.IO.Directory]::CreateDirectory($source)
    [System.IO.File]::WriteAllText('\\?\' + (Join-Path $source 'a.txt '), 'SPACED')
    [System.IO.File]::WriteAllText((Join-Path $source 'a.txt'), 'TWIN')
    $zip = Join-Path $folder 'tw.zip'
    $run = Invoke-Pakko @('a', '-sdel', $zip, $source)
    Assert-Smoke ($run.ExitCode -eq 2) "pakko a -sdel: exit $($run.ExitCode) $($run.Text)"
    Assert-Smoke ($run.Text -match 'ends in a space or a dot') "no refusal for the trimmed name: $($run.Text)"
    Assert-Smoke ([System.IO.File]::ReadAllText('\\?\' + (Join-Path $source 'a.txt ')) -eq 'SPACED') 'the spaced file changed or went'
    Assert-Smoke ([System.IO.File]::ReadAllText((Join-Path $source 'a.txt')) -eq 'TWIN') 'the twin changed or went'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try { $names = @($archive.Entries | ForEach-Object { $_.FullName }) } finally { $archive.Dispose() }
    Assert-Smoke (($names -join ',') -eq 'tw/a.txt') "archive entries: $($names -join ',')"
    [System.IO.File]::Delete('\\?\' + (Join-Path $source 'a.txt '))
    'refused, exit 2; both files kept; the archive holds only the twin under its own name'
}

Add-SmokeStep 'CLI-057' {
    $folder = Get-StepFolder 'cli057'
    $source = Join-Path $folder 'cs'
    $null = [System.IO.Directory]::CreateDirectory($source)
    $fsutil = Join-Path ([System.Environment]::SystemDirectory) 'fsutil.exe'
    $null = & $fsutil file setCaseSensitiveInfo $source enable
    if ($LASTEXITCODE -ne 0) { return 'skipped: fsutil cannot make a case-sensitive folder here' }
    [System.IO.File]::WriteAllText((Join-Path $source 'A.TXT'), 'UPPER')
    [System.IO.File]::WriteAllText((Join-Path $source 'a.txt'), 'lower')
    $zip = Join-Path $folder 'cs.zip'
    $run = Invoke-Pakko @('a', '-sdel', $zip, (Join-Path $source 'a.txt'))
    Assert-Smoke ($run.ExitCode -eq 0) "pakko a -sdel: exit $($run.ExitCode) $($run.Text)"
    Assert-Smoke (-not [System.IO.File]::Exists((Join-Path $source 'a.txt'))) 'a.txt was archived but not deleted'
    Assert-Smoke ([System.IO.File]::ReadAllText((Join-Path $source 'A.TXT')) -eq 'UPPER') 'A.TXT changed or went'
    'a.txt archived and deleted; A.TXT kept'
}

Add-SmokeStep 'CLI-058' {
    $folder = Get-StepFolder 'cli058'
    $source = Join-Path $folder 'a.txt'
    [System.IO.File]::WriteAllText($source, 'listed')
    $zip = Join-Path $folder 'a.zip'
    $made = Invoke-Pakko @('a', $zip, $source)
    Assert-Smoke ($made.ExitCode -eq 0) "pakko a: exit $($made.ExitCode) $($made.Text)"
    $stdinRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'Archiver.CLI.Stdin'
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Smoke.Pakko
    $info.Arguments = 't -si'
    $info.UseShellExecute = $false
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $killed = [System.Diagnostics.Process]::Start($info)
    try {
        $killed.StandardInput.BaseStream.Write([byte[]]::new(4096), 0, 4096)
        $killed.StandardInput.BaseStream.Flush()
        $marker = "-$($killed.Id)-"
        $left = $null
        for ($i = 0; $i -lt 100 -and -not $left; $i++) {
            if ([System.IO.Directory]::Exists($stdinRoot)) {
                $left = [System.IO.Directory]::GetDirectories($stdinRoot) | Where-Object { (Split-Path $_ -Leaf).Contains($marker) } | Select-Object -First 1
            }
            if (-not $left) { [System.Threading.Thread]::Sleep(100) }
        }
        Assert-Smoke ($null -ne $left) "no -si staging folder for pid $($killed.Id) under $stdinRoot"
        $killed.Kill()
        $null = $killed.WaitForExit(10000)
    }
    finally {
        if (-not $killed.HasExited) { $killed.Kill() }
        $killed.Dispose()
    }
    Assert-Smoke ([System.IO.Directory]::Exists($left)) 'the killed run left no folder to sweep'
    $listed = Invoke-Pakko @('l', $zip)
    Assert-Smoke ($listed.ExitCode -eq 0) "pakko l: exit $($listed.ExitCode) $($listed.Text)"
    Assert-Smoke (-not [System.IO.Directory]::Exists($left)) "the next pakko left the killed run's folder: $left"
    'the killed -si run left its folder; the next pakko swept it'
}

Add-SmokeStep 'CLI-060' {
    $fixtures = Join-Path $PSScriptRoot '..\..\tests\Archiver.Core.IntegrationTests\Fixtures'
    $damaged = Join-Path $fixtures 'damaged_data.7z'
    $out = Join-Path (Get-StepFolder 'cli060') 'out'
    $extract = Invoke-Pakko @('x', $damaged, "-o$out")
    $test = Invoke-Pakko @('t', $damaged)
    $encrypted = Invoke-Pakko @('t', (Join-Path $fixtures 'encrypted.7z'))
    foreach ($run in $extract, $test) {
        Assert-Smoke ($run.ExitCode -eq 2) "damaged_data.7z: exit $($run.ExitCode) $($run.Text)"
        Assert-Smoke (-not $run.Text.Contains('password-protected')) "a damaged plain 7z reads as password-protected: $($run.Text)"
    }
    Assert-Smoke ($encrypted.ExitCode -eq 2 -and $encrypted.Text.Contains('password-protected')) "encrypted.7z: exit $($encrypted.ExitCode) $($encrypted.Text)"
    'the damaged plain 7z fails with its own error; encrypted.7z still reads as password-protected'
}

Add-SmokeStep 'CLI-059' {
    $folder = Get-StepFolder 'cli059'
    $arcs = Join-Path $folder 'arcs'
    $deep = Join-Path $arcs 'deep'
    $null = [System.IO.Directory]::CreateDirectory($deep)
    $source = Join-Path $folder 'a.txt'
    [System.IO.File]::WriteAllText($source, 'archived')
    $made = Invoke-Pakko @('a', (Join-Path $arcs 'one.zip'), $source)
    Assert-Smoke ($made.ExitCode -eq 0) "pakko a: exit $($made.ExitCode) $($made.Text)"
    $out = Join-Path $folder 'out'
    $security = Get-Acl -LiteralPath $deep
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        [System.Security.Principal.WindowsIdentity]::GetCurrent().User, 'ListDirectory', 'Deny')
    $security.AddAccessRule($rule)
    Set-Acl -LiteralPath $deep -AclObject $security
    try {
        $run = Invoke-Pakko @('x', '-an', "-air!$arcs\*.zip", "-o$out") -WorkingDirectory $folder
        Assert-Smoke ($run.ExitCode -eq 2) "an unreadable folder: exit $($run.ExitCode) $($run.Text)"
        Assert-Smoke ($run.Text.Contains($deep)) "stderr does not name the folder: $($run.Text)"
        Assert-Smoke (-not [System.IO.Directory]::Exists($out)) 'something was extracted'
    }
    finally {
        $null = $security.RemoveAccessRule($rule)
        Set-Acl -LiteralPath $deep -AclObject $security
    }
    $again = Invoke-Pakko @('x', '-an', "-air!$arcs\*.zip", "-o$out") -WorkingDirectory $folder
    Assert-Smoke ($again.ExitCode -eq 0) "readable again: exit $($again.ExitCode) $($again.Text)"
    'an unreadable folder stops the search with exit 2 and its name; readable, exit 0'
}

Add-SmokeStep 'CLI-055' {
    $folder = Get-StepFolder 'cli055'
    $content = Join-Path $folder 'content'
    $null = [System.IO.Directory]::CreateDirectory($content)
    [System.IO.File]::WriteAllText((Join-Path $content 'a.txt'), ('recoverable line' + "`n") * 4000)
    $zip = Join-Path $folder 'x.zip'
    $made = Invoke-Pakko @('a', '-rr10', $zip, $content)
    Assert-Smoke ($made.ExitCode -eq 0) "pakko a -rr10: exit $($made.ExitCode) $($made.Text)"
    $stream = [System.IO.File]::Open($zip, 'Open', 'ReadWrite')
    try {
        $stream.Position = [long]($stream.Length / 3)
        $value = $stream.ReadByte()
        $stream.Position--
        $stream.WriteByte([byte]($value -bxor 0xFF))
    } finally { $stream.Dispose() }
    $twin = Join-Path $folder 'out'
    $null = [System.IO.Directory]::CreateDirectory($twin)
    $refused = Invoke-Pakko @('r', $zip, "-o$twin.")
    Assert-Smoke ($refused.ExitCode -eq 2) "pakko r -o out.: exit $($refused.ExitCode) $($refused.Text)"
    Assert-Smoke ($refused.Text -match 'ends in a space or a dot') "no refusal for the trimmed folder: $($refused.Text)"
    Assert-Smoke (@(Get-ChildItem -LiteralPath $twin -Force).Count -eq 0) 'something was written into the twin out'
    $fixed = Join-Path $folder 'fixed'
    $repaired = Invoke-Pakko @('r', $zip, "-o$fixed")
    Assert-Smoke ($repaired.ExitCode -eq 0) "pakko r -o fixed: exit $($repaired.ExitCode) $($repaired.Text)"
    Assert-Smoke (@(Get-ChildItem -LiteralPath $fixed -File).Count -eq 1) 'no repaired copy in fixed'
    'out. refused, exit 2, out empty; the repair into fixed writes its copy'
}

# T-F512 (c): the path matrix through the installed pakko. The cases are the generator's own
# (scripts/smoke/PathMatrixCases.json, kept current by PathMatrixDiskTests): a name shape in a
# spelling, as a source (with and without -sdel), an archive or a destination, ZIP or tar.gz, with or
# without a twin Windows would open in its place. Per case: no crash, the twin untouched, no twin
# bytes written, nothing outside the case's folder, exit 0 holds the named place's bytes (and -sdel
# then removed it, and only it), the plain name works.
Add-SmokeStep 'CLI-056' {
    $root = Get-StepFolder 'cli056'
    $cases = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'PathMatrixCases.json') -Raw -Encoding utf8 | ConvertFrom-Json
    $share = 'localhost\' + $root[0] + '$' + $root.Substring(2)
    $tar = Join-Path $env:SystemRoot 'System32\tar.exe'
    $long = '\\?\'
    # By the string: .NET Framework (Windows PowerShell on the VM) refuses a path over 260 here.
    function Get-Parent([string]$path) { $path.Substring(0, $path.LastIndexOf([char]92)) }
    function Resolve-CasePath([string]$path) {
        if ($null -eq $path) { return $null }
        $path.Replace('localhost\Z$\R', $share).Replace('Z:\R', $root).Replace('Z:/R', $root.Replace('\', '/'))
    }
    function Write-MarkedArchive([string]$At, [string]$Ext, [string]$Mark, [string]$Scratch) {
        $null = [System.IO.Directory]::CreateDirectory($long + (Get-Parent $at))
        if ($ext -eq 'zip') {
            $bytes = [System.IO.MemoryStream]::new()
            $zip = [System.IO.Compression.ZipArchive]::new($bytes, [System.IO.Compression.ZipArchiveMode]::Create, $true)
            $writer = [System.IO.StreamWriter]::new($zip.CreateEntry('m.txt').Open())
            $writer.Write($mark); $writer.Dispose(); $zip.Dispose()
            [System.IO.File]::WriteAllBytes($long + $at, $bytes.ToArray())
            return
        }
        $null = [System.IO.Directory]::CreateDirectory($scratch)
        [System.IO.File]::WriteAllText((Join-Path $scratch 'm.txt'), $mark)
        $made = Invoke-Tool -Exe $tar -Arguments @('-czf', (Join-Path $scratch 'm.tar.gz'), '-C', $scratch, 'm.txt')
        Assert-Smoke ($made.ExitCode -eq 0) "tar.exe could not make the case's archive: $($made.Text)"
        [System.IO.File]::Copy((Join-Path $scratch 'm.tar.gz'), $long + $at)
    }
    function Get-State([string]$path) {
        $at = $long + $path
        if ([System.IO.File]::Exists($at)) {
            return [System.BitConverter]::ToString([System.IO.File]::ReadAllBytes($at)) + '@' + [System.IO.File]::GetLastWriteTimeUtc($at).Ticks
        }
        if (-not [System.IO.Directory]::Exists($at)) { return 'gone' }
        (@([System.IO.Directory]::GetFileSystemEntries($at, '*', 'AllDirectories') | Sort-Object -CaseSensitive | ForEach-Object {
            $_ + '=' + $(if ([System.IO.File]::Exists($_)) { [System.BitConverter]::ToString([System.IO.File]::ReadAllBytes($_)) } else { 'dir' })
        })) -join '|'
    }
    function Get-ArchiveText([string]$file) {
        $stream = [System.IO.File]::OpenRead($file)
        try {
            if ($file.EndsWith('.zip')) {
                $zip = [System.IO.Compression.ZipArchive]::new($stream)
                (@($zip.Entries | Where-Object { -not $_.FullName.EndsWith('/') } | ForEach-Object {
                    $reader = [System.IO.StreamReader]::new($_.Open()); $reader.ReadToEnd(); $reader.Dispose()
                })) -join ''
                $zip.Dispose()
                return
            }
            $gzip = [System.IO.Compression.GZipStream]::new($stream, [System.IO.Compression.CompressionMode]::Decompress)
            $bytes = [System.IO.MemoryStream]::new()
            $gzip.CopyTo($bytes); $gzip.Dispose()
            [System.Text.Encoding]::GetEncoding(28591).GetString($bytes.ToArray())
        } finally { $stream.Dispose() }
    }
    function Get-FilesText([string]$folder) {
        if (-not [System.IO.Directory]::Exists($long + $folder)) { return '' }
        (@([System.IO.Directory]::GetFiles($long + $folder, '*', 'AllDirectories') | ForEach-Object { [System.IO.File]::ReadAllText($_) })) -join ''
    }

    $failures = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    for ($i = 0; $i -lt $cases.Count; $i++) {
        $case = $cases[$i]
        $folder = Join-Path $root "c$i"
        $null = [System.IO.Directory]::CreateDirectory($folder)
        $null = $seen.Add("c$i")
        $path = Resolve-CasePath $case.path
        $place = Resolve-CasePath $case.place
        $twin = Resolve-CasePath $case.twin
        $lit = "LITERAL-$i"
        $mark = "TWIN-$i"
        $ext = $case.ext
        $isSource = $case.role -in 'a-source', 'a-sdel'
        if ($case.onDisk) {
            if ($isSource) {
                $null = [System.IO.Directory]::CreateDirectory($long + (Get-Parent $place))
                [System.IO.File]::WriteAllText($long + $place, $lit)
            } elseif ($case.role -eq 'x-archive') {
                Write-MarkedArchive -At $place -Ext $ext -Mark $lit -Scratch (Join-Path $root "s$i-l")
            }
            if ($twin) {
                if ($isSource) {
                    $null = [System.IO.Directory]::CreateDirectory($long + (Get-Parent $twin))
                    [System.IO.File]::WriteAllText($long + $twin, $mark)
                } elseif ($case.role -eq 'x-archive') {
                    Write-MarkedArchive -At $twin -Ext $ext -Mark $mark -Scratch (Join-Path $root "s$i-t")
                } else {
                    $null = [System.IO.Directory]::CreateDirectory($long + $twin)
                    [System.IO.File]::WriteAllText($long + (Join-Path $twin 't.txt'), $mark)
                }
            }
        }
        foreach ($scratch in "s$i-l", "s$i-t") {
            if ([System.IO.Directory]::Exists((Join-Path $root $scratch))) { [System.IO.Directory]::Delete((Join-Path $root $scratch), $true) }
        }
        $twinBefore = if ($twin) { Get-State $twin } else { '' }
        $out = Join-Path $folder 'out'
        $made = Join-Path $folder "out.$ext"
        $type = if ($ext -eq 'zip') { '-tzip' } else { '-ttar.gz' }
        $arguments = switch ($case.role) {
            'a-source' { @('a', $type, $made, $path) }
            'a-sdel' { @('a', $type, '-sdel', $made, $path) }
            'x-archive' { @('x', $path, "-o$out") }
            'x-destination' {
                Write-MarkedArchive -At (Join-Path $folder "in.$ext") -Ext $ext -Mark $lit -Scratch (Join-Path $folder 'seed')
                if ([System.IO.Directory]::Exists((Join-Path $folder 'seed'))) { [System.IO.Directory]::Delete((Join-Path $folder 'seed'), $true) }
                @('x', (Join-Path $folder "in.$ext"), "-o$path")
            }
        }
        $run = Invoke-Pakko $arguments -WorkingDirectory $root -TimeoutSeconds 120
        $id = "$($case.id) ($path)"
        if ($run.ExitCode -notin 0, 1, 2, 7 -or $run.Text -match 'Unhandled exception') {
            $failures.Add("${id}: exit $($run.ExitCode) $($run.Text)")
            continue
        }
        if ($twin -and (Get-State $twin) -ne $twinBefore) { $failures.Add("${id}: the twin changed") }
        $written = ''
        if ([System.IO.File]::Exists($made)) { $written += Get-ArchiveText $made }
        $written += Get-FilesText $out
        if ($case.role -eq 'x-destination') { $written += Get-FilesText $place }
        if ($written.Contains('TWIN-')) { $failures.Add("${id}: the twin's bytes were written") }
        if ($run.ExitCode -eq 0 -and $case.onDisk -and -not $written.Contains($lit)) { $failures.Add("${id}: exit 0 without the named place's bytes") }
        if ($case.role -eq 'a-sdel' -and $case.onDisk) {
            $gone = -not [System.IO.File]::Exists($long + $place)
            if ($run.ExitCode -eq 0 -and -not $gone) { $failures.Add("${id}: -sdel kept the source after exit 0") }
            if ($run.ExitCode -ne 0 -and $gone) { $failures.Add("${id}: -sdel deleted the source of a failed run") }
        }
        # "ok.txt\" names a folder: a file under that spelling is refused, as 7-Zip refuses it.
        $folderOnly = $case.id.Contains(':trailing-separator:') -and $case.role -ne 'x-destination'
        if ($case.plain -and -not $folderOnly -and $run.ExitCode -ne 0) { $failures.Add("${id}: the plain name failed, exit $($run.ExitCode) $($run.Text)") }
        foreach ($name in @([System.IO.Directory]::GetFileSystemEntries($long + $root) | ForEach-Object { [System.IO.Path]::GetFileName($_) })) {
            if ($seen.Add($name)) { $failures.Add("${id}: written outside the case's folder: $name") }
        }
    }
    Assert-Smoke ($failures.Count -eq 0) ("$($failures.Count) of $($cases.Count) cases:`n" + ($failures -join "`n"))
    # Names made through \\?\ go only through \\?\; a failed run keeps them to look at.
    [System.IO.Directory]::Delete($long + $root, $true)
    "$($cases.Count) cases, every one safe; the plain name works in each role"
}
