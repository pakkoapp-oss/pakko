#Requires -Version 7.0
# Steps of docs/SMOKE.md's PAR rows. Dot-sourced by scripts/Invoke-Smoke.ps1.

# A private copy of the 200 MB ZIP with its set: the steps damage it.
function Copy-BigZipWithSet {
    param([Parameter(Mandatory)][string]$Folder)
    $source = Split-Path -Parent (Get-Fixture BigZipWithSet)
    Copy-Item -Path (Join-Path $source '*') -Destination $Folder
    Join-Path $Folder 'big.zip'
}

Add-SmokeStep 'PAR-001' {
    $folder = Get-StepFolder 'par001'
    $zip = Copy-BigZipWithSet $folder
    $original = Get-Sha256 $zip
    Assert-Smoke ((Test-Path -LiteralPath "$zip.par2") -and @(Get-ChildItem -LiteralPath $folder -Filter 'big.zip.vol*.par2').Count -eq 1) 'a -rr5 did not write big.zip.par2 and one volume'

    $intact = Invoke-Pakko @('t', $zip)
    Assert-Smoke ($intact.ExitCode -eq 0 -and $intact.Text -match 'recovery data intact') "t on the fresh archive: exit $($intact.ExitCode) $($intact.Text)"

    Set-DamagedRange -Path $zip -Offset 50MB -Count 8MB
    $damaged = Get-Sha256 $zip
    $verdict = Invoke-Pakko @('t', $zip)
    Assert-Smoke ($verdict.ExitCode -eq 2 -and $verdict.Text -match 'can repair it') "t on the damaged archive: exit $($verdict.ExitCode) $($verdict.Text)"

    $repair = Invoke-Pakko @('r', $zip)
    Assert-Smoke ($repair.ExitCode -eq 0) "r: exit $($repair.ExitCode) $($repair.Text)"
    $copy = Join-Path $folder 'big.repaired.zip'
    Assert-Smoke ((Test-Path -LiteralPath $copy) -and (Get-Sha256 $copy) -eq $original) 'the repaired copy is not equal to the original archive'
    Assert-Smoke ((Get-Sha256 $zip) -eq $damaged) 'r changed the damaged archive itself'
}

Add-SmokeStep 'PAR-002' {
    if (-not (Test-Path -LiteralPath $Smoke.Par2)) { Skip-Smoke 'par2cmdline is not downloaded: run scripts\Get-Par2Oracles.ps1' }

    # par2cmdline on Pakko's set.
    $ours = Get-StepFolder 'par002ours'
    $zip = Copy-BigZipWithSet $ours
    $original = Get-Sha256 $zip
    $verified = Invoke-Tool -Exe $Smoke.Par2 -Arguments @('verify', '-q', '-q', "$zip.par2")
    Assert-Smoke ($verified.ExitCode -eq 0) "par2 verify on Pakko's set: exit $($verified.ExitCode) $($verified.Text)"
    Set-DamagedRange -Path $zip -Offset 20MB -Count 4MB
    $repaired = Invoke-Tool -Exe $Smoke.Par2 -Arguments @('repair', '-q', '-q', "$zip.par2")
    Assert-Smoke ($repaired.ExitCode -eq 0 -and (Get-Sha256 $zip) -eq $original) "par2 repair with Pakko's set: exit $($repaired.ExitCode)"

    # Pakko on par2cmdline's set.
    $theirs = Get-StepFolder 'par002theirs'
    $file = Join-Path $theirs 'data.zip'
    $made = Invoke-Pakko @('a', '-mx=0', $file, (Get-Fixture Tree))
    Assert-Smoke ($made.ExitCode -eq 0) "a: exit $($made.ExitCode) $($made.Text)"
    $original = Get-Sha256 $file
    $created = Invoke-Tool -Exe $Smoke.Par2 -Arguments @('create', '-q', '-q', '-r10', "$file.par2", $file)
    Assert-Smoke ($created.ExitCode -eq 0) "par2 create: exit $($created.ExitCode) $($created.Text)"
    Set-DamagedRange -Path $file -Offset 10KB -Count 4KB
    $repair = Invoke-Pakko @('r', $file)
    Assert-Smoke ($repair.ExitCode -eq 0) "r from par2cmdline's set: exit $($repair.ExitCode) $($repair.Text)"
    Assert-Smoke ((Get-Sha256 (Join-Path $theirs 'data.repaired.zip')) -eq $original) 'the copy repaired from par2cmdline''s set is not equal to the original'
}

Add-SmokeStep 'PAR-003' {
    $folder = Get-StepFolder 'par003'
    $zip = Copy-BigZipWithSet $folder
    $original = Get-Sha256 $zip

    # The verdict is in the user's language; its numbers are not: 2000 blocks, 100 recovery blocks.
    foreach ($path in $zip, "$zip.par2") {
        $run = Invoke-Shell -Arguments @('--recovery-verify', $path) -Until 'big\.zip.*2000.*100' -TimeoutSeconds 60
        Assert-Smoke $run.Matched "--recovery-verify $(Split-Path -Leaf $path) did not report the set: $($run.Text)"
    }

    Set-DamagedRange -Path $zip -Offset 50MB -Count 8MB
    $copy = Join-Path $folder 'big.repaired.zip'
    $run = Invoke-Shell -Arguments @('--recovery-repair', $zip) -Until 'big\.repaired\.zip' -TimeoutSeconds 120
    Assert-Smoke $run.Matched "--recovery-repair on the archive did not name the copy: $($run.Text)"
    Assert-Smoke ((Get-Sha256 $copy) -eq $original) '--recovery-repair on the archive: the copy is not equal to the original'

    Remove-Item -LiteralPath $copy
    $run = Invoke-Shell -Arguments @('--recovery-repair', "$zip.par2") -Until 'big\.repaired\.zip' -TimeoutSeconds 120
    Assert-Smoke $run.Matched "--recovery-repair on the .par2 file did not name the copy: $($run.Text)"
    Assert-Smoke ((Get-Sha256 $copy) -eq $original) '--recovery-repair on the .par2 file: the copy is not equal to the original'
}
