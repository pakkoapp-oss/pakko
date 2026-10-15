#Requires -Version 7.0
# Steps of docs/SMOKE.md's POL rows and of PKG-005: the ones that need an administrator. They
# share one elevated helper (elevated/PolicyHost.ps1) and so one elevation prompt per run.
# Dot-sourced by scripts/Invoke-Smoke.ps1.

# Archives made before any policy is set: a ZIP with the download mark that holds a text file
# and a file of an unsafe type, a tar.gz, and the 7z and RAR of the integration tests.
function Get-PolicyFixture {
    if (-not $Smoke.Fixtures.ContainsKey('Policy')) {
        $folder = Get-StepFolder 'policy-fixture'
        $source = Join-Path $folder 'files'
        $null = [System.IO.Directory]::CreateDirectory($source)
        [System.IO.File]::WriteAllText((Join-Path $source 'doc.txt'), 'text')
        [System.IO.File]::WriteAllText((Join-Path $source 'run.cmd'), '@echo off')
        $zip = Join-Path $folder 'a.zip'
        $tarGz = Join-Path $folder 'a.tar.gz'
        foreach ($arguments in @('a', $zip, $source), @('a', '-ttar.gz', $tarGz, $source)) {
            $made = Invoke-Pakko $arguments
            Assert-Smoke ($made.ExitCode -eq 0) "policy fixture: exit $($made.ExitCode) $($made.Text)"
        }
        Set-Content -LiteralPath $zip -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3"
        $fixtures = Join-Path $Smoke.Repo 'tests\Archiver.Core.IntegrationTests\Fixtures'
        $Smoke.Fixtures['Policy'] = @{
            Source = $source; Zip = $zip; TarGz = $tarGz
            SevenZip = Join-Path $fixtures 'valid.7z'; Rar = Join-Path $fixtures 'valid.rar'
        }
    }
    $Smoke.Fixtures['Policy']
}

# The names of the files under a folder that carry the download mark, sorted and joined.
function Get-MarkedName {
    param([Parameter(Mandatory)][string]$Path)
    (Get-ChildItem -LiteralPath $Path -Recurse -File | Where-Object { Get-Item -LiteralPath $_.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue } |
        ForEach-Object Name | Sort-Object) -join ','
}

# Each case: @{ Run = <pakko arguments>; Exit = <code>; Says = <pattern, when one is expected> }.
function Assert-PakkoCase {
    param([Parameter(Mandatory)][hashtable[]]$Cases, [Parameter(Mandatory)][string]$Under)
    foreach ($case in $Cases) {
        $run = Invoke-Pakko $case.Run -TimeoutSeconds 120
        $what = "under ${Under}: pakko $($case.Run[0]) $(Split-Path -Leaf $case.Run[-1])"
        Assert-Smoke ($run.ExitCode -eq $case.Exit) "${what}: exit $($run.ExitCode), expected $($case.Exit). $($run.Text)"
        if ($case.Says) { Assert-Smoke ($run.Text -match $case.Says) "$what does not say '$($case.Says)': $($run.Text)" }
    }
}

# Archiver.Shell on a private copy of an archive; returns the copy's path and what the Shell did.
function Invoke-PolicyShell {
    param([Parameter(Mandatory)][string]$Archive, [Parameter(Mandatory)][string]$Folder, [Parameter(Mandatory)][string[]]$Arguments)
    $null = [System.IO.Directory]::CreateDirectory($Folder)
    $copy = Join-Path $Folder (Split-Path -Leaf $Archive)
    Copy-Item -LiteralPath $Archive -Destination $copy
    if (Get-Item -LiteralPath $Archive -Stream Zone.Identifier -ErrorAction SilentlyContinue) {
        Set-Content -LiteralPath $copy -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3"
    }
    # A refusal is a result window with a line "<archive>: <reason>"; a success ends with no window.
    $run = Invoke-Shell -Arguments ($Arguments + $copy) -Until '(^|[^.\w])a\.[a-z.]+: ' -TimeoutSeconds 30
    [pscustomobject]@{ Copy = $copy; Refused = $run.Matched; ExitCode = $run.ExitCode; Text = $run.Text
        Left = @(Get-ChildItem -LiteralPath $Folder -Force -Name | Where-Object { $_ -ne (Split-Path -Leaf $copy) }) }
}

