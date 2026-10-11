#Requires -Version 7.0
# Steps of docs/SMOKE.md's PAR rows of the release tier. Dot-sourced by scripts/Invoke-Smoke.ps1.

# A 20 MB file that does not compress.
function Get-Par2Payload {
    if (-not $Smoke.Fixtures.ContainsKey('Par2Payload')) {
        $path = Join-Path (Get-StepFolder 'fixtures') 'payload.bin'
        Write-RandomFile -Path $path -Length 20MB -Seed 9
        $Smoke.Fixtures['Par2Payload'] = $path
    }
    $Smoke.Fixtures['Par2Payload']
}

# A private copy of data.zip (the payload, stored) with its -rr5 set: 2000 blocks, 100 recovery
# blocks. The steps damage it.
function New-Par2Case {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Writes a fixture inside the run''s own temporary folder.')]
    param([Parameter(Mandatory)][string]$Name)
    if (-not $Smoke.Fixtures.ContainsKey('Par2Set')) {
        $source = Join-Path (Get-StepFolder 'fixtures') 'par2set'
        $null = [System.IO.Directory]::CreateDirectory($source)
        $made = Invoke-Pakko @('a', '-rr5', '-mx=0', (Join-Path $source 'data.zip'), (Get-Par2Payload))
        Assert-Smoke ($made.ExitCode -eq 0) "fixture data.zip with a set: exit $($made.ExitCode) $($made.Text)"
        $Smoke.Fixtures['Par2Set'] = $source
    }
    $folder = Get-StepFolder $Name
    Copy-Item -Path (Join-Path $Smoke.Fixtures['Par2Set'] '*') -Destination $folder
    Join-Path $folder 'data.zip'
}

function Get-Par2Volume {
    param([Parameter(Mandatory)][string]$Archive)
    $volumes = @(Get-ChildItem -LiteralPath (Split-Path -Parent $Archive) -Filter "$(Split-Path -Leaf $Archive).vol*.par2")
    Assert-Smoke ($volumes.Count -eq 1) "$(Split-Path -Leaf $Archive) has $($volumes.Count) recovery volumes, expected one"
    $volumes[0].FullName
}

# pakko r, which must never change the archive it is asked to repair (PAR-006).
function Invoke-Par2Repair {
    param([Parameter(Mandatory)][string]$Archive, [string[]]$More = @())
    $before = Get-Sha256 $Archive
    $run = Invoke-Pakko (@('r', $Archive) + $More) -TimeoutSeconds 300
    Assert-Smoke ((Get-Sha256 $Archive) -eq $before) "pakko r changed $(Split-Path -Leaf $Archive) itself"
    $run
}

function Get-RepairedCopy {
    param([Parameter(Mandatory)][string]$Folder)
    @(Get-ChildItem -LiteralPath $Folder -Force -Recurse -ErrorAction SilentlyContinue | Where-Object Name -Like '*repaired*')
}

