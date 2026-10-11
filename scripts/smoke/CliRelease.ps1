#Requires -Version 7.0
# Steps of docs/SMOKE.md's CLI rows of the release tier. Dot-sourced by scripts/Invoke-Smoke.ps1.

Add-SmokeStep 'CLI-006' {
    $folder = Get-StepFolder 'cli006'
    $started = Get-Date
    $fixtures = Join-Path $Smoke.Repo 'tests\Archiver.Core.IntegrationTests\Fixtures'
    $tree = Get-Fixture Tree
    $name = Split-Path -Leaf $tree

    $tarGz = Join-Path $folder 'tree.tar.gz'
    $made = Invoke-Pakko @('a', '-ttar.gz', $tarGz, $tree)
    Assert-Smoke ($made.ExitCode -eq 0) "a -ttar.gz: exit $($made.ExitCode) $($made.Text)"

    $cases = @(
        @{ Archive = Join-Path $fixtures 'valid.7z'; File = 'seven.txt' }
        @{ Archive = Join-Path $fixtures 'valid.rar'; File = 'rar.txt' }
        @{ Archive = $tarGz; File = "$name\random.bin" }
    )
    foreach ($case in $cases) {
        $out = Join-Path $folder ((Split-Path -Leaf $case.Archive) + '.out')
        $run = Invoke-Pakko @('x', $case.Archive, "-o$out")
        Assert-Smoke ($run.ExitCode -eq 0) "x $(Split-Path -Leaf $case.Archive): exit $($run.ExitCode) $($run.Text)"
        Assert-Smoke (Test-Path -LiteralPath (Join-Path $out $case.File)) "x $(Split-Path -Leaf $case.Archive) did not write $($case.File)"
    }
    Assert-Smoke ((Get-TreeHash (Join-Path $folder "tree.tar.gz.out\$name")) -eq (Get-TreeHash $tree)) 'the files from the tar.gz differ from the sources'

    $escaping = Join-Path $folder 'escaping.tar'
    New-TarFile -Path $escaping -Entries @(@{ Name = '../escaped.txt'; Content = 'out' }, @{ Name = 'inside.txt'; Content = 'in' })
    $out = Join-Path $folder 'nest\dest'
    $run = Invoke-Pakko @('x', $escaping, "-o$out")
    Assert-Smoke ($run.ExitCode -ne 0) "x on a tar with an escaping name: exit 0 $($run.Text)"
    Assert-Smoke (-not (Test-Path -LiteralPath (Join-Path $folder 'nest\escaped.txt'))) 'the escaping entry was written outside the destination'

    $left = @(Get-TarLeftover -Since $started)
    Assert-Smoke ($left.Count -eq 0) "left in %TEMP%: $(($left | Select-Object -First 3 -ExpandProperty Name) -join ', ')"
}