Add-SmokeStep 'POL-001' {
    $fixture = Get-PolicyFixture
    $root = Get-StepFolder 'pol001'
    # The user's choice is the opposite each time: -snz0 asks for no mark, no switch for the mark.
    $cases = @(
        @{ Value = 0; Switch = @(); Marked = '' }
        @{ Value = 1; Switch = @('-snz0'); Marked = 'doc.txt,run.cmd' }
        @{ Value = 2; Switch = @('-snz0'); Marked = 'run.cmd' }
    )
    foreach ($case in $cases) {
        Use-SmokePolicy -Values @{ EnforceMOTW = $case.Value } -Body {
            $out = Join-Path $root "cli$($case.Value)"
            $run = Invoke-Pakko (@('x', $fixture.Zip, "-o$out") + $case.Switch)
            Assert-Smoke ($run.ExitCode -eq 0) "x under EnforceMOTW=$($case.Value): exit $($run.ExitCode) $($run.Text)"
            Assert-Smoke ((Get-MarkedName $out) -eq $case.Marked) "x under EnforceMOTW=$($case.Value) marked '$(Get-MarkedName $out)', expected '$($case.Marked)'"
            if ($case.Switch) { Assert-Smoke ($run.Text -match 'EnforceMOTW') "x -snz0 under EnforceMOTW=$($case.Value) does not say that the policy decides: $($run.Text)" }

            $shell = Invoke-PolicyShell -Archive $fixture.Zip -Folder (Join-Path $root "shell$($case.Value)") -Arguments @('--extract-folder')
            Assert-Smoke ($shell.ExitCode -eq 0 -and $shell.Left.Count -eq 1) "Shell extraction under EnforceMOTW=$($case.Value): exit $($shell.ExitCode), wrote $($shell.Left -join ', ') $($shell.Text)"
            $marked = Get-MarkedName (Join-Path (Split-Path -Parent $shell.Copy) $shell.Left[0])
            Assert-Smoke ($marked -eq $case.Marked) "Shell extraction under EnforceMOTW=$($case.Value) marked '$marked', expected '$($case.Marked)'"
        }
    }
}

Add-SmokeStep 'POL-002' {
    $fixture = Get-PolicyFixture
    $root = Get-StepFolder 'pol002'
    Use-SmokePolicy -Values @{ AllowedFormats = @('zip') } -Body {
        $blocked = 'is blocked by Group Policy'
        Assert-PakkoCase -Under 'AllowedFormats=zip' -Cases @(
            @{ Run = @('l', $fixture.TarGz); Exit = 2; Says = $blocked }
            @{ Run = @('x', "-o$root\tgz", $fixture.TarGz); Exit = 1; Says = $blocked }
            @{ Run = @('a', '-ttar.gz', "$root\new.tar.gz", $fixture.Source); Exit = 2; Says = $blocked }
            @{ Run = @('a', '-ttar', "$root\new.tar", $fixture.Source); Exit = 2; Says = $blocked }
            @{ Run = @('a', '-t7z', "$root\new.7z", $fixture.Source); Exit = 2; Says = $blocked }
            @{ Run = @('l', $fixture.SevenZip); Exit = 2; Says = $blocked }
            @{ Run = @('x', "-o$root\7z", $fixture.SevenZip); Exit = 1; Says = $blocked }
            @{ Run = @('l', $fixture.Rar); Exit = 2; Says = $blocked }
            @{ Run = @('x', "-o$root\rar", $fixture.Rar); Exit = 1; Says = $blocked }
            @{ Run = @('l', $fixture.Zip); Exit = 0 }
            @{ Run = @('t', $fixture.Zip); Exit = 0 }
            @{ Run = @('x', "-o$root\zip", $fixture.Zip); Exit = 0 }
            @{ Run = @('a', "$root\new.zip", $fixture.Source); Exit = 0 }
        )
        $left = @(Get-ChildItem -LiteralPath $root -Force -Name | Sort-Object)
        Assert-Smoke (($left -join ',') -eq 'new.zip,zip') "under AllowedFormats=zip pakko wrote: $($left -join ', ')"

        $refused = Invoke-PolicyShell -Archive $fixture.TarGz -Folder (Join-Path $root 'shell-tgz') -Arguments @('--extract-folder')
        Assert-Smoke ($refused.Refused -and $refused.Left.Count -eq 0) "Shell extraction of a tar.gz under AllowedFormats=zip: wrote $($refused.Left -join ', ') $($refused.Text)"
        $allowed = Invoke-PolicyShell -Archive $fixture.Zip -Folder (Join-Path $root 'shell-zip') -Arguments @('--extract-folder')
        Assert-Smoke ($allowed.ExitCode -eq 0 -and $allowed.Left.Count -eq 1) "Shell extraction of a ZIP under AllowedFormats=zip: exit $($allowed.ExitCode) $($allowed.Text)"
        $created = Invoke-PolicyShell -Archive $fixture.Zip -Folder (Join-Path $root 'shell-tar') -Arguments @('--archive', '--format', 'tar')
        Assert-Smoke ($created.Left.Count -eq 0) "Shell created a tar under AllowedFormats=zip: $($created.Left -join ', ')"
    }
}

