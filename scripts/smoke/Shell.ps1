#Requires -Version 7.0
# Steps of docs/SMOKE.md's SHL and OPW rows: Archiver.Shell.exe started the way Explorer starts it.
# Dot-sourced by scripts/Invoke-Smoke.ps1.

# Three ZIPs in a fresh folder: one root folder, several root items, one root file.
function Get-ShapedZip {
    param([Parameter(Mandatory)][string]$Folder)
    $tree = Get-Fixture Tree
    $one = Join-Path $Folder 'one.txt'
    $two = Join-Path $Folder 'two.txt'
    [System.IO.File]::WriteAllText($one, 'one')
    [System.IO.File]::WriteAllText($two, 'two')
    $shapes = @{
        Folder = Join-Path $Folder 'folder.zip'
        Several = Join-Path $Folder 'several.zip'
        File = Join-Path $Folder 'file.zip'
    }
    foreach ($made in @(
            (Invoke-Pakko @('a', $shapes.Folder, $tree)),
            (Invoke-Pakko @('a', $shapes.Several, $one, $two)),
            (Invoke-Pakko @('a', $shapes.File, $one)))) {
        Assert-Smoke ($made.ExitCode -eq 0) "fixture ZIP: exit $($made.ExitCode) $($made.Text)"
    }
    Remove-Item -LiteralPath $one, $two
    $shapes
}

Add-SmokeStep 'SHL-001' {
    $folder = Get-StepFolder 'shl001'
    $zips = Get-ShapedZip $folder
    foreach ($zip in $zips.Values) {
        $run = Invoke-Shell -Arguments @('--extract-here', $zip) -TimeoutSeconds 30
        Assert-Smoke ($run.Exited -and $run.ExitCode -eq 0) "--extract-here $(Split-Path -Leaf $zip): exited=$($run.Exited) code=$($run.ExitCode) $($run.Text)"
    }

    Assert-Smoke (Test-Path -LiteralPath (Join-Path $folder 'folder\text.txt')) 'one root folder: its content is not in a folder named after the archive'
    $doubled = Join-Path (Join-Path $folder 'folder') (Split-Path -Leaf (Get-Fixture Tree))
    Assert-Smoke (-not (Test-Path -LiteralPath $doubled)) 'one root folder: the folder is doubled'
    Assert-Smoke ((Test-Path -LiteralPath (Join-Path $folder 'several\one.txt')) -and (Test-Path -LiteralPath (Join-Path $folder 'several\two.txt'))) 'several root items are not wrapped in a folder named after the archive'
    Assert-Smoke (Test-Path -LiteralPath (Join-Path $folder 'one.txt')) 'one root file did not land next to the archive'
}

Add-SmokeStep 'SHL-002' {
    $flat = Get-StepFolder 'shl002flat'
    $zips = Get-ShapedZip $flat
    $run = Invoke-Shell -Arguments @('--extract-flat', $zips.Several) -TimeoutSeconds 30
    Assert-Smoke ($run.Exited -and $run.ExitCode -eq 0) "--extract-flat: exited=$($run.Exited) code=$($run.ExitCode) $($run.Text)"
    Assert-Smoke ((Test-Path -LiteralPath (Join-Path $flat 'one.txt')) -and (Test-Path -LiteralPath (Join-Path $flat 'two.txt'))) '--extract-flat did not put the items straight into the folder'

    $named = Get-StepFolder 'shl002folder'
    $zips = Get-ShapedZip $named
    $run = Invoke-Shell -Arguments @('--extract-folder', $zips.File) -TimeoutSeconds 30
    Assert-Smoke ($run.Exited -and $run.ExitCode -eq 0) "--extract-folder: exited=$($run.Exited) code=$($run.ExitCode) $($run.Text)"
    Assert-Smoke (Test-Path -LiteralPath (Join-Path $named 'file\one.txt')) '--extract-folder did not make a folder named after the archive'
}

