#Requires -Version 7.0
<#
.SYNOPSIS
    Measures how far pakko.exe can stand in for 7-Zip's console program (T-F343).
.DESCRIPTION
    Runs the exact command lines that real callers give 7z.exe, once with the 7-Zip reference
    (the vendored, hash-checked 7za.exe of T-F114) and once with pakko.exe, each in a fresh
    folder, and compares the exit code, the extracted or created files (relative path and
    SHA-256) and the lines of output the caller reads. Writes one TSV row per case and archive.

    Callers, with the source each command line is copied from (docs/DECISIONS.md, T-F343):
      Scoop       lib/decompress.ps1 Expand-7zipArchive
      Chocolatey  helpers/functions/Get-ChocolateyUnzip.ps1
      patool      patoolib/programs/p7zip.py

    Verdicts: works (same exit code, same files, same parsed output); differs (both ran, the
    result is not the same); missing (pakko refuses the command line: exit 7); reference-fails
    (7za itself does not accept it on this archive, so the case says nothing). 7za reads no RAR:
    RAR cases record pakko's result with the verdict pakko-only.

    A measurement, not a test: nothing here fails CI. Run it before and after a change that
    touches the command line, and compare the two TSVs.
.PARAMETER Pakko
    The pakko.exe to measure (default: the standalone x64 build from Publish-Cli.ps1).
.PARAMETER Reference
    The 7-Zip console program to compare with (default: the vendored 7za.exe).
.PARAMETER OutFile
    Where the TSV goes (default: pakko-7z-compat.tsv in the temp folder).
.EXAMPLE
    .\Publish-Cli.ps1 -Architecture x64; .\Measure-SevenZipCompat.ps1
#>
[CmdletBinding()]
param(
    [string]$Pakko = (Join-Path $PSScriptRoot '..\artifacts\cli\win-x64\pakko.exe'),
    [string]$Reference = (Join-Path $PSScriptRoot '..\tests\Archiver.Core.PerformanceTests\Tools\7-Zip\x64\7za.exe'),
    [string]$OutFile = (Join-Path ([IO.Path]::GetTempPath()) 'pakko-7z-compat.tsv')
)

$ErrorActionPreference = 'Stop'
$Pakko = (Resolve-Path -LiteralPath $Pakko).Path
$Reference = (Resolve-Path -LiteralPath $Reference).Path
$RarFixture = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\tests\Archiver.Core.IntegrationTests\Fixtures\valid.rar')).Path
$Password = 'Right-1'

function Invoke-Tool {
    param([string]$Exe, [string[]]$Arguments, [string]$WorkingDirectory)
    $info = [Diagnostics.ProcessStartInfo]::new($Exe)
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.Encoding]::UTF8
    $info.StandardErrorEncoding = [Text.Encoding]::UTF8
    $process = [Diagnostics.Process]::Start($info)
    try {
        # A caller that never answers a prompt: stdin closed, as under a CI runner.
        $process.StandardInput.Close()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) {
            $process.Kill($true)
            return [pscustomobject]@{ Exit = -1; Out = @(); Err = @('timed out') }
        }
        [pscustomobject]@{
            Exit = $process.ExitCode
            Out  = @($stdout.Result -split "`r?`n" | Where-Object { $_ -ne '' })
            Err  = @($stderr.Result -split "`r?`n" | Where-Object { $_ -ne '' })
        }
    }
    finally {
        $process.Dispose()
    }
}

function Get-Tree {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    $root = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\') + '\'
    $lines = Get-ChildItem -LiteralPath $Path -Recurse -File | ForEach-Object {
        $_.FullName.Substring($root.Length) + ' ' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.Substring(0, 12)
    }
    (@($lines) | Sort-Object) -join '|'
}

# What a caller reads from the output, per case.
function Get-Parsed {
    param([string]$Parse, [string[]]$Out)
    switch ($Parse) {
        # Chocolatey: every "- name" line is a file it records as installed.
        'choco-dash-lines' { (@($Out | Where-Object { $_.StartsWith('- ') } | ForEach-Object { $_.Substring(2) }) | Sort-Object) -join '|' }
        # Scoop: the inner tar's name, by the regex it runs over the l output.
        'scoop-tar-name' { (@($Out | Select-String -Pattern '[^ ]*tar$' | ForEach-Object { $_.Matches.Value })) -join '|' }
        default { '' }
    }
}