Add-SmokeStep 'POL-003' {
    $fixture = Get-PolicyFixture
    $root = Get-StepFolder 'pol003'
    Use-SmokePolicy -Values @{ BlockedFormats = @('zip') } -Body {
        $blocked = 'zip\) is blocked by Group Policy|Creating a zip archive is blocked by Group Policy'
        Assert-PakkoCase -Under 'BlockedFormats=zip' -Cases @(
            @{ Run = @('l', $fixture.Zip); Exit = 2; Says = $blocked }
            @{ Run = @('t', $fixture.Zip); Exit = 1; Says = $blocked }
            @{ Run = @('x', "-o$root\zip", $fixture.Zip); Exit = 1; Says = $blocked }
            @{ Run = @('a', "$root\new.zip", $fixture.Source); Exit = 2; Says = $blocked }
            @{ Run = @('l', $fixture.TarGz); Exit = 0 }
            @{ Run = @('x', "-o$root\7z", $fixture.SevenZip); Exit = 0 }
            @{ Run = @('a', '-ttar.gz', "$root\new.tar.gz", $fixture.Source); Exit = 0 }
            @{ Run = @('a', '-t7z', "$root\new.7z", $fixture.Source); Exit = 0 }
        )
        $left = @(Get-ChildItem -LiteralPath $root -Force -Name | Sort-Object)
        Assert-Smoke (($left -join ',') -eq '7z,new.7z,new.tar.gz') "under BlockedFormats=zip pakko wrote: $($left -join ', ')"

        foreach ($command in '--extract-folder', '--test') {
            $refused = Invoke-PolicyShell -Archive $fixture.Zip -Folder (Join-Path $root "shell$command") -Arguments @($command)
            Assert-Smoke ($refused.Refused -and $refused.Left.Count -eq 0) "Shell $command on a ZIP under BlockedFormats=zip: wrote $($refused.Left -join ', ') $($refused.Text)"
        }
        $created = Invoke-PolicyShell -Archive $fixture.Zip -Folder (Join-Path $root 'shell-tar') -Arguments @('--archive', '--format', 'tar')
        Assert-Smoke ($created.ExitCode -eq 0 -and ($created.Left -join ',') -eq 'a.tar') "Shell did not create a tar under BlockedFormats=zip: exit $($created.ExitCode), $($created.Left -join ', ') $($created.Text)"
    }

    Use-SmokePolicy -Values @{ BlockedFormats = @('sevenzip', 'rar') } -Body {
        $blocked = '(sevenzip|rar)\) is blocked by Group Policy'
        Assert-PakkoCase -Under 'BlockedFormats=sevenzip,rar' -Cases @(
            @{ Run = @('l', $fixture.SevenZip); Exit = 2; Says = $blocked }
            @{ Run = @('x', "-o$root\7z-blocked", $fixture.SevenZip); Exit = 1; Says = $blocked }
            @{ Run = @('l', $fixture.Rar); Exit = 2; Says = $blocked }
            @{ Run = @('x', "-o$root\rar-blocked", $fixture.Rar); Exit = 1; Says = $blocked }
            @{ Run = @('a', '-t7z', "$root\blocked.7z", $fixture.Source); Exit = 2; Says = 'is blocked by Group Policy' }
            @{ Run = @('t', $fixture.Zip); Exit = 0 }
            @{ Run = @('x', "-o$root\tgz", $fixture.TarGz); Exit = 0 }
        )
        Assert-Smoke (-not (Test-Path -LiteralPath "$root\7z-blocked") -and -not (Test-Path -LiteralPath "$root\rar-blocked")) 'a blocked 7z or RAR was extracted'
        Assert-Smoke (-not (Test-Path -LiteralPath "$root\blocked.7z")) 'a blocked 7z was created'
    }
}