Add-SmokeStep 'SHL-003' {
    $folder = Get-StepFolder 'shl003'
    $name = Split-Path -Leaf (Get-Fixture Tree)
    $source = Join-Path $folder $name
    Copy-Item -LiteralPath (Get-Fixture Tree) -Destination $source -Recurse

    $run = Invoke-Shell -Arguments @('--archive', $source) -TimeoutSeconds 30
    Assert-Smoke ($run.Exited -and $run.ExitCode -eq 0) "--archive: exited=$($run.Exited) code=$($run.ExitCode) $($run.Text)"
    $zip = Join-Path $folder "$name.zip"
    Assert-Smoke (Test-Path -LiteralPath $zip) '--archive made no <name>.zip next to the source'
    $tested = Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('t', $zip)
    Assert-Smoke ($tested.ExitCode -eq 0) "7za t on the ZIP: exit $($tested.ExitCode)"
    $listed = Invoke-Pakko @('l', '-sccUTF-8', $zip)
    Assert-Smoke ($listed.Output.Contains("$name/sub/empty.dat")) "the entries lost their folder prefix: $($listed.Output)"

    $run = Invoke-Shell -Arguments @('--archive', '--format', 'tar', $source) -TimeoutSeconds 30
    Assert-Smoke ($run.Exited -and $run.ExitCode -eq 0) "--archive --format tar: exited=$($run.Exited) code=$($run.ExitCode) $($run.Text)"
    $tar = Join-Path $folder "$name.tar"
    Assert-Smoke (Test-Path -LiteralPath $tar) '--archive --format tar made no <name>.tar next to the source'
    $entries = Invoke-Tool -Exe $Smoke.Tar -Arguments @('-tf', $tar)
    Assert-Smoke ($entries.ExitCode -eq 0 -and @($entries.Output -split '\r?\n' | Where-Object { $_ -match 'empty\.dat$' }).Count -eq 1) "tar -tf does not list the tar's entries: $($entries.Text)"
}

Add-SmokeStep 'SHL-004' {
    $folder = Get-StepFolder 'shl004'
    $source = Join-Path $folder 'payload.bin'
    Write-RandomFile -Path $source -Length 400KB -Seed 9
    $good = Join-Path $folder 'good.zip'
    $bad = Join-Path $folder 'bad.zip'
    $null = Invoke-Pakko @('a', '-mx=0', $good, $source)
    Copy-Item -LiteralPath $good -Destination $bad
    Set-DamagedRange -Path $bad -Offset 100KB -Count 16

    # The result text is in the user's language: a clean result has no CRC value in it.
    $clean = Invoke-Shell -Arguments @('--test', $good) -Until 'good\.zip.*\|.*\|' -TimeoutSeconds 30
    Assert-Smoke $clean.Matched "--test on a good ZIP showed no result window: $($clean.Text)"
    Assert-Smoke ($clean.Text -notmatch '[0-9A-F]{8}\b.*[0-9A-F]{8}\b') "--test on a good ZIP reports CRC values: $($clean.Text)"

    $broken = Invoke-Shell -Arguments @('--test', $bad) -Until 'payload\.bin.*[0-9A-F]{8}.*[0-9A-F]{8}' -TimeoutSeconds 30
    Assert-Smoke $broken.Matched "--test on a damaged ZIP does not name the entry and both CRC values: $($broken.Text)"
}

Add-SmokeStep 'SHL-005' {
    $folder = Get-StepFolder 'shl005'
    $file = Join-Path $folder 'payload.bin'
    Write-RandomFile -Path $file -Length 300KB -Seed 11

    $sha = Get-Sha256 $file
    $run = Invoke-Shell -Arguments @('--hash', '--algorithm', 'sha256', $file) -Until '[0-9A-Fa-f]{64}' -TimeoutSeconds 30
    Assert-Smoke ($run.Matched -and $run.Text -match $sha) "--hash sha256 does not show $sha : $($run.Text)"

    $reference = Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('h', '-scrcCRC32', $file)
    $crc = [regex]::Match($reference.Output, '(?m)^([0-9A-F]{8})\s').Groups[1].Value
    Assert-Smoke $crc "7za h printed no CRC-32: $($reference.Output)"
    $run = Invoke-Shell -Arguments @('--hash', '--algorithm', 'crc32', $file) -Until 'payload\.bin: [0-9A-Fa-f]{8}\b' -TimeoutSeconds 30
    Assert-Smoke ($run.Matched -and $run.Text -match $crc) "--hash crc32 does not show $crc : $($run.Text)"
}