# Changes the data of every recovery block in a PAR2 file and gives each packet the MD5 of its
# new content, so that the packets still read as whole. PAR2 fixes MD5 as its packet hash.
function Set-ForgedRecoveryData {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Forges a fixture inside the run''s own temporary folder.')]
    param([Parameter(Mandatory)][string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $ascii = [System.Text.Encoding]::ASCII
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $forged = 0
        $position = 0
        while ($position + 64 -le $bytes.Length -and $ascii.GetString($bytes, $position, 8) -eq "PAR2`0PKT") {
            $length = [int][System.BitConverter]::ToUInt64($bytes, $position + 8)
            if ($length -lt 64 -or $position + $length -gt $bytes.Length) { break }
            # Header: magic 8, length 8, MD5 16 (of everything after it), set ID 16, type 16;
            # a recovery packet's body is its exponent (4) and the block.
            if ($ascii.GetString($bytes, $position + 48, 16) -eq "PAR 2.0`0RecvSlic") {
                $bytes[$position + 100] = $bytes[$position + 100] -bxor 0xFF
                $hash = $md5.ComputeHash($bytes, $position + 32, $length - 32)
                [System.Array]::Copy($hash, 0, $bytes, $position + 16, 16)
                $forged++
            }
            $position += $length
        }
    }
    finally { $md5.Dispose() }
    Assert-Smoke ($forged -gt 0) "no recovery packet was found in $(Split-Path -Leaf $Path)"
    [System.IO.File]::WriteAllBytes($Path, $bytes)
}

Add-SmokeStep 'PAR-004' {
    $oracles = Join-Path $Smoke.Repo 'artifacts\par2-oracles'
    $turbo = Join-Path $oracles 'par2cmdline-turbo\par2.exe'
    $par2j = Join-Path $oracles 'multipar\par2j64.exe'
    if (-not ((Test-Path -LiteralPath $turbo) -and (Test-Path -LiteralPath $par2j))) { Skip-Smoke 'par2cmdline-turbo and MultiPar are not downloaded: run scripts\Get-Par2Oracles.ps1' }

    # par2cmdline-turbo on Pakko's set.
    $zip = New-Par2Case 'par004turbo'
    $original = Get-Sha256 $zip
    $verified = Invoke-Tool -Exe $turbo -Arguments @('verify', '-q', '-q', "$zip.par2")
    Assert-Smoke ($verified.ExitCode -eq 0) "par2cmdline-turbo verify on Pakko's set: exit $($verified.ExitCode) $($verified.Text)"
    Set-DamagedRange -Path $zip -Offset 5MB -Count 300KB
    $repaired = Invoke-Tool -Exe $turbo -Arguments @('repair', '-q', '-q', "$zip.par2")
    Assert-Smoke ($repaired.ExitCode -eq 0 -and (Get-Sha256 $zip) -eq $original) "par2cmdline-turbo repair with Pakko's set: exit $($repaired.ExitCode)"

    # MultiPar on Pakko's set; par2j exits with 16 after a repair.
    $zip = New-Par2Case 'par004multipar'
    $verified = Invoke-Tool -Exe $par2j -Arguments @('v', "$zip.par2")
    Assert-Smoke ($verified.ExitCode -eq 0) "par2j verify on Pakko's set: exit $($verified.ExitCode) $($verified.Text)"
    Set-DamagedRange -Path $zip -Offset 5MB -Count 300KB
    $repaired = Invoke-Tool -Exe $par2j -Arguments @('r', "$zip.par2")
    Assert-Smoke ($repaired.ExitCode -eq 16 -and (Get-Sha256 $zip) -eq $original) "par2j repair with Pakko's set: exit $($repaired.ExitCode)"

    # Pakko on a MultiPar set: damage in the middle and the tail cut off, repaired into -o<dir>.
    $theirs = Get-StepFolder 'par004theirs'
    $file = Join-Path $theirs 'data.zip'
    $made = Invoke-Pakko @('a', '-mx=0', $file, (Get-Par2Payload))
    Assert-Smoke ($made.ExitCode -eq 0) "a: exit $($made.ExitCode) $($made.Text)"
    $original = Get-Sha256 $file
    $created = Invoke-Tool -Exe $par2j -Arguments @('c', '/rr10', "$file.par2", $file)
    Assert-Smoke ($created.ExitCode -eq 0) "par2j create: exit $($created.ExitCode) $($created.Text)"
    $intact = Invoke-Pakko @('t', $file)
    Assert-Smoke ($intact.ExitCode -eq 0 -and $intact.Text -match 'recovery data intact') "t on a MultiPar set: exit $($intact.ExitCode) $($intact.Text)"

    Set-DamagedRange -Path $file -Offset 3MB -Count 200KB
    $stream = [System.IO.File]::Open($file, [System.IO.FileMode]::Open)
    try { $stream.SetLength($stream.Length - 100KB) }
    finally { $stream.Dispose() }
    $verdict = Invoke-Pakko @('t', $file)
    Assert-Smoke ($verdict.ExitCode -eq 2 -and $verdict.Text -match 'can repair it') "t on the damaged and cut archive: exit $($verdict.ExitCode) $($verdict.Text)"
    $out = Join-Path $theirs 'out'
    $repair = Invoke-Par2Repair $file @("-o$out")
    Assert-Smoke ($repair.ExitCode -eq 0) "r -o from a MultiPar set: exit $($repair.ExitCode) $($repair.Text)"
    Assert-Smoke ((Get-Sha256 (Join-Path $out 'data.repaired.zip')) -eq $original) 'the copy repaired from a MultiPar set is not equal to the original'
    Assert-Smoke (@(Get-ChildItem -LiteralPath $theirs -File -Filter '*repaired*').Count -eq 0) 'r -o also wrote a copy next to the archive'
}

Add-SmokeStep 'PAR-005' {
    $fresh = Invoke-Pakko @('t', (New-Par2Case 'par005fresh'))
    Assert-Smoke ($fresh.Text -match '\((\d+) blocks, (\d+) recovery blocks\)') "t on a fresh set: $($fresh.Text)"
    $recoveryBlocks = [int]$Matches[2]

    # A damaged main file: every packet in it is repeated in the volume.
    $zip = New-Par2Case 'par005main'
    $original = Get-Sha256 $zip
    Set-DamagedRange -Path "$zip.par2" -Offset 100 -Count 2000
    Set-DamagedRange -Path $zip -Offset 5MB -Count 100KB
    $repair = Invoke-Par2Repair $zip
    Assert-Smoke ($repair.ExitCode -eq 0 -and (Get-Sha256 (Join-Path (Split-Path -Parent $zip) 'data.repaired.zip')) -eq $original) "r with a damaged main file: exit $($repair.ExitCode) $($repair.Text)"

    # A damaged volume: the recovery block that was hit is not counted, the rest repair.
    $zip = New-Par2Case 'par005volume'
    $volume = Get-Par2Volume $zip
    Set-DamagedRange -Path $volume -Offset ([long]((Get-Item -LiteralPath $volume).Length / 2)) -Count 4000
    $verdict = Invoke-Pakko @('t', $zip)
    Assert-Smoke ($verdict.Text -match '(\d+) recovery blocks' -and [int]$Matches[1] -lt $recoveryBlocks -and [int]$Matches[1] -gt 0) "t with a damaged volume still counts $recoveryBlocks recovery blocks: $($verdict.Text)"
    Set-DamagedRange -Path $zip -Offset 5MB -Count 100KB
    $repair = Invoke-Par2Repair $zip
    Assert-Smoke ($repair.ExitCode -eq 0 -and (Get-Sha256 (Join-Path (Split-Path -Parent $zip) 'data.repaired.zip')) -eq $original) "r with a damaged volume: exit $($repair.ExitCode) $($repair.Text)"

    # The cases where no repair is possible: a verdict of its own for each, and no copy.
    $refused = [ordered]@{}

    # The volume is missing, as after a set that was only half written.
    $zip = New-Par2Case 'par005half'
    Remove-Item -LiteralPath (Get-Par2Volume $zip)
    Set-DamagedRange -Path $zip -Offset 5MB -Count 100KB
    $refused['a set without its volume'] = @{ Archive = $zip; Test = 2; Repair = 2; Says = 'beyond what its recovery data can repair \(0 recovery blocks\)' }

    # The set of another archive, under this one's name; the archive itself is whole.
    $zip = New-Par2Case 'par005other'
    $other = Join-Path (Get-StepFolder 'par005other-source') 'data.zip'
    $made = Invoke-Pakko @('a', '-rr5', '-mx=0', $other, (Get-Par2Payload), (Get-Fixture Tree))
    Assert-Smoke ($made.ExitCode -eq 0) "a: exit $($made.ExitCode) $($made.Text)"
    Remove-Item -LiteralPath (Get-Par2Volume $zip)
    Copy-Item -LiteralPath "$other.par2", (Get-Par2Volume $other) -Destination (Split-Path -Parent $zip) -Force
    $refused['the set of another archive'] = @{ Archive = $zip; Test = 1; Repair = 1; Says = 'does not match the archive' }

    # Forged recovery blocks whose packets carry a right hash: only the repaired copy shows it.
    $zip = New-Par2Case 'par005forged'
    Set-ForgedRecoveryData (Get-Par2Volume $zip)
    Set-DamagedRange -Path $zip -Offset 5MB -Count 100KB
    $refused['forged recovery blocks'] = @{ Archive = $zip; Test = 2; Repair = 2; Says = 'did not match the recovery data and was not kept' }

    # More damage than the set can repair.
    $zip = New-Par2Case 'par005beyond'
    Set-DamagedRange -Path $zip -Offset 2MB -Count 6MB
    $refused['damage beyond the set'] = @{ Archive = $zip; Test = 2; Repair = 2; Says = "beyond what its recovery data can repair \($recoveryBlocks recovery blocks\)" }

    foreach ($what in $refused.Keys) {
        $case = $refused[$what]
        $verdict = Invoke-Pakko @('t', $case.Archive) -TimeoutSeconds 300
        Assert-Smoke ($verdict.ExitCode -eq $case.Test) "t on ${what}: exit $($verdict.ExitCode) $($verdict.Text)"
        $repair = Invoke-Par2Repair $case.Archive
        Assert-Smoke ($repair.ExitCode -eq $case.Repair -and $repair.Text -match $case.Says) "r on ${what}: exit $($repair.ExitCode) $($repair.Text)"
        $left = @(Get-ChildItem -LiteralPath (Split-Path -Parent $case.Archive) -Force -Name | Where-Object { $_ -notlike 'data.zip*' })
        Assert-Smoke ($left.Count -eq 0) "r on $what left: $($left -join ', ')"
    }
}

Add-SmokeStep 'PAR-006' {
    $zip = New-Par2Case 'par006'
    $folder = Split-Path -Parent $zip
    $original = Get-Sha256 $zip
    Set-DamagedRange -Path $zip -Offset 5MB -Count 100KB

    # No new file may be made in the folder; what is in it can still be read. (Denying all of
    # "write" would take the right to wait on a file handle, and with it reading.)
    $icacls = Join-Path $env:SystemRoot 'System32\icacls.exe'
    $user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $null = Invoke-Tool -Exe $icacls -Arguments @($folder, '/deny', "${user}:(WD,AD)")
    $out = Join-Path $Smoke.Work 'par006out'
    try {
        $refused = Invoke-Par2Repair $zip
        Assert-Smoke ($refused.ExitCode -eq 2 -and $refused.Text -match 'could not be written' -and $refused.Text -match '-o') "r in a folder closed for new files: exit $($refused.ExitCode) $($refused.Text)"
        $repair = Invoke-Par2Repair $zip @("-o$out")
        Assert-Smoke ($repair.ExitCode -eq 0) "r -o from a folder closed for new files: exit $($repair.ExitCode) $($repair.Text)"
    }
    finally { $null = Invoke-Tool -Exe $icacls -Arguments @($folder, '/remove:d', $user) }
    Assert-Smoke ((Get-Sha256 (Join-Path $out 'data.repaired.zip')) -eq $original) 'the copy written with -o is not equal to the original'
    Assert-Smoke (@(Get-RepairedCopy $folder).Count -eq 0) 'a repaired copy appeared in the folder closed for new files'
}

Add-SmokeStep 'PAR-007' {
    $root = Get-StepFolder 'par007'
    $sources = @((Get-Fixture Tree), (Get-Par2Payload))
    $cases = @('zip', 'tar', 'tar.gz', 'tar.bz2', 'tar.xz', 'tar.zst', 'tar.lzma' | ForEach-Object { @{ Name = $_; Extension = $_; Switches = @("-t$_") } })
    $cases += @{ Name = 'aes'; Extension = 'zip'; Switches = @('-pSecret-1', '-mem=AES256') }

    foreach ($case in $cases) {
        $folder = Join-Path $root $case.Name
        $null = [System.IO.Directory]::CreateDirectory($folder)
        $archive = Join-Path $folder "data.$($case.Extension)"
        $made = Invoke-Pakko (@('a', '-rr20') + $case.Switches + @($archive) + $sources)
        Assert-Smoke ($made.ExitCode -eq 0) "a -rr20 $($case.Name): exit $($made.ExitCode) $($made.Text)"
        $names = @(Get-ChildItem -LiteralPath $folder -Force -Name | Sort-Object)
        Assert-Smoke ($names.Count -eq 3 -and $names[0] -eq "data.$($case.Extension)" -and $names[1] -eq "data.$($case.Extension).par2" -and $names[2] -like "data.$($case.Extension).vol*.par2") "a -rr20 $($case.Name) wrote: $($names -join ', ')"
        $original = Get-Sha256 $archive

        Set-DamagedRange -Path $archive -Offset 1MB -Count 50KB
        # No password is given from here on: the set covers the archive's bytes.
        $verdict = Invoke-Pakko @('t', $archive)
        Assert-Smoke ($verdict.ExitCode -eq 2 -and $verdict.Text -match 'can repair it') "t on the damaged $($case.Name): exit $($verdict.ExitCode) $($verdict.Text)"
        $repair = Invoke-Par2Repair $archive
        Assert-Smoke ($repair.ExitCode -eq 0) "r $($case.Name): exit $($repair.ExitCode) $($repair.Text)"
        $copy = Join-Path $folder "data.repaired.$($case.Extension)"
        Assert-Smoke ((Test-Path -LiteralPath $copy) -and (Get-Sha256 $copy) -eq $original) "the repaired $($case.Name) is not equal to the original"
    }
}

Add-SmokeStep 'PAR-008' {
    $big = Get-Fixture Big
    $signal = Send-CtrlC -Prepare

    # The archive is in place under its name while the set is still being written beside it.
    $setBeingWritten = {
        (Test-Path -LiteralPath (Join-Path $folder 'data.zip')) -and
        @(Get-ChildItem -LiteralPath $folder -Force | Where-Object Name -NE 'data.zip').Count -gt 0
    }
    # What is left must not pass for a set, and the same command with -y must end with a whole one.
    function Assert-NoSetThenRerun {
        param([string]$Folder, [string]$After)
        $zip = Join-Path $Folder 'data.zip'
        $named = @(Get-ChildItem -LiteralPath $Folder -Force -Name -Filter '*.par2')
        Assert-Smoke ($named.Count -eq 0) "after $After a file is named as recovery data: $($named -join ', ')"
        $tested = Invoke-Pakko @('t', $zip)
        Assert-Smoke ($tested.ExitCode -eq 0 -and $tested.Text -notmatch 'recovery') "t after ${After}: exit $($tested.ExitCode) $($tested.Text)"
        $repair = Invoke-Par2Repair $zip
        Assert-Smoke ($repair.ExitCode -ne 0 -and $repair.Text -match 'No recovery data') "r after ${After}: exit $($repair.ExitCode) $($repair.Text)"

        $again = Invoke-Pakko @('a', '-y', '-rr20', '-mx=0', $zip, $big)
        Assert-Smoke ($again.ExitCode -eq 0) "a -y -rr20 after ${After}: exit $($again.ExitCode) $($again.Text)"
        $tested = Invoke-Pakko @('t', $zip)
        Assert-Smoke ($tested.ExitCode -eq 0 -and $tested.Text -match 'recovery data intact') "t after the second run after ${After}: exit $($tested.ExitCode) $($tested.Text)"
        $left = @(Get-ChildItem -LiteralPath $Folder -Force -Name | Where-Object { $_ -notlike 'data.zip*' })
        Assert-Smoke ($left.Count -eq 0) "the second run after $After did not replace: $($left -join ', ')"
    }

    $folder = Get-StepFolder 'par008kill'
    $process = Start-PakkoInConsole @('a', '-rr20', '-mx=0', (Join-Path $folder 'data.zip'), $big)
    try {
        Wait-Smoke $setBeingWritten 'pakko a -rr20 starting on the set' 120
        $process.Kill()
        $null = $process.WaitForExit(10000)
    }
    finally { $process.Dispose() }
    Assert-Smoke (@(Get-ChildItem -LiteralPath $folder -Force).Count -gt 1) 'pakko a -rr20 was killed after its set was done, so the next checks would prove nothing'
    Assert-NoSetThenRerun $folder 'a killed a -rr20'

    $folder = Get-StepFolder 'par008cancel'
    $process = Start-PakkoInConsole @('a', '-rr20', '-mx=0', (Join-Path $folder 'data.zip'), $big)
    try {
        Wait-Smoke $setBeingWritten 'pakko a -rr20 starting on the set' 120
        Send-CtrlC -Signal $signal -Process $process
        Assert-Smoke ($process.WaitForExit(30000)) 'pakko a -rr20 did not end within 30 s of Ctrl+C'
        Assert-Smoke ($process.ExitCode -eq 255) "pakko a -rr20 after Ctrl+C: exit $($process.ExitCode), expected 255"
    }
    finally {
        if (-not $process.HasExited) { $process.Kill() }
        $process.Dispose()
    }
    $left = @(Get-ChildItem -LiteralPath $folder -Force -Name)
    Assert-Smoke (($left -join ',') -eq 'data.zip') "after Ctrl+C during the set the folder holds: $($left -join ', ')"
    Assert-NoSetThenRerun $folder 'a cancelled a -rr20'
}

Add-SmokeStep 'PAR-009' {
    $root = Get-StepFolder 'par009'
    $tree = Get-Fixture Tree
    foreach ($format in 'zip', 'tar.gz') {
        $folder = Join-Path $root $format
        $null = [System.IO.Directory]::CreateDirectory($folder)
        $archive = Join-Path $folder "data.$format"
        $made = Invoke-Pakko @('a', '-rr5', "-t$format", $archive, (Get-Par2Payload))
        Assert-Smoke ($made.ExitCode -eq 0 -and @(Get-ChildItem -LiteralPath $folder -Filter '*.par2').Count -eq 2) "a -rr5 -t${format}: exit $($made.ExitCode) $($made.Text)"

        $rewritten = Invoke-Pakko @('a', '-y', "-t$format", $archive, $tree)
        Assert-Smoke ($rewritten.ExitCode -eq 0) "a -y -t$format over an archive with a set: exit $($rewritten.ExitCode) $($rewritten.Text)"
        $names = @(Get-ChildItem -LiteralPath $folder -Force -Name)
        Assert-Smoke (($names -join ',') -eq "data.$format") "after a -y -t$format without -rr the folder holds: $($names -join ', ')"

        # A tar.gz cannot be tested (exit 1); neither may be called damaged.
        $tested = Invoke-Pakko @('t', $archive)
        Assert-Smoke ($tested.ExitCode -eq ($format -eq 'zip' ? 0 : 1) -and $tested.Text -notmatch 'damaged|recovery') "t after the rewrite as ${format}: exit $($tested.ExitCode) $($tested.Text)"
    }
}