Add-SmokeStep 'POL-004' {
    $fixture = Get-PolicyFixture
    $root = Get-StepFolder 'pol004'
    Use-SmokePolicy -Values @{ DisableTarExtraction = 1 } -Body {
        $extraction = 'tar\.exe-based extraction is disabled by Group Policy'
        $creation = 'tar\.exe-based archive creation is disabled by Group Policy'
        Assert-PakkoCase -Under 'DisableTarExtraction=1' -Cases @(
            @{ Run = @('l', $fixture.TarGz); Exit = 2; Says = $extraction }
            @{ Run = @('x', "-o$root\tgz", $fixture.TarGz); Exit = 1; Says = $extraction }
            @{ Run = @('l', $fixture.SevenZip); Exit = 2; Says = $extraction }
            @{ Run = @('x', "-o$root\7z", $fixture.SevenZip); Exit = 1; Says = $extraction }
            @{ Run = @('l', $fixture.Rar); Exit = 2; Says = $extraction }
            @{ Run = @('x', "-o$root\rar", $fixture.Rar); Exit = 1; Says = $extraction }
            @{ Run = @('a', '-ttar.gz', "$root\new.tar.gz", $fixture.Source); Exit = 2; Says = $creation }
            @{ Run = @('a', '-ttar', "$root\new.tar", $fixture.Source); Exit = 2; Says = $creation }
            @{ Run = @('a', '-t7z', "$root\new.7z", $fixture.Source); Exit = 2; Says = $creation }
            @{ Run = @('i'); Exit = 0; Says = 'tar\.exe: .*\(disabled by Group Policy\)' }
            @{ Run = @('x', "-o$root\zip", $fixture.Zip); Exit = 0 }
            @{ Run = @('a', "$root\new.zip", $fixture.Source); Exit = 0 }
        )
        $left = @(Get-ChildItem -LiteralPath $root -Force -Name | Sort-Object)
        Assert-Smoke (($left -join ',') -eq 'new.zip,zip') "under DisableTarExtraction=1 pakko wrote: $($left -join ', ')"

        # The refusal names the archive, not the folder it was to be extracted into (T-F326).
        $run = Invoke-Pakko @('x', "-o$root\named", $fixture.TarGz)
        Assert-Smoke ($run.Text -match 'a\.tar\.gz: ' -and $run.Text -notmatch 'named') "the refusal does not name the archive alone: $($run.Text)"

        $refused = Invoke-PolicyShell -Archive $fixture.TarGz -Folder (Join-Path $root 'shell-tgz') -Arguments @('--extract-folder')
        Assert-Smoke ($refused.Refused -and $refused.Left.Count -eq 0) "Shell extraction of a tar.gz under DisableTarExtraction=1: wrote $($refused.Left -join ', ') $($refused.Text)"
        $created = Invoke-PolicyShell -Archive $fixture.Zip -Folder (Join-Path $root 'shell-tar') -Arguments @('--archive', '--format', 'tar')
        Assert-Smoke ($created.Left.Count -eq 0) "Shell created a tar under DisableTarExtraction=1: $($created.Left -join ', ')"
    }
}