Add-SmokeStep 'CLI-007' {
    $folder = Get-StepFolder 'cli007'
    $tree = Get-Fixture Tree
    $name = Split-Path -Leaf $tree
    $pakko = $Smoke.Pakko

    $out = Join-Path $folder 'piped'
    $piped = Invoke-CmdLine "`"$pakko`" a -so piped.zip `"$tree`" | `"$pakko`" x -si `"-o$out`""
    Assert-Smoke ($piped.ExitCode -eq 0) "a -so | x -si: exit $($piped.ExitCode) $($piped.Text)"
    Assert-Smoke ((Get-TreeHash (Join-Path $out $name)) -eq (Get-TreeHash $tree)) 'the files that went through the pipe differ from the sources'

    $file = Join-Path $tree 'random.bin'
    $hashed = Invoke-CmdLine "`"$pakko`" h -si -scrcSHA256 < `"$file`""
    Assert-Smoke ($hashed.ExitCode -eq 0 -and $hashed.Output -match "(?i)^$(Get-Sha256 $file)\s") "h -si: exit $($hashed.ExitCode) $($hashed.Text)"

    # A console of its own, so that stdout is a terminal.
    $terminal = Start-Process -FilePath $pakko -ArgumentList @('a', '-so', 'x.zip', "`"$file`"") -WindowStyle Hidden -Wait -PassThru
    Assert-Smoke ($terminal.ExitCode -eq 7) "a -so with stdout on a terminal: exit $($terminal.ExitCode), expected 7"
}

Add-SmokeStep 'CLI-011' {
    $folder = Get-StepFolder 'cli011'
    $zip = Join-Path $folder 'marked.zip'
    $made = Invoke-Pakko @('a', $zip, (Get-Fixture Tree))
    Assert-Smoke ($made.ExitCode -eq 0) "a: exit $($made.ExitCode) $($made.Text)"
    Set-Content -LiteralPath $zip -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3"

    foreach ($case in @{ Switch = @(); Marked = $true }, @{ Switch = @('-snz0'); Marked = $false }) {
        $out = Join-Path $folder "out$($case.Switch)"
        $run = Invoke-Pakko (@('x', $zip, "-o$out") + $case.Switch)
        Assert-Smoke ($run.ExitCode -eq 0) "x $($case.Switch): exit $($run.ExitCode) $($run.Text)"
        $files = @(Get-ChildItem -LiteralPath $out -Recurse -File)
        $marked = @($files | Where-Object { Get-Item -LiteralPath $_.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue })
        Assert-Smoke ($files.Count -eq 4) "x $($case.Switch) wrote $($files.Count) files, expected 4"
        Assert-Smoke ($marked.Count -eq ($case.Marked ? 4 : 0)) "x $($case.Switch): $($marked.Count) of 4 files carry the download mark"
    }
}

Add-SmokeStep 'CLI-012' {
    if (-not (Test-Path -LiteralPath $Smoke.SevenZip)) { Skip-Smoke 'the vendored 7za.exe is not there' }
    $tree = Get-Fixture Tree
    $parent = Split-Path -Parent $tree
    $name = Split-Path -Leaf $tree

    foreach ($algorithm in 'CRC32', 'SHA256') {
        $ours = Invoke-Pakko @('h', "-scrc$algorithm", $name) -WorkingDirectory $parent
        $theirs = Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('h', "-scrc$algorithm", '-sccUTF-8', $name) -WorkingDirectory $parent
        Assert-Smoke ($ours.ExitCode -eq 0 -and $theirs.ExitCode -eq 0) "h -scrc$algorithm : pakko exit $($ours.ExitCode), 7za exit $($theirs.ExitCode)"
        foreach ($line in 'for data', 'for data and names') {
            $pattern = "$algorithm\s+$line`:\s+(\S+)"
            $mine = [regex]::Match($ours.Output, $pattern).Groups[1].Value
            $reference = [regex]::Match($theirs.Output, $pattern).Groups[1].Value
            Assert-Smoke ($mine -and $mine -eq $reference) "$algorithm $line`: pakko '$mine', 7za '$reference'"
        }
    }

    # An unreadable subfolder and a junction back to the top.
    $folder = Get-StepFolder 'cli012'
    $hostile = Join-Path $folder 'hostile'
    $closed = Join-Path $hostile 'closed'
    $null = [System.IO.Directory]::CreateDirectory($closed)
    [System.IO.File]::WriteAllText((Join-Path $hostile 'open.txt'), 'open')
    [System.IO.File]::WriteAllText((Join-Path $closed 'secret.txt'), 'closed')
    $null = New-Item -ItemType Junction -Path (Join-Path $hostile 'loop') -Target $hostile
    $icacls = Join-Path $env:SystemRoot 'System32\icacls.exe'
    $user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $null = Invoke-Tool -Exe $icacls -Arguments @($closed, '/deny', "${user}:(OI)(CI)(RX)")
    try {
        $run = Invoke-Pakko @('h', 'hostile') -WorkingDirectory $folder -TimeoutSeconds 60
        Assert-Smoke ($run.Output -match '(?m)^[0-9A-F]{8}\s+hostile\\open\.txt') "h did not hash the readable file: $($run.Text)"
        Assert-Smoke ($run.Text -match 'closed') "h does not name the unreadable folder: $($run.Text)"
        Assert-Smoke ($run.Text -notmatch 'loop\\loop') "h followed the junction: $($run.Text)"
        Assert-Smoke ($run.ExitCode -eq 1) "h with an unreadable folder: exit $($run.ExitCode), expected 1"
    }
    finally {
        $null = Invoke-Tool -Exe $icacls -Arguments @($closed, '/remove:d', $user)
        # The junction first: removing the tree through it would start on the tree itself.
        [System.IO.Directory]::Delete((Join-Path $hostile 'loop'))
    }
}

Add-SmokeStep 'CLI-018' {
    $folder = Get-StepFolder 'cli018'
    $old = [datetime]::new(2001, 9, 9, 1, 46, 40, [System.DateTimeKind]::Utc)

    $zip = Join-Path $folder 'old.zip'
    New-ZipFile -Path $zip -Entries @(@{ Name = 'dir/'; Modified = $old.ToLocalTime() }, @{ Name = 'dir/file.txt'; Content = 'x'; Modified = $old.ToLocalTime() })
    $tar = Join-Path $folder 'old.tar'
    New-TarFile -Path $tar -Entries @(@{ Name = 'dir/'; Folder = $true; Modified = $old }, @{ Name = 'dir/file.txt'; Content = 'x'; Modified = $old })
    foreach ($archive in $zip, $tar) {
        $out = Join-Path $folder ((Split-Path -Leaf $archive) + '.out')
        $run = Invoke-Pakko @('x', $archive, "-o$out")
        Assert-Smoke ($run.ExitCode -eq 0) "x $(Split-Path -Leaf $archive): exit $($run.ExitCode) $($run.Text)"
        foreach ($item in 'dir\file.txt', 'dir') {
            $written = (Get-Item -LiteralPath (Join-Path $out $item)).LastWriteTimeUtc
            # A ZIP keeps two-second steps of local time.
            Assert-Smoke ([System.Math]::Abs(($written - $old).TotalSeconds) -le 2) "$(Split-Path -Leaf $archive): $item is dated $($written.ToString('s')), the archive says $($old.ToString('s'))"
        }
    }

    $here = Join-Path $folder 'here'
    $null = [System.IO.Directory]::CreateDirectory($here)
    $run = Invoke-Pakko @('x', $zip) -WorkingDirectory $here
    Assert-Smoke ($run.ExitCode -eq 0 -and (Test-Path -LiteralPath (Join-Path $here 'dir\file.txt'))) "x with no -o did not extract into the current folder: exit $($run.ExitCode) $($run.Text)"

    $random = Join-Path $folder 'random.bin'
    Write-RandomFile -Path $random -Length 32MB -Seed 18
    foreach ($level in 1, 9) {
        $archive = Join-Path $folder "mx$level.zip"
        $run = Invoke-Pakko @('a', "-mx=$level", $archive, $random)
        Assert-Smoke ($run.ExitCode -eq 0) "a -mx=$level : exit $($run.ExitCode) $($run.Text)"
        $size = (Get-Item -LiteralPath $archive).Length
        Assert-Smoke ($size -ge 32MB -and $size -le 32MB + 64KB) "a -mx=$level on 32 MiB of random bytes wrote $size bytes"
    }
}