Add-SmokeStep 'SHL-006' {
    $folder = Get-StepFolder 'shl006'
    $source = Join-Path $folder 'pick'
    $null = [System.IO.Directory]::CreateDirectory($source)
    foreach ($i in 0..999) { [System.IO.File]::WriteAllText((Join-Path $source ('p{0:d4}.txt' -f $i)), "p$i") }

    # Explorer's transport: UTF-16LE paths, a NUL after each and one more at the end.
    $paths = Get-ChildItem -LiteralPath $source -File | ForEach-Object FullName
    $payload = (($paths | ForEach-Object { $_ + [char]0 }) -join '') + [char]0
    $run = Invoke-Shell -Arguments @('--archive', '--paths-stdin') -StandardInput $payload -TimeoutSeconds 60
    Assert-Smoke ($run.Exited -and $run.ExitCode -eq 0) "--archive --paths-stdin: exited=$($run.Exited) code=$($run.ExitCode) $($run.Text)"

    $zip = @(Get-ChildItem -LiteralPath $source -Filter '*.zip')
    Assert-Smoke ($zip.Count -eq 1) "expected one archive next to the sources, found $($zip.Count)"
    $listed = Invoke-Pakko @('l', $zip[0].FullName)
    $entries = @($listed.Output -split '\r?\n' | Where-Object { $_ -match '\tf\t' }).Count
    Assert-Smoke ($entries -eq 1000) "the archive holds $entries entries, expected 1000"
}

Add-SmokeStep 'SHL-007' {
    $folder = Get-StepFolder 'shl007'
    $zips = Get-ShapedZip $folder
    $file = Join-Path $folder 'plain.txt'
    [System.IO.File]::WriteAllText($file, 'plain')
    $already = @(Get-Process -Name 'Archiver.App' -ErrorAction SilentlyContinue | ForEach-Object Id)
    $all = [System.Windows.Automation.Condition]::TrueCondition

    function Wait-AppWindow {
        param([int[]]$Known, [string]$Pattern)
        $deadline = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $deadline) {
            foreach ($process in Get-Process -Name 'Archiver.App' -ErrorAction SilentlyContinue | Where-Object { $Known -notcontains $_.Id -and $_.MainWindowHandle -ne 0 }) {
                $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
                $text = ($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $all) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) -join ' | '
                if ($text -match $Pattern) {
                    return [pscustomobject]@{ Id = $process.Id; Rect = $window.Current.BoundingRectangle }
                }
            }
            Start-Sleep -Milliseconds 300
        }
        throw "no new Pakko window shows '$Pattern' within 30 seconds"
    }

    $opened = @()
    try {
        # The Shell hands the paths over and leaves: the window belongs to Archiver.App.
        $null = Invoke-Shell -Arguments @('--open-ui', '--browse', $zips.Several) -TimeoutSeconds 15
        $first = Wait-AppWindow -Known $already -Pattern 'one\.txt.*two\.txt'
        $opened += $first.Id

        $null = Invoke-Shell -Arguments @('--open-ui', '--extract', $zips.File) -TimeoutSeconds 15
        $second = Wait-AppWindow -Known ($already + $opened) -Pattern 'file\.zip'
        $opened += $second.Id
        Assert-Smoke ($second.Rect.X -ne $first.Rect.X -or $second.Rect.Y -ne $first.Rect.Y) 'the second window opened exactly on top of the first'

        $null = Invoke-Shell -Arguments @('--open-ui', '--archive', $file) -TimeoutSeconds 15
        $third = Wait-AppWindow -Known ($already + $opened) -Pattern 'plain\.txt'
        $opened += $third.Id
    }
    finally {
        foreach ($id in Get-Process -Name 'Archiver.App' -ErrorAction SilentlyContinue | Where-Object { $already -notcontains $_.Id } | ForEach-Object Id) {
            Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
        }
    }
}