Add-SmokeStep 'POL-005' {
    $fixture = Get-PolicyFixture
    $root = Get-StepFolder 'pol005'
    # An archive with a set, and a damaged copy of it, from before the policy.
    $whole = Join-Path $root 'whole\data.zip'
    $damaged = Join-Path $root 'damaged\data.zip'
    $null = [System.IO.Directory]::CreateDirectory((Split-Path -Parent $whole))
    $made = Invoke-Pakko @('a', '-rr20', '-mx=0', $whole, (Join-Path (Get-Fixture Tree) 'random.bin'))
    Assert-Smoke ($made.ExitCode -eq 0) "a -rr20: exit $($made.ExitCode) $($made.Text)"
    Copy-Item -LiteralPath (Split-Path -Parent $whole) -Destination (Split-Path -Parent $damaged) -Recurse
    Set-DamagedRange -Path $damaged -Offset 100KB -Count 8KB
    $before = Invoke-Pakko @('t', $whole)
    Assert-Smoke ($before.Text -match 'recovery data intact') "t before the policy: $($before.Text)"

    Use-SmokePolicy -Values @{ DisableRecoveryData = 1 } -Body {
        $disabled = 'Recovery data is disabled by Group Policy'
        $new = Join-Path $root 'new'
        $null = [System.IO.Directory]::CreateDirectory($new)
        Assert-PakkoCase -Under 'DisableRecoveryData=1' -Cases @(
            @{ Run = @('a', '-rr5', "$new\rr.zip", $fixture.Source); Exit = 2; Says = $disabled }
            @{ Run = @('r', $damaged); Exit = 2; Says = $disabled }
            @{ Run = @('a', "$new\plain.zip", $fixture.Source); Exit = 0 }
        )
        Assert-Smoke ((@(Get-ChildItem -LiteralPath $new -Force -Name) -join ',') -eq 'plain.zip') "under DisableRecoveryData=1 pakko a wrote: $(@(Get-ChildItem -LiteralPath $new -Force -Name) -join ', ')"
        $tested = Invoke-Pakko @('t', $whole)
        Assert-Smoke ($tested.ExitCode -eq 0 -and $tested.Text -notmatch 'recovery') "t under DisableRecoveryData=1 still looks at the set: exit $($tested.ExitCode) $($tested.Text)"

        $shell = Invoke-Shell -Arguments @('--recovery-repair', $damaged) -Until 'data\.zip: ' -TimeoutSeconds 20
        Assert-Smoke ($shell.Matched -or $shell.Exited) "Shell --recovery-repair under DisableRecoveryData=1 neither refused nor ended: $($shell.Text)"
        $copies = @(Get-ChildItem -LiteralPath (Split-Path -Parent $damaged) -Force -Name -Filter '*repaired*')
        Assert-Smoke ($copies.Count -eq 0) "under DisableRecoveryData=1 a repaired copy was written: $($copies -join ', ')"
    }
}

Add-SmokeStep 'POL-006' {
    $root = Get-StepFolder 'pol006'
    $zip = Join-Path $root 'data.zip'
    $made = Invoke-Pakko @('a', '-rr20', '-mx=0', $zip, (Join-Path (Get-Fixture Tree) 'random.bin'))
    Assert-Smoke ($made.ExitCode -eq 0) "a -rr20: exit $($made.ExitCode) $($made.Text)"
    $original = Get-Sha256 $zip
    Set-DamagedRange -Path $zip -Offset 100KB -Count 8KB

    # Repair works on the archive's bytes: a format the policy blocks is still repaired.
    Use-SmokePolicy -Values @{ BlockedFormats = @('zip') } -Body {
        $listed = Invoke-Pakko @('l', $zip)
        Assert-Smoke ($listed.ExitCode -eq 2 -and $listed.Text -match 'blocked by Group Policy') "l on a ZIP under BlockedFormats=zip: exit $($listed.ExitCode) $($listed.Text)"
        $repair = Invoke-Pakko @('r', $zip)
        Assert-Smoke ($repair.ExitCode -eq 0) "r on a ZIP under BlockedFormats=zip: exit $($repair.ExitCode) $($repair.Text)"
        Assert-Smoke ((Get-Sha256 (Join-Path $root 'data.repaired.zip')) -eq $original) 'the copy repaired under BlockedFormats=zip is not equal to the original'
    }
}