Add-SmokeStep 'CLI-009' {
    $folder = Get-StepFolder 'cli009'
    $tree = Get-Fixture Tree
    $pakko = $Smoke.Pakko
    $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $shells = [ordered]@{
        'cmd'  = { param($line) Invoke-CmdLine $line }
        'pwsh' = { param($line) Invoke-Tool -Exe (Get-Process -Id $PID).Path -Arguments @('-NoProfile', '-Command', "& $line; exit `$LASTEXITCODE") }
        '5.1'  = { param($line) Invoke-Tool -Exe $windowsPowerShell -Arguments @('-NoProfile', '-Command', "& $line; exit `$LASTEXITCODE") }
    }

    foreach ($shell in $shells.Keys) {
        $quote = $shell -eq 'cmd' ? '"' : "'"
        $zip = "$quote$(Join-Path $folder "$shell.zip")$quote"
        $source = "$quote$tree$quote"
        $lines = [ordered]@{
            '-v'                              = 0
            "a $zip $source"                  = 0
            "t $zip"                          = 0
            "t $quote$folder\none.zip$quote"  = 2
            "t $zip -zz"                      = 7
        }
        foreach ($line in $lines.Keys) {
            $run = & $shells[$shell] "$quote$pakko$quote $line"
            Assert-Smoke ($run.ExitCode -eq $lines[$line]) "$shell`: pakko $line => exit $($run.ExitCode), expected $($lines[$line]): $($run.Text)"
        }

        # PowerShell passes -ttar.gz as "-ttar" and ".gz"; cmd passes it whole.
        $tarGz = Join-Path $folder "$shell.tar.gz"
        $run = & $shells[$shell] "$quote$pakko$quote a -ttar.gz $quote$tarGz$quote $source"
        if ($shell -eq 'cmd') {
            Assert-Smoke ($run.ExitCode -eq 0 -and (Test-Path -LiteralPath $tarGz)) "cmd: a -ttar.gz => exit $($run.ExitCode) $($run.Text)"
        }
        else {
            Assert-Smoke ($run.ExitCode -eq 7 -and $run.Text.Contains("'-ttar.gz'")) "$shell`: the refusal of a split -ttar.gz does not say how to write it: exit $($run.ExitCode) $($run.Text)"
            Assert-Smoke (-not (Test-Path -LiteralPath $tarGz)) "$shell`: a split -ttar.gz still wrote an archive"
        }
    }
}

Add-SmokeStep 'CLI-010' {
    $folder = Get-StepFolder 'cli010'
    $tree = Get-Fixture Tree
    $name = Split-Path -Leaf $tree
    $zip = Join-Path $folder 'names.zip'
    $made = Invoke-Pakko @('a', $zip, $tree)
    Assert-Smoke ($made.ExitCode -eq 0) "a: exit $($made.ExitCode) $($made.Text)"

    # What the bytes of the listing must decode with, for each console code page and switch.
    $expected = @{
        '866 '          = 'cp866';   '866 -sccUTF-8'   = 'utf-8'; '866 -sccWIN'   = 'windows-1251'; '866 -sccDOS'   = 'cp866'
        '65001 '        = 'utf-8';   '65001 -sccUTF-8' = 'utf-8'; '65001 -sccWIN' = 'windows-1251'; '65001 -sccDOS' = 'cp866'
    }
    foreach ($case in $expected.Keys) {
        $codePage, $encodingSwitch = $case -split ' '
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $env:SystemRoot 'System32\cmd.exe'))
        $startInfo.Arguments = "/d /s /c `"chcp $codePage >nul & `"$($Smoke.Pakko)`" l $encodingSwitch `"$zip`"`""
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $process = [System.Diagnostics.Process]::Start($startInfo)
        $bytes = [System.IO.MemoryStream]::new()
        try {
            $process.StandardOutput.BaseStream.CopyTo($bytes)
            $process.WaitForExit()
            Assert-Smoke ($process.ExitCode -eq 0) "chcp $case`: exit $($process.ExitCode)"
            $text = [System.Text.Encoding]::GetEncoding($expected[$case]).GetString($bytes.ToArray())
            Assert-Smoke ($text.Contains("$name/sub/$name.txt")) "chcp $case`: the listing is not $($expected[$case])"
        }
        finally {
            $bytes.Dispose()
            $process.Dispose()
        }
    }
}