# A Cyrillic folder and file: 7-Zip and pakko must agree on non-ASCII names. Built from code
# points so the script stays ASCII.
$cyrillicFolder = -join [char[]]@(0x041A, 0x0438, 0x0440, 0x0438, 0x043B, 0x0438, 0x0446, 0x044F)
$cyrillicFile = (-join [char[]]@(0x0444, 0x0430, 0x0439, 0x043B)) + '.txt'
$cyrillicText = -join [char[]]@(0x0442, 0x0435, 0x043A, 0x0441, 0x0442)

$work = Join-Path ([IO.Path]::GetTempPath()) ('pakko-7zcompat-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$source = Join-Path $work 'src'
$archives = Join-Path $work 'archives'
New-Item -ItemType Directory -Force -Path (Join-Path $source 'app\docs'), (Join-Path $source $cyrillicFolder), $archives | Out-Null
Set-Content -LiteralPath (Join-Path $source 'app\readme.txt') -Value 'read me' -NoNewline
Set-Content -LiteralPath (Join-Path $source 'app\setup.nsis') -Value 'installer script' -NoNewline
Set-Content -LiteralPath (Join-Path $source 'app\docs\guide.md') -Value '# guide' -NoNewline
Set-Content -LiteralPath (Join-Path -Path $source -ChildPath $cyrillicFolder -AdditionalChildPath $cyrillicFile) -Value $cyrillicText -NoNewline
[IO.File]::WriteAllBytes((Join-Path $source 'app\tool.bin'), [byte[]](0..255))

# The archives are made by the reference, so both programs read the same bytes.
$inputs = @('app', $cyrillicFolder)
$made = @{}
function Build-ReferenceArchive {
    param([string]$Name, [string[]]$Arguments)
    $result = Invoke-Tool -Exe $Reference -Arguments $Arguments -WorkingDirectory $source
    if ($result.Exit -ne 0) { throw "7za could not make $Name : $($result.Err -join ' ')" }
    $made[$Name] = Join-Path $archives $Name
}
Build-ReferenceArchive 'test.zip' (@('a', '-tzip', (Join-Path $archives 'test.zip')) + $inputs)
Build-ReferenceArchive 'test.7z' (@('a', '-t7z', (Join-Path $archives 'test.7z')) + $inputs)
Build-ReferenceArchive 'test.tar' (@('a', '-ttar', (Join-Path $archives 'test.tar')) + $inputs)
Build-ReferenceArchive 'test.tar.gz' @('a', '-tgzip', (Join-Path $archives 'test.tar.gz'), (Join-Path $archives 'test.tar'))
Build-ReferenceArchive 'test.tar.xz' @('a', '-txz', (Join-Path $archives 'test.tar.xz'), (Join-Path $archives 'test.tar'))
Build-ReferenceArchive 'aes.zip' (@('a', '-tzip', "-p$Password", '-mem=AES256', (Join-Path $archives 'aes.zip')) + $inputs)
Copy-Item -LiteralPath $RarFixture -Destination (Join-Path $archives 'test.rar')
$made['test.rar'] = Join-Path $archives 'test.rar'

$readable = @('test.zip', 'test.7z', 'test.tar', 'test.tar.gz', 'test.tar.xz', 'test.rar')

# For the error cases: a ZIP and a 7z cut in half, a text file, a path that does not exist, and an
# archive whose name starts with a dash (only "--" lets it through).
foreach ($name in 'test.zip', 'test.7z') {
    $bytes = [IO.File]::ReadAllBytes($made[$name])
    $cut = Join-Path $archives ('cut-' + $name)
    [IO.File]::WriteAllBytes($cut, $bytes[0..([int]($bytes.Length / 2))])
    $made['cut-' + $name] = $cut
}
$made['text.txt'] = Join-Path $source 'app\readme.txt'
$made['missing.zip'] = Join-Path $archives 'missing.zip'
Copy-Item -LiteralPath $made['test.zip'] -Destination (Join-Path $archives '-dash.zip')
$made['-dash.zip'] = Join-Path $archives '-dash.zip'

# {a} = the archive, {o} = the output folder, {n} = a new archive in the output folder.
$cases = @(
    @{ Caller = 'Scoop'; Id = 'x'; Args = @('x', '{a}', '-o{o}', '-xr!*.nsis', '-y'); On = $readable }
    @{ Caller = 'Scoop'; Id = 'x ExtractDir'; Args = @('x', '{a}', '-o{o}', '-xr!*.nsis', '-y', '-ir!app\*'); On = @('test.zip', 'test.7z') }
    @{ Caller = 'Scoop'; Id = 'x -aoa onto a file'; Args = @('x', '{a}', '-o{o}', '-xr!*.nsis', '-y', '-aoa'); On = @('test.zip'); Conflict = $true }
    @{ Caller = 'Scoop'; Id = 'x -aos onto a file'; Args = @('x', '{a}', '-o{o}', '-xr!*.nsis', '-y', '-aos'); On = @('test.zip'); Conflict = $true }
    @{ Caller = 'Scoop'; Id = 'x -aou onto a file'; Args = @('x', '{a}', '-o{o}', '-xr!*.nsis', '-y', '-aou'); On = @('test.zip'); Conflict = $true }
    @{ Caller = 'Scoop'; Id = 'l (inner tar name)'; Args = @('l', '{a}'); On = @('test.tar.gz', 'test.tar.xz'); Parse = 'scoop-tar-name' }
    @{ Caller = 'Chocolatey'; Id = 'x'; Args = @('x', '-aoa', '-bd', '-bb1', '-o{o}', '-y', '{a}'); On = $readable; Parse = 'choco-dash-lines' }
    @{ Caller = 'Chocolatey'; Id = 'x specificFolder'; Args = @('x', '-aoa', '-bd', '-bb1', '-o{o}', '-y', '{a}', 'app'); On = @('test.zip', 'test.7z'); Parse = 'choco-dash-lines' }
    @{ Caller = 'patool'; Id = 'x'; Args = @('x', '-y', '-p-', '-aou', '-o{o}', '--', '{a}'); On = $readable }
    @{ Caller = 'patool'; Id = 'x with password'; Args = @('x', '-y', "-p$Password", '-aou', '-o{o}', '--', '{a}'); On = @('aes.zip') }
    @{ Caller = 'patool'; Id = 'e'; Args = @('e', '-y', '-p-', '-o{o}', '--', '{a}'); On = @('test.zip', 'test.7z') }
    @{ Caller = 'patool'; Id = 'l'; Args = @('l', '-y', '-p-', '--', '{a}'); On = $readable }
    @{ Caller = 'patool'; Id = 't'; Args = @('t', '-y', '-p-', '--', '{a}'); On = $readable }
    @{ Caller = 'patool'; Id = 'a -t7z'; Args = @('a', '-y', '-t7z', '-sae', '--', '{n}.7z', 'app'); On = @('(create)') }
    @{ Caller = 'patool'; Id = 'a -tzip'; Args = @('a', '-y', '-tzip', '--', '{n}.zip', 'app'); On = @('(create)') }
    @{ Caller = 'patool'; Id = 'a -tzip with password'; Args = @('a', '-y', "-p$Password", '-tzip', '--', '{n}.zip', 'app'); On = @('(create)') }
    @{ Caller = 'patool'; Id = 'a -txz'; Args = @('a', '-y', '-txz', '--', '{n}.xz', 'app\readme.txt'); On = @('(create)') }
    @{ Caller = 'patool'; Id = 'a -tgzip'; Args = @('a', '-y', '-tgzip', '--', '{n}.gz', 'app\readme.txt'); On = @('(create)') }
    @{ Caller = 'patool'; Id = 'a -tbzip2'; Args = @('a', '-y', '-tbzip2', '--', '{n}.bz2', 'app\readme.txt'); On = @('(create)') }
    @{ Caller = 'patool'; Id = 'a -ttar'; Args = @('a', '-y', '-ttar', '--', '{n}.tar', 'app'); On = @('(create)') }

    # How each program reports a failure: the exit code a script branches on (7-Zip's
    # ExitCode.h: 0 ok, 1 warning, 2 fatal, 7 command line, 8 memory, 255 user break) and
    # whether the text goes to stderr. Stdin is closed, so a prompt reads end of input.
    @{ Caller = '(errors)'; Id = 'x a missing archive'; Args = @('x', '{a}', '-o{o}', '-y'); On = @('missing.zip'); Error = $true }
    @{ Caller = '(errors)'; Id = 'x a cut archive'; Args = @('x', '{a}', '-o{o}', '-y'); On = @('cut-test.zip', 'cut-test.7z'); Error = $true }
    @{ Caller = '(errors)'; Id = 't a cut archive'; Args = @('t', '{a}'); On = @('cut-test.zip', 'cut-test.7z'); Error = $true }
    @{ Caller = '(errors)'; Id = 'l a cut archive'; Args = @('l', '{a}'); On = @('cut-test.zip', 'cut-test.7z'); Error = $true }
    @{ Caller = '(errors)'; Id = 'x not an archive'; Args = @('x', '{a}', '-o{o}', '-y'); On = @('text.txt'); Error = $true }
    @{ Caller = '(errors)'; Id = 'x wrong password'; Args = @('x', '{a}', '-o{o}', '-y', '-pWrong'); On = @('aes.zip'); Error = $true }
    @{ Caller = '(errors)'; Id = 'x encrypted, no password, no stdin'; Args = @('x', '{a}', '-o{o}'); On = @('aes.zip'); Error = $true }
    @{ Caller = '(errors)'; Id = 'x onto a file, no -y, no stdin'; Args = @('x', '{a}', '-o{o}'); On = @('test.zip'); Conflict = $true; Error = $true }
    @{ Caller = '(errors)'; Id = 'a a missing source (warning)'; Args = @('a', '{n}.zip', 'app\readme.txt', 'no-such-file.txt'); On = @('(create)'); Error = $true }
    @{ Caller = '(errors)'; Id = 'unknown command'; Args = @('z', '{a}'); On = @('test.zip'); Error = $true }
    @{ Caller = '(errors)'; Id = 'unknown switch'; Args = @('x', '{a}', '-o{o}', '-qq'); On = @('test.zip'); Error = $true }
    @{ Caller = '(errors)'; Id = 'no archive name'; Args = @('x'); On = @('test.zip'); Error = $true }
    @{ Caller = '(errors)'; Id = 'output folder is a file'; Args = @('x', '{a}', '-o{a}', '-y'); On = @('test.zip'); Error = $true }

    # How the command line is read (CPP/Common/CommandLineParser.cpp, ArchiveCommandLine.cpp):
    # the command is the first argument that is not a switch, switches go anywhere and in any
    # case, names after the archive are file filters.
    @{ Caller = '(parsing)'; Id = 'command in capitals'; Args = @('X', '{a}', '-o{o}', '-y'); On = @('test.zip'); Error = $true }
    @{ Caller = '(parsing)'; Id = 'switches in capitals'; Args = @('x', '{a}', '-O{o}', '-Y', '-AOA'); On = @('test.zip'); Error = $true }
    @{ Caller = '(parsing)'; Id = 'switch before the command'; Args = @('-y', 'x', '{a}', '-o{o}'); On = @('test.zip'); Error = $true }
    @{ Caller = '(parsing)'; Id = 'a file filter after the archive'; Args = @('x', '{a}', 'app\readme.txt', '-o{o}', '-y'); On = @('test.zip', 'test.7z'); Error = $true }
    @{ Caller = '(parsing)'; Id = 'a wildcard filter'; Args = @('x', '{a}', '*.md', '-r', '-o{o}', '-y'); On = @('test.zip'); Error = $true }
    @{ Caller = '(parsing)'; Id = '-o with a space'; Args = @('x', '{a}', '-o', '{o}', '-y'); On = @('test.zip'); Error = $true }
    @{ Caller = '(parsing)'; Id = '-o given twice'; Args = @('x', '{a}', '-o{o}\first', '-o{o}\second', '-y'); On = @('test.zip'); Error = $true }
    @{ Caller = '(parsing)'; Id = 'wildcard archive name'; Args = @('x', '{d}\test.*', '-o{o}', '-y'); On = @('test.zip'); Error = $true }
    @{ Caller = '(parsing)'; Id = '@listfile of filters'; Args = @('x', '{a}', '@{l}', '-o{o}', '-y'); On = @('test.zip'); Error = $true }
    @{ Caller = '(parsing)'; Id = '-- before a dash name'; Args = @('x', '-o{o}', '-y', '--', '{a}'); On = @('-dash.zip'); Error = $true }
    @{ Caller = '(parsing)'; Id = 'a dash name without --'; Args = @('x', '-o{o}', '-y', '{a}'); On = @('-dash.zip'); Error = $true }
    @{ Caller = '(parsing)'; Id = '-mx9 without ='; Args = @('a', '-mx9', '{n}.zip', 'app'); On = @('(create)'); Error = $true }
    @{ Caller = '(parsing)'; Id = '-tZIP in capitals'; Args = @('a', '-tZIP', '{n}.zip', 'app'); On = @('(create)'); Error = $true }
    @{ Caller = '(parsing)'; Id = 'a without an extension'; Args = @('a', '{n}', 'app'); On = @('(create)'); Error = $true }
    @{ Caller = '(parsing)'; Id = 'a wildcard source, -r'; Args = @('a', '{n}.zip', '*.md', '-r'); On = @('(create)'); Error = $true }
)

function Invoke-Case {
    param([string]$Exe, [hashtable]$Case, [string]$ArchiveName, [string]$Run)
    $out = Join-Path $work "$Run\out"
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    if ($Case.Conflict) {
        New-Item -ItemType Directory -Force -Path (Join-Path $out 'app') | Out-Null
        Set-Content -LiteralPath (Join-Path $out 'app\readme.txt') -Value 'already here' -NoNewline
    }
    $newArchive = Join-Path $out 'new'
    $listFile = Join-Path $work "$Run\list.txt"
    Set-Content -LiteralPath $listFile -Value 'app\readme.txt' -NoNewline
    $arguments = foreach ($token in $Case.Args) {
        $token.Replace('{a}', $made[$ArchiveName] ?? '').Replace('{o}', $out).Replace('{n}', $newArchive).
        Replace('{d}', $archives).Replace('{l}', $listFile)
    }
    $result = Invoke-Tool -Exe $Exe -Arguments $arguments -WorkingDirectory $source
    $tree = Get-Tree $out
    if ($ArchiveName -eq '(create)') {
        # What a created archive holds, read back by the reference into its own folder.
        $created = Get-ChildItem -LiteralPath $out -File | Select-Object -First 1
        $tree = if ($created) {
            $back = Join-Path $work "$Run\back"
            $null = Invoke-Tool -Exe $Reference -Arguments @('x', $created.FullName, "-o$back", '-y', "-p$Password") -WorkingDirectory $source
            $created.Extension + ' ' + (Get-Tree $back)
        }
        else { '(no archive)' }
    }
    [pscustomobject]@{
        Exit   = $result.Exit
        Tree   = $tree
        Parsed = (Get-Parsed $Case.Parse $result.Out)
        Err    = ($result.Err -join ' ')
        # Where the text went: the first stderr line, and whether stdout ended in 7-Zip's
        # "Everything is Ok" or an "Errors: N" / "Warnings: N" summary.
        Report = ('stderr: ' + ($result.Err | Select-Object -First 1) + ' / stdout ends: ' + ($result.Out | Select-Object -Last 1))
    }
}

$rows = [Collections.Generic.List[object]]::new()
$runIndex = 0
foreach ($case in $cases) {
    foreach ($archiveName in $case.On) {
        $runIndex++
        $pakkoResult = Invoke-Case -Exe $Pakko -Case $case -ArchiveName $archiveName -Run "p$runIndex"
        if ($archiveName -eq 'test.rar') {
            $referenceResult = $null
            $verdict = 'pakko-only'
            $detail = "pakko exit $($pakkoResult.Exit)"
        }
        else {
            $referenceResult = Invoke-Case -Exe $Reference -Case $case -ArchiveName $archiveName -Run "r$runIndex"
            $differences = @()
            if ($referenceResult.Exit -ne $pakkoResult.Exit) { $differences += "exit $($referenceResult.Exit) vs $($pakkoResult.Exit)" }
            if ($referenceResult.Tree -ne $pakkoResult.Tree) { $differences += "files: 7za [$($referenceResult.Tree)] pakko [$($pakkoResult.Tree)]" }
            if ($referenceResult.Parsed -ne $pakkoResult.Parsed) { $differences += "parsed: 7za [$($referenceResult.Parsed)] pakko [$($pakkoResult.Parsed)]" }
            # An error case compares the failure itself: a non-zero exit is its subject, not noise.
            $verdict = if ($referenceResult.Exit -ne 0 -and -not $case.Error) { 'reference-fails' }
            elseif ($differences.Count -eq 0) { 'works' }
            elseif ($pakkoResult.Exit -eq 7 -and $referenceResult.Exit -ne 7) { 'missing' }
            else { 'differs' }
            $detail = if ($case.Error) {
                ($differences + "7za $($referenceResult.Report)" + "pakko $($pakkoResult.Report)") -join '; '
            }
            else {
                ($differences + $(if ($pakkoResult.Err) { "pakko stderr: $($pakkoResult.Err)" })) -join '; '
            }
        }
        $rows.Add([pscustomobject]@{
                Caller    = $case.Caller
                Case      = $case.Id
                Archive   = $archiveName
                RefExit   = $referenceResult.Exit
                PakkoExit = $pakkoResult.Exit
                Verdict   = $verdict
                Detail    = ($detail -replace "`t", ' ')
            })
    }
}

# Every switch of 7-Zip 26.02 (the kSwitchForms table of CPP/7zip/UI/Common/ArchiveCommandLine.cpp),
# one at a time on a command line that works without it, so one refused switch cannot hide the
# next. {o} is a fresh folder; "a" runs on a copy of the sources (-sdel deletes them). -seml
# (send by e-mail) and -si (reads stdin) are left out: they need a mail client or a pipe.
$switchProbes = @(
    @{ T = '--'; C = 'x' }, @{ T = '-ba'; C = 'l' }, @{ T = '-bd'; C = 'x' }, @{ T = '-bt'; C = 'x' },
    @{ T = '-bb0'; C = 'x' }, @{ T = '-bb1'; C = 'x' }, @{ T = '-bb3'; C = 'x' }, @{ T = '-bso1'; C = 'x' },
    @{ T = '-bse1'; C = 'x' }, @{ T = '-bsp1'; C = 'x' }, @{ T = '-bso0'; C = 'l' }, @{ T = '-y'; C = 'x' },
    @{ T = '-y'; C = 'l' }, @{ T = '-y'; C = 't' }, @{ T = '-y'; C = 'a' }, @{ T = '-ad'; C = 'x' },
    @{ T = '-aoa'; C = 'x' }, @{ T = '-aos'; C = 'x' }, @{ T = '-aot'; C = 'x' }, @{ T = '-aou'; C = 'x' },
    @{ T = '-t7z'; C = 'a' }, @{ T = '-tzip'; C = 'x' }, @{ T = '-stxrar'; C = 'x' }, @{ T = '-stxzip'; C = 't' }, @{ T = '-stx7z'; C = 't' }, @{ T = '-mx9'; C = 'a' },
    @{ T = '-mmt2'; C = 'a' }, @{ T = '-mm=Deflate'; C = 'a' }, @{ T = '-mtc=on'; C = 'a' }, @{ T = '-w'; C = 'a' },
    @{ T = '-i!*.txt'; C = 'x' }, @{ T = '-ir!*.txt'; C = 'x' }, @{ T = '-x!*.nsis'; C = 'x' }, @{ T = '-xr!*.nsis'; C = 'x' },
    @{ T = '-x!*.nsis'; C = 'a' }, @{ T = '-x!*.nsis'; C = 't' }, @{ T = '-ir!*.txt'; C = 't' }, @{ T = '-ai!*.zip'; C = 'x' }, @{ T = '-ax!*.rar'; C = 'x' }, @{ T = '-an'; C = 'x' },
    @{ T = '-u-'; C = 'a' }, @{ T = '-v1m'; C = 'a' }, @{ T = '-r'; C = 'a' }, @{ T = '-r-'; C = 'a' },
    @{ T = '-r'; C = 'x' }, @{ T = '-stm1'; C = 'a' }, @{ T = '-sfx'; C = 'a' }, @{ T = '-scrcSHA256'; C = 'h' },
    @{ T = '-scrcSHA256'; C = 'x' }, @{ T = '-scrf'; C = 'h' }, @{ T = '-shd'; C = 'h' }, @{ T = '-smemx'; C = 'x' },
    @{ T = '-so'; C = 'x1' }, @{ T = '-slp'; C = 'a' }, @{ T = '-scsUTF-8'; C = 'a' }, @{ T = '-sccUTF-8'; C = 'l' },
    @{ T = '-slt'; C = 'l' }, @{ T = '-slf'; C = 'l' }, @{ T = '-slsl'; C = 'l' }, @{ T = '-slmu'; C = 'l' },
    @{ T = '-ssp'; C = 'a' }, @{ T = '-ssw'; C = 'a' }, @{ T = '-sse'; C = 'a' }, @{ T = '-ssc'; C = 'x' },
    @{ T = '-ssc-'; C = 'x' }, @{ T = '-sae'; C = 'a' }, @{ T = '-spm'; C = 'x' }, @{ T = '-spd'; C = 'x' },
    @{ T = '-spe'; C = 'x' }, @{ T = '-spf'; C = 'x' }, @{ T = '-spo'; C = 'x' }, @{ T = '-snh'; C = 'a' },
    @{ T = '-snld'; C = 'x' }, @{ T = '-snl'; C = 'a' }, @{ T = '-sni'; C = 'a' }, @{ T = '-snoi'; C = 'x' },
    @{ T = '-snon'; C = 'x' }, @{ T = '-snz'; C = 'x' }, @{ T = '-sns'; C = 'a' }, @{ T = '-snr'; C = 'x' },
    @{ T = '-snc'; C = 'x' }, @{ T = '-snt'; C = 'x' }, @{ T = '-sdel'; C = 'a' }, @{ T = '-stl'; C = 'a' },
    @{ T = '-p-'; C = 'x' }, @{ T = '-p-'; C = 'l' }, @{ T = '-p-'; C = 't' }, @{ T = "-p$Password"; C = 'x' },
    @{ T = '@list.txt'; C = 'a' }, @{ T = '(none)'; C = 'e' }, @{ T = '(none)'; C = 'u' }, @{ T = '(none)'; C = 'd' },
    @{ T = '(none)'; C = 'rn' }, @{ T = '(none)'; C = 'b' }, @{ T = '(none)'; C = 'i' }
)

function Invoke-Probe {
    param([string]$Exe, [hashtable]$Probe, [string]$Run)
    $out = Join-Path $work "$Run\out"
    $copy = Join-Path $work "$Run\src"
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    Copy-Item -LiteralPath (Join-Path $source 'app') -Destination (Join-Path $copy 'app') -Recurse
    Set-Content -LiteralPath (Join-Path $copy 'list.txt') -Value 'app\readme.txt' -NoNewline
    $zip = $made['test.zip']
    $token = if ($Probe.T -eq '(none)') { @() } else { @($Probe.T) }
    $arguments = switch ($Probe.C) {
        'x' { if ($Probe.T -eq '--') { @('x', "-o$out", '--', $zip) } else { @('x', $zip, "-o$out") + $token } }
        'x1' { @('x', $zip, 'app\readme.txt') + $token }
        'l' { @('l', $zip) + $token }
        't' { @('t', $zip) + $token }
        'a' { @('a', (Join-Path $out 'new.zip'), 'app') + $token }
        'h' { @('h', 'app\readme.txt') + $token }
        'e' { @('e', $zip, "-o$out") }
        'u' { @('u', (Join-Path $out 'new.zip'), 'app') }
        'd' { Copy-Item -LiteralPath $zip -Destination (Join-Path $out 'd.zip'); @('d', (Join-Path $out 'd.zip'), 'app\readme.txt') }
        'rn' { Copy-Item -LiteralPath $zip -Destination (Join-Path $out 'rn.zip'); @('rn', (Join-Path $out 'rn.zip'), 'app\readme.txt', 'app\read.txt') }
        'b' { @('b', '-mmt1', '-md18') }
        'i' { @('i') }
    }
    $result = Invoke-Tool -Exe $Exe -Arguments $arguments -WorkingDirectory $copy
    [pscustomobject]@{ Exit = $result.Exit; Err = ($result.Err | Select-Object -First 1) }
}

foreach ($probe in $switchProbes) {
    $runIndex++
    $pakkoResult = Invoke-Probe -Exe $Pakko -Probe $probe -Run "p$runIndex"
    $referenceResult = Invoke-Probe -Exe $Reference -Probe $probe -Run "r$runIndex"
    $verdict = if ($referenceResult.Exit -ne 0) { 'reference-fails' }
    elseif ($pakkoResult.Exit -eq 0) { 'accepted' }
    elseif ($pakkoResult.Exit -eq 7) { 'missing' }
    else { 'differs' }
    $rows.Add([pscustomobject]@{
            Caller    = '(switch)'
            Case      = $probe.T
            Archive   = $probe.C
            RefExit   = $referenceResult.Exit
            PakkoExit = $pakkoResult.Exit
            Verdict   = $verdict
            Detail    = ("$($pakkoResult.Err)" -replace "`t", ' ')
        })
}

$rows | Export-Csv -LiteralPath $OutFile -Delimiter "`t" -NoTypeInformation -Encoding utf8
$rows | Group-Object Verdict | Sort-Object Name | ForEach-Object { "{0,-16} {1}" -f $_.Name, $_.Count }
"TSV: $OutFile"
Remove-Item -LiteralPath $work -Recurse -Force