Add-SmokeStep 'POL-007' {
    $root = Get-StepFolder 'pol007'
    $tree = Get-Fixture Tree
    Use-SmokePolicy -Fips 1 -Body {
        $enabled = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa\FipsAlgorithmPolicy').Enabled
        Assert-Smoke ($enabled -eq 1) "the FIPS flag is $enabled while the row runs"
        # PAR2 fixes MD5 as its hash; under FIPS .NET's own MD5 would throw.
        $zip = Join-Path $root 'data.zip'
        $made = Invoke-Pakko @('a', '-rr20', '-mx=0', $zip, (Join-Path $tree 'random.bin'))
        Assert-Smoke ($made.ExitCode -eq 0) "a -rr20 under FIPS: exit $($made.ExitCode) $($made.Text)"
        $original = Get-Sha256 $zip
        $intact = Invoke-Pakko @('t', $zip)
        Assert-Smoke ($intact.ExitCode -eq 0 -and $intact.Text -match 'recovery data intact') "t under FIPS: exit $($intact.ExitCode) $($intact.Text)"
        Set-DamagedRange -Path $zip -Offset 100KB -Count 8KB
        $repair = Invoke-Pakko @('r', $zip)
        Assert-Smoke ($repair.ExitCode -eq 0 -and (Get-Sha256 (Join-Path $root 'data.repaired.zip')) -eq $original) "r under FIPS: exit $($repair.ExitCode) $($repair.Text)"

        $hashed = Invoke-Pakko @('h', '-scrcSHA256', $zip)
        Assert-Smoke ($hashed.ExitCode -eq 0 -and $hashed.Output -match "(?i)\b$(Get-Sha256 $zip)\b") "h -scrcSHA256 under FIPS: exit $($hashed.ExitCode) $($hashed.Text)"

        $locked = Join-Path $root 'locked.zip'
        $made = Invoke-Pakko @('a', $locked, '-pFips-1', '-mem=AES256', $tree)
        Assert-Smoke ($made.ExitCode -eq 0) "a -mem=AES256 under FIPS: exit $($made.ExitCode) $($made.Text)"
        $out = Join-Path $root 'out'
        $run = Invoke-Pakko @('x', $locked, '-pFips-1', "-o$out")
        Assert-Smoke ($run.ExitCode -eq 0) "x of the AES-256 ZIP under FIPS: exit $($run.ExitCode) $($run.Text)"
        Assert-Smoke ((Get-TreeHash (Join-Path $out (Split-Path -Leaf $tree))) -eq (Get-TreeHash $tree)) 'the files from the AES-256 ZIP made under FIPS differ from the sources'
    }
}

Add-SmokeStep 'POL-008' -AtEnd {
    if (-not $Smoke.Elevated.Folder) { Skip-Smoke 'no row of this run asked for the elevated helper' }
    if ($Smoke.Elevated.Declined) { Skip-Smoke $Smoke.Elevated.Declined }
    $null = Stop-SmokeElevated
    foreach ($before in Get-ChildItem -LiteralPath $Smoke.Elevated.Folder -Filter 'before-*.reg') {
        $after = $before.FullName -replace 'before-(\w+)\.reg$', 'after-$1.reg'
        Assert-Smoke ((Test-Path -LiteralPath $after) -and (Get-Sha256 $before.FullName) -eq (Get-Sha256 $after)) "the registry is not as it was: compare $($before.FullName) with $after"
    }
    $log = Get-Content -LiteralPath (Join-Path $Smoke.Elevated.Folder 'host.log')
    "$(@($log | Where-Object { $_ -match 'request \d+ : policy' }).Count) policy changes, the two keys as before"
}

Add-SmokeStep 'PKG-005' {
    $msix = Get-ChildItem -LiteralPath (Join-Path $Smoke.Repo 'src\Archiver.App\AppPackages') -Recurse -Filter "*_$($Smoke.Package.Version)_*.msix" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $msix) { Skip-Smoke "the .msix of the installed package $($Smoke.Package.Version) is not under src\Archiver.App\AppPackages: run Deploy.ps1" }
    $report = Join-Path (Get-StepFolder 'pkg005') 'wack.xml'
    $null = Send-ElevatedRequest @{ Kind = 'appcert'; Package = $msix.FullName; Report = $report } -TimeoutSeconds 2400
    Assert-Smoke (Test-Path -LiteralPath $report) 'appcert.exe wrote no report'

    $xml = [xml](Get-Content -LiteralPath $report -Raw)
    $found = @($xml.SelectNodes('//TEST') | ForEach-Object {
            $result = $_.SelectSingleNode('RESULT').InnerText.Trim()
            if ($result -ne 'PASS') { "$($_.GetAttribute('NAME'))=$result" }
        })
    Assert-Smoke ($xml.SelectNodes('//TEST').Count -gt 10) "the report has $($xml.SelectNodes('//TEST').Count) tests"

    # The findings every run so far has had, one "<test name>=<result>" per line.
    $known = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'wack-known.txt') | Where-Object { $_ -and -not $_.StartsWith('#') })
    $new = @($found | Where-Object { $known -notcontains $_ })
    Assert-Smoke ($new.Count -eq 0) "findings the last record did not have: $($new -join '; ')"
    $gone = @($known | Where-Object { $found -notcontains $_ })
    "overall $($xml.DocumentElement.GetAttribute('OVERALL_RESULT')), $($xml.SelectNodes('//TEST').Count) tests; known: $($found -join '; ')$($gone ? "; no longer found: $($gone -join '; ')" : '')"
}