Add-SmokeStep 'CLI-014' {
    $folder = Get-StepFolder 'cli014'

    # Names that leave the destination.
    $slip = Join-Path $folder 'slip.zip'
    New-ZipFile -Path $slip -Entries @(@{ Name = '../../evil.txt'; Content = 'e' }, @{ Name = 'ok.txt'; Content = 'o' }, @{ Name = 'C:/abs.txt'; Content = 'a' })
    $out = Join-Path $folder 'slip\a\b'
    $run = Invoke-Pakko @('x', $slip, "-o$out")
    Assert-Smoke ($run.ExitCode -eq 2 -and $run.Text -match "'\.\./\.\./evil\.txt'" -and $run.Text -match "'C:/abs\.txt'") "x slip.zip: exit $($run.ExitCode) $($run.Text)"
    $written = @(Get-ChildItem -LiteralPath (Join-Path $folder 'slip') -Recurse -File -Name)
    Assert-Smoke ($written.Count -eq 1 -and $written[0] -eq 'a\b\ok.txt') "x slip.zip wrote: $($written -join ', ')"

    # A bomb: 1 GiB of zeros in about a megabyte.
    $bomb = Join-Path $folder 'bomb.zip'
    $archive = [System.IO.Compression.ZipFile]::Open($bomb, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $stream = $archive.CreateEntry('zeros.bin', [System.IO.Compression.CompressionLevel]::SmallestSize).Open()
        try {
            $block = [byte[]]::new(1MB)
            foreach ($i in 1..1024) { $stream.Write($block, 0, $block.Length) }
        }
        finally { $stream.Dispose() }
    }
    finally { $archive.Dispose() }
    $out = Join-Path $folder 'bomb'
    $run = Invoke-Pakko @('x', $bomb, "-o$out")
    Assert-Smoke ($run.ExitCode -eq 1 -and $run.Text -match 'bomb\.zip.*\d+:1') "x bomb.zip with no one to ask: exit $($run.ExitCode) $($run.Text)"
    Assert-Smoke (-not (Test-Path -LiteralPath (Join-Path $out 'zeros.bin'))) 'x bomb.zip wrote the entry it declined'

    # A local header that names another file than the central directory does.
    $mismatch = Join-Path $folder 'mismatch.zip'
    New-ZipFile -Path $mismatch -Entries @(@{ Name = 'good.txt'; Content = 'hello hello hello' })
    $bytes = [System.IO.File]::ReadAllBytes($mismatch)
    [System.Text.Encoding]::ASCII.GetBytes('evil').CopyTo($bytes, 30)
    [System.IO.File]::WriteAllBytes($mismatch, $bytes)
    $out = Join-Path $folder 'mismatch'
    $run = Invoke-Pakko @('x', $mismatch, "-o$out")
    Assert-Smoke ($run.ExitCode -eq 1 -and $run.Text -match "mismatch\.zip.*: 1 \(first: 'good\.txt'\)") "x mismatch.zip: exit $($run.ExitCode) $($run.Text)"
    Assert-Smoke ((Get-ChildItem -LiteralPath $out -Name) -join ',' -eq 'good.txt') 'x mismatch.zip did not use the central directory''s name'
    $run = Invoke-Pakko @('t', $mismatch)
    Assert-Smoke ($run.ExitCode -eq 2 -and $run.Text -match "'good\.txt'") "t mismatch.zip: exit $($run.ExitCode) $($run.Text)"

    # A name of 20,000 folders.
    $deep = Join-Path $folder 'deep.zip'
    New-ZipFile -Path $deep -Entries @(@{ Name = ('a/' * 20000) + 'f.txt'; Content = 'd' }, @{ Name = 'ok.txt'; Content = 'o' })
    $out = Join-Path $folder 'deep'
    $run = Invoke-Pakko @('x', $deep, "-o$out") -TimeoutSeconds 60
    Assert-Smoke ($run.ExitCode -eq 2 -and $run.Text.Length -lt 4000) "x deep.zip: exit $($run.ExitCode), $($run.Text.Length) characters of output"
    Assert-Smoke ((Get-ChildItem -LiteralPath $out -Name) -join ',' -eq 'ok.txt') 'x deep.zip did not extract the ordinary entry next to the deep one'
}