Add-SmokeStep 'OPW-001' {
    # The 6000 files alone take a second once Windows has them cached, and the window comes up
    # only for work that lasts: the 200 MB file keeps it there whatever ran before this step.
    $many = Get-Fixture Many
    $big = Get-Fixture Big
    $zip = Join-Path (Split-Path -Parent $many) ((Split-Path -Leaf (Split-Path -Parent $many)) + '.zip')
    Remove-Item -LiteralPath $zip -ErrorAction SilentlyContinue
    try {
        $run = Invoke-Shell -Arguments @('--archive', $many, $big) -Watch '\d+\s?%' -TimeoutSeconds 120
        Assert-Smoke ($run.Exited -and $run.ExitCode -eq 0) "--archive on 6000 files and 200 MB: exited=$($run.Exited) code=$($run.ExitCode) $($run.Text)"
        Assert-Smoke ($null -ne $run.FirstWindow -and $run.FirstWindow -le 3) "the operation window took $($run.FirstWindow) s to appear, expected within 3"
        Assert-Smoke $run.Watched "the operation window never showed a percentage: $($run.Text)"

        $listed = Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('l', $zip)
        Assert-Smoke ($listed.Output -match '(?m)\s6001 files') "7za does not count 6001 files: $(($listed.Output -split '\r?\n')[-2])"
        'window after {0:n1} s, done in {1:n1} s' -f $run.FirstWindow, $run.Seconds
    }
    finally { Remove-Item -LiteralPath $zip -ErrorAction SilentlyContinue }
}

Add-SmokeStep 'SHL-012' {
    $folder = Get-StepFolder 'shl012'
    $name = Split-Path -Leaf (Get-Fixture Tree)
    $source = Join-Path $folder $name
    Copy-Item -LiteralPath (Get-Fixture Tree) -Destination $source -Recurse

    $run = Invoke-Shell -Arguments @('--archive', '--format', '7z', $source) -TimeoutSeconds 30
    Assert-Smoke ($run.Exited -and $run.ExitCode -eq 0) "--archive --format 7z: exited=$($run.Exited) code=$($run.ExitCode) $($run.Text)"
    $archive = Join-Path $folder "$name.7z"
    Assert-Smoke (Test-Path -LiteralPath $archive) '--archive --format 7z made no <name>.7z next to the source'
    $tested = Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('t', $archive)
    Assert-Smoke ($tested.ExitCode -eq 0) "7za t on the 7z: exit $($tested.ExitCode)"
    $listed = Invoke-Pakko @('l', '-sccUTF-8', $archive)
    Assert-Smoke ($listed.Output.Contains("$name/sub/empty.dat")) "the entries lost their folder prefix: $($listed.Output)"
    $left = @(Get-ChildItem -LiteralPath $folder -Force -Name | Where-Object { $_ -notin $name, "$name.7z" })
    Assert-Smoke ($left.Count -eq 0) "left next to the archive: $($left -join ', ')"
}

# T-F342: the Explorer menu cannot ask tar.exe whether it writes 7z, so the Shell leaves the
# answer where the menu reads it (Archiver.Core's SevenZipWriterVerdict).
Add-SmokeStep 'SHL-013' {
    $folder = Get-StepFolder 'shl013'
    $source = Join-Path $folder 'note.txt'
    Set-Content -LiteralPath $source -Value 'verdict' -Encoding ascii
    $verdict = Join-Path $env:LOCALAPPDATA "Packages\$($Smoke.Package.PackageFamilyName)\LocalCache\sevenzip-writer.txt"
    Remove-Item -LiteralPath $verdict -Force -ErrorAction SilentlyContinue

    $run = Invoke-Shell -Arguments @('--archive', '--format', '7z', $source) -TimeoutSeconds 30
    Assert-Smoke ($run.Exited -and $run.ExitCode -eq 0) "--archive --format 7z: exited=$($run.Exited) code=$($run.ExitCode) $($run.Text)"
    Assert-Smoke (Test-Path -LiteralPath (Join-Path $folder 'note.7z')) '--archive --format 7z made no note.7z'
    Assert-Smoke (Test-Path -LiteralPath $verdict) "no verdict file at $verdict"
    $tar = Get-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\tar.exe')
    $expected = '{0};{1};1' -f $tar.Length, $tar.LastWriteTimeUtc.ToFileTimeUtc()
    $written = [IO.File]::ReadAllText($verdict)
    Assert-Smoke ($written -ceq $expected) "the verdict reads '$written', expected '$expected'"
}