Add-SmokeStep 'CLI-015' {
    $folder = Get-StepFolder 'cli015'
    $tree = Get-Fixture Tree
    $name = Split-Path -Leaf $tree
    $zip = Join-Path $folder 'tree.zip'
    $made = Invoke-Pakko @('a', $zip, $tree)
    Assert-Smoke ($made.ExitCode -eq 0) "a: exit $($made.ExitCode) $($made.Text)"

    # A folder the user may not write to.
    $closed = Join-Path $folder 'closed'
    $null = [System.IO.Directory]::CreateDirectory($closed)
    $icacls = Join-Path $env:SystemRoot 'System32\icacls.exe'
    $user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $null = Invoke-Tool -Exe $icacls -Arguments @($closed, '/deny', "${user}:(OI)(CI)(W)")
    try {
        $run = Invoke-Pakko @('x', $zip, "-o$closed")
        Assert-Smoke ($run.ExitCode -eq 2 -and $run.Text -match 'tree\.zip') "x into a folder closed for writing: exit $($run.ExitCode) $($run.Text)"
    }
    finally { $null = Invoke-Tool -Exe $icacls -Arguments @($closed, '/remove:d', $user) }
    Assert-Smoke (@(Get-ChildItem -LiteralPath $closed -Force).Count -eq 0) 'x left something in the folder it could not write to'

    # A destination longer than 260 characters.
    $long = Join-Path $folder ((('L' * 50 + '\') * 6).TrimEnd('\'))
    $run = Invoke-Pakko @('x', $zip, "-o$long")
    Assert-Smoke ($run.ExitCode -eq 0) "x into a path of $($long.Length) characters: exit $($run.ExitCode) $($run.Text)"
    Assert-Smoke ((Get-TreeHash (Join-Path "\\?\$long" $name)) -eq (Get-TreeHash $tree)) 'the files under the long path differ from the sources'

    # Over a file another process holds open.
    $held = Join-Path $folder 'held'
    $null = Invoke-Pakko @('x', $zip, "-o$held")
    $lock = [System.IO.File]::Open((Join-Path $held "$name\text.txt"), [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
    try {
        Remove-Item -LiteralPath (Join-Path $held "$name\random.bin")
        $run = Invoke-Pakko @('x', '-aoa', $zip, "-o$held")
        Assert-Smoke ($run.ExitCode -eq 2 -and $run.Text -match 'text\.txt') "x -aoa over a held file: exit $($run.ExitCode) $($run.Text)"
        Assert-Smoke (@($run.Text -split '\r?\n' | Where-Object { $_.Trim() }).Count -eq 1) "x -aoa over a held file says more than the one error: $($run.Text)"
    }
    finally { $lock.Dispose() }
    Assert-Smoke ((Get-TreeHash (Join-Path $held $name)) -eq (Get-TreeHash $tree)) 'after x -aoa over a held file the other files are not the archive''s'
    Assert-Smoke (@(Get-ChildItem -LiteralPath $held -Recurse -Force -File).Count -eq 4) 'x -aoa over a held file left a partial file'
}

Add-SmokeStep 'CLI-021' {
    $folder = Get-StepFolder 'cli021'
    $tree = Get-Fixture Tree
    $fixtures = Join-Path $Smoke.Repo 'tests\Archiver.Core.IntegrationTests\Fixtures'
    $text = Join-Path $tree 'text.txt'

    # A CRC error into an empty folder: nothing was kept, so nothing to hint at.
    $damaged = Join-Path $folder 'crc.zip'
    $made = Invoke-Pakko @('a', '-mx=0', $damaged, (Join-Path $tree 'random.bin'), $text)
    Assert-Smoke ($made.ExitCode -eq 0) "a: exit $($made.ExitCode) $($made.Text)"
    Set-DamagedRange -Path $damaged -Offset 100KB -Count 16
    $run = Invoke-Pakko @('x', $damaged, "-o$(Join-Path $folder 'crc')")
    Assert-Smoke ($run.ExitCode -eq 2 -and $run.Text -match 'CRC-32') "x on a CRC error: exit $($run.ExitCode) $($run.Text)"
    Assert-Smoke ($run.Text -notmatch '-ao') "x on a CRC error hints at -ao: $($run.Text)"

    # An encrypted 7z: no password would help, so none is asked for.
    $encrypted = Join-Path $fixtures 'encrypted.7z'
    $run = Invoke-Pakko @('x', $encrypted, "-o$(Join-Path $folder 'e7')")
    Assert-Smoke ($run.ExitCode -eq 2 -and $run.Text -notmatch '(^|\s)-p') "x on an encrypted 7z: exit $($run.ExitCode) $($run.Text)"
    $run = Invoke-Pakko @('t', $encrypted)
    Assert-Smoke ($run.ExitCode -eq 1 -and $run.Text -notmatch '(^|\s)-p') "t on an encrypted 7z: exit $($run.ExitCode) $($run.Text)"

    # One password for two archives; it fits the first.
    $first = Join-Path $folder 'first.zip'
    $second = Join-Path $folder 'second.zip'
    $null = Invoke-Pakko @('a', $first, '-pFirst-1', $text)
    $null = Invoke-Pakko @('a', $second, '-pSecond-2', $text)
    foreach ($arguments in @('x', $first, $second, '-pFirst-1', "-o$(Join-Path $folder 'two')"), @('t', $first, $second, '-pFirst-1')) {
        $run = Invoke-Pakko $arguments
        $lines = @($run.Text -split '\r?\n' | Where-Object { $_.Trim() })
        Assert-Smoke ($run.ExitCode -eq 2 -and $lines.Count -eq 1 -and $lines[0] -match 'second\.zip') "$($arguments[0]) with a password that fits the first archive only: exit $($run.ExitCode) $($run.Text)"
    }

    # All, some and none; an archive of one entry is not "1 entries".
    $one = Join-Path $folder 'one.zip'
    $null = Invoke-Pakko @('a', $one, $text)
    $zip = Join-Path $folder 'tree.zip'
    $null = Invoke-Pakko @('a', $zip, $tree)
    $out = Join-Path $folder 'out'
    $all = Invoke-Pakko @('x', $zip, $one, "-o$out")
    Assert-Smoke ($all.ExitCode -eq 0 -and -not $all.Text.Trim()) "x of everything: exit $($all.ExitCode) $($all.Text)"
    Remove-Item -LiteralPath (Join-Path $out "$(Split-Path -Leaf $tree)\random.bin")
    $some = Invoke-Pakko @('x', $zip, $one, "-o$out")
    Assert-Smoke ($some.ExitCode -eq 1) "x where some files exist: exit $($some.ExitCode) $($some.Text)"
    $none = Invoke-Pakko @('x', (Join-Path $folder 'none.zip'), "-o$out")
    Assert-Smoke ($none.ExitCode -eq 2) "x of a missing archive: exit $($none.ExitCode) $($none.Text)"
    foreach ($run in $some, $none, (Invoke-Pakko @('t', $one)), (Invoke-Pakko @('l', $one))) {
        Assert-Smoke ($run.Text -notmatch '\b1 (entries|files|archives|errors)\b') "a count of one reads as a plural: $($run.Text)"
    }
}

Add-SmokeStep 'CLI-008' {
    $folder = Get-StepFolder 'cli008'
    $sources = @((Get-Fixture Many), (Get-Fixture Big))
    $signal = Send-CtrlC -Prepare

    # About ten seconds of work at -mx=9; the signal goes as soon as the first file is there.
    $process = Start-PakkoInConsole (@('a', '-mx=9', (Join-Path $folder 'long.zip')) + $sources)
    try {
        Wait-Smoke { @(Get-ChildItem -LiteralPath $folder -Force).Count -gt 0 } 'the first file of pakko a'
        Send-CtrlC -Signal $signal -Process $process
        Assert-Smoke ($process.WaitForExit(30000)) 'pakko a did not end within 30 s of Ctrl+C'
        Assert-Smoke ($process.ExitCode -eq 255) "pakko a after Ctrl+C: exit $($process.ExitCode), expected 255"
    }
    finally {
        if (-not $process.HasExited) { $process.Kill() }
        $process.Dispose()
    }
    $left = @(Get-ChildItem -LiteralPath $folder -Force -Name)
    Assert-Smoke ($left.Count -eq 0) "pakko a after Ctrl+C left: $($left -join ', ')"
}

Add-SmokeStep 'CLI-016' {
    $folder = Get-StepFolder 'cli016'
    $many = Get-Fixture Many

    $first = Start-PakkoInConsole @('a', (Join-Path $folder 'one.zip'), $many)
    $second = Start-PakkoInConsole @('a', (Join-Path $folder 'two.zip'), $many)
    try {
        Assert-Smoke ($first.WaitForExit(120000) -and $second.WaitForExit(120000)) 'two pakko a at once did not end in two minutes'
        Assert-Smoke ($first.ExitCode -eq 0 -and $second.ExitCode -eq 0) "two pakko a at once: exit $($first.ExitCode) and $($second.ExitCode)"
    }
    finally {
        $first.Dispose()
        $second.Dispose()
    }
    foreach ($name in 'one.zip', 'two.zip') {
        $tested = Invoke-Pakko @('t', (Join-Path $folder $name))
        Assert-Smoke ($tested.ExitCode -eq 0) "t $name after two runs at once: exit $($tested.ExitCode) $($tested.Text)"
    }
    $hashes = @{ one = Get-Sha256 (Join-Path $folder 'one.zip'); two = Get-Sha256 (Join-Path $folder 'two.zip') }

    # A small archive after the kill: every creation sweeps the killed run's temporary file and
    # its chunk folder, not only a run that makes chunks itself (T-F379).
    $long = @('a', '-mx=9', (Join-Path $folder 'killed.zip'), $many, (Get-Fixture Big))
    $killed = Start-PakkoInConsole $long
    try {
        Wait-Smoke { @(Get-ChildItem -LiteralPath $folder -Force | Where-Object Name -NotLike '*.zip').Count -gt 0 } 'the first file of the third pakko a'
        $killed.Kill()
        $null = $killed.WaitForExit(10000)
    }
    finally { $killed.Dispose() }
    Assert-Smoke (@(Get-ChildItem -LiteralPath $folder -Force | Where-Object Name -NotLike '*.zip').Count -gt 0) 'the killed pakko a left nothing, so the next check would prove nothing'

    $again = Invoke-Pakko @('a', (Join-Path $folder 'small.zip'), (Get-Fixture Tree))
    Assert-Smoke ($again.ExitCode -eq 0) "a after a killed run: exit $($again.ExitCode) $($again.Text)"
    $left = @(Get-ChildItem -LiteralPath $folder -Force -Name | Sort-Object)
    Assert-Smoke (($left -join ',') -eq 'one.zip,small.zip,two.zip') "after the small run the folder holds: $($left -join ', ')"
    Assert-Smoke ((Get-Sha256 (Join-Path $folder 'one.zip')) -eq $hashes.one -and (Get-Sha256 (Join-Path $folder 'two.zip')) -eq $hashes.two) 'the small run changed another run''s archive'
}

Add-SmokeStep 'CLI-019' {
    $folder = Get-StepFolder 'cli019'
    $started = Get-Date

    # 300 files named one by one on the command line.
    $source = Join-Path $folder 'three'
    $null = [System.IO.Directory]::CreateDirectory($source)
    $files = foreach ($i in 1..300) {
        $path = Join-Path $source ('file-{0:d3}.txt' -f $i)
        [System.IO.File]::WriteAllText($path, "$i")
        $path
    }
    $tar = Join-Path $folder 'three.tar'
    $run = Invoke-Pakko (@('a', '-ttar', $tar) + $files)
    Assert-Smoke ($run.ExitCode -eq 0) "a -ttar with 300 files: exit $($run.ExitCode) $($run.Text)"
    $listed = Invoke-Pakko @('l', $tar)
    $count = [regex]::Matches($listed.Output, 'file-\d{3}\.txt').Count
    Assert-Smoke ($count -eq 300) "the tar of 300 files lists $count"

    # Names that tar.exe would read as options or as a list file.
    $odd = Join-Path $folder 'odd'
    $null = [System.IO.Directory]::CreateDirectory($odd)
    $names = '-C', '-T', '--exclude=x', '@list', 'plain.txt'
    foreach ($name in $names) { [System.IO.File]::WriteAllText((Join-Path $odd $name), $name) }
    $oddTar = Join-Path $folder 'odd.tar'
    $run = Invoke-Pakko @('a', '-ttar', $oddTar, $odd)
    Assert-Smoke ($run.ExitCode -eq 0) "a -ttar on names like options: exit $($run.ExitCode) $($run.Text)"
    $out = Join-Path $folder 'odd.out'
    $run = Invoke-Pakko @('x', $oddTar, "-o$out")
    Assert-Smoke ($run.ExitCode -eq 0) "x of the tar with names like options: exit $($run.ExitCode) $($run.Text)"
    foreach ($name in $names) {
        $written = Join-Path $out "odd\$name"
        Assert-Smoke ((Test-Path -LiteralPath $written) -and [System.IO.File]::ReadAllText($written) -eq $name) "the file named $name did not come back as a file"
    }

    # The same files named one by one: now each name is an argument on tar.exe's own command line.
    $namedTar = Join-Path $folder 'named.tar'
    $run = Invoke-Pakko (@('a', '-ttar', $namedTar) + ($names | ForEach-Object { Join-Path $odd $_ }))
    Assert-Smoke ($run.ExitCode -eq 0) "a -ttar on files named like options: exit $($run.ExitCode) $($run.Text)"
    $out = Join-Path $folder 'named.out'
    $run = Invoke-Pakko @('x', $namedTar, "-o$out")
    Assert-Smoke ($run.ExitCode -eq 0) "x of the tar of files named like options: exit $($run.ExitCode) $($run.Text)"
    $written = @(Get-ChildItem -LiteralPath $out -Recurse -File)
    Assert-Smoke ($written.Count -eq $names.Count) "the tar of $($names.Count) named files gave back $($written.Count): $($written.Name -join ', ')"
    foreach ($name in $names) {
        $file = @($written | Where-Object Name -eq $name)
        Assert-Smoke ($file.Count -eq 1 -and [System.IO.File]::ReadAllText($file[0].FullName) -eq $name) "the file named $name, given by name, did not come back as a file"
    }

    # A name outside the ANSI code page is refused before tar.exe sees it.
    $quoted = Join-Path $folder 'quoted'
    $null = [System.IO.Directory]::CreateDirectory($quoted)
    $fullwidth = [string][char]0xFF02
    [System.IO.File]::WriteAllText((Join-Path $quoted "q${fullwidth}uoted${fullwidth}.txt"), 'q')
    $quotedTar = Join-Path $folder 'quoted.tar'
    $run = Invoke-Pakko @('a', '-ttar', $quotedTar, $quoted)
    Assert-Smoke ($run.ExitCode -eq 2 -and -not (Test-Path -LiteralPath $quotedTar)) "a -ttar on a name with fullwidth quotes: exit $($run.ExitCode) $($run.Text)"

    # Two entries of one name.
    $twice = Join-Path $folder 'twice.tar'
    New-TarFile -Path $twice -Entries @(@{ Name = 'same.txt'; Content = 'first' }, @{ Name = 'same.txt'; Content = 'second' })
    $expected = [ordered]@{ '-aou' = 'same (1).txt=second,same.txt=first'; '-aos' = 'same.txt=first'; '-aoa' = 'same.txt=second' }
    foreach ($mode in $expected.Keys) {
        $out = Join-Path $folder "twice$mode"
        $null = Invoke-Pakko @('x', $twice, "-o$out", $mode)
        $got = (Get-ChildItem -LiteralPath $out -File | Sort-Object Name | ForEach-Object { "$($_.Name)=$([System.IO.File]::ReadAllText($_.FullName))" }) -join ','
        Assert-Smoke ($got -eq $expected[$mode]) "x $mode on two entries of one name wrote: $got"
    }

    # A read-only archive is staged for the sandbox; the copy must not stay, nor the attribute go.
    (Get-Item -LiteralPath $tar).IsReadOnly = $true
    try {
        $out = Join-Path $folder 'readonly.out'
        $run = Invoke-Pakko @('x', $tar, "-o$out")
        Assert-Smoke ($run.ExitCode -eq 0 -and @(Get-ChildItem -LiteralPath $out -File).Count -eq 300) "x of a read-only tar: exit $($run.ExitCode) $($run.Text)"
        Assert-Smoke (Get-Item -LiteralPath $tar).IsReadOnly 'x cleared the read-only attribute of the archive'
    }
    finally { (Get-Item -LiteralPath $tar).IsReadOnly = $false }

    $left = @(Get-TarLeftover -Since $started)
    Assert-Smoke ($left.Count -eq 0) "left in %TEMP%: $(($left | Select-Object -First 3 -ExpandProperty Name) -join ', ')"
}

Add-SmokeStep 'CLI-020' {
    $folder = Get-StepFolder 'cli020'
    $cyrillic = Split-Path -Leaf (Get-Fixture Tree)
    $dos = [System.Text.Encoding]::GetEncoding(866)
    $old = [datetime]::new(2020, 2, 3, 4, 5, 0, [System.DateTimeKind]::Utc)

    $tar = Join-Path $folder 'dos.tar'
    New-TarFile -Path $tar -Entries @(
        @{ Name = $dos.GetBytes("$cyrillic/one.txt"); Content = '1'; Modified = $old }
        @{ Name = $dos.GetBytes("$cyrillic/two.txt"); Content = '22'; Modified = $old }
        @{ Name = 'ascii.txt'; Content = 'a'; Modified = $old }
    )
    $tarGz = Join-Path $folder 'dos.tar.gz'
    $source = [System.IO.File]::OpenRead($tar)
    try {
        $packed = [System.IO.Compression.GZipStream]::new([System.IO.File]::Create($tarGz), [System.IO.Compression.CompressionMode]::Compress)
        try { $source.CopyTo($packed) }
        finally { $packed.Dispose() }
    }
    finally { $source.Dispose() }

    foreach ($archive in $tar, $tarGz) {
        $listed = Invoke-Pakko @('l', '-sccUTF-8', $archive)
        Assert-Smoke ($listed.ExitCode -eq 0) "l $(Split-Path -Leaf $archive): exit $($listed.ExitCode) $($listed.Text)"
        $rows = @($listed.Output -split '\r?\n' | Where-Object { $_ -match '\tf\t' })
        Assert-Smoke ($rows.Count -eq 3) "l $(Split-Path -Leaf $archive) shows $($rows.Count) of 3 entries"
        foreach ($name in "$cyrillic/one.txt", "$cyrillic/two.txt", 'ascii.txt') {
            $row = @($rows | Where-Object { $_.EndsWith("`t$name") })
            Assert-Smoke ($row.Count -eq 1) "l $(Split-Path -Leaf $archive) does not show $name"
            # Size, "-" for the packed size and the CRC, and the entry's own date (tar.exe prints
            # no time of day for an entry this old).
            $local = $old.ToLocalTime().ToString('yyyy-MM-dd')
            Assert-Smoke ($row[0] -match "^\d+\t-\t-\t$local") "l $(Split-Path -Leaf $archive): $($row[0])"
        }
    }

    $zip = Join-Path $folder 'dos.zip'
    $archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create, $dos)
    try {
        foreach ($name in "$cyrillic/one.txt", 'ascii.txt') {
            $writer = [System.IO.StreamWriter]::new($archive.CreateEntry($name).Open())
            try { $writer.Write('z') }
            finally { $writer.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    $listed = Invoke-Pakko @('l', '-sccUTF-8', $zip)
    Assert-Smoke ($listed.ExitCode -eq 0 -and $listed.Output.Contains("`t$cyrillic/one.txt") -and $listed.Output.Contains("`tascii.txt")) "l of a ZIP with cp866 names: exit $($listed.ExitCode) $($listed.Text)"
}
