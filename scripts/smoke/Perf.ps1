#Requires -Version 7.0
# Steps of docs/SMOKE.md's PRF rows: the installed build against a yardstick measured in the same
# run. Dot-sourced by scripts/Invoke-Smoke.ps1.

# Fails when Pakko took more than -Factor times the yardstick. A run under one second passes
# whatever the ratio: at that size the two times are mostly process start.
function Assert-Budget {
    param([Parameter(Mandatory)][string]$What, [Parameter(Mandatory)][double]$Pakko, [Parameter(Mandatory)][double]$Yardstick, [Parameter(Mandatory)][double]$Factor)
    $line = '{0}: {1:n1} s against {2:n1} s' -f $What, $Pakko, $Yardstick
    Assert-Smoke ($Pakko -lt 1 -or $Pakko -lt $Yardstick * $Factor) "$line, over the budget of $Factor times"
    $line
}

function Assert-Exit {
    param([Parameter(Mandatory)]$Run, [Parameter(Mandatory)][string]$What)
    Assert-Smoke ($Run.ExitCode -eq 0) "${What}: exit $($Run.ExitCode) $($Run.Text)"
    $Run.Seconds
}

Add-SmokeStep 'PRF-001' {
    if (-not (Test-Path -LiteralPath $Smoke.Par2)) { Skip-Smoke 'par2cmdline is not downloaded: run scripts\Get-Par2Oracles.ps1' }
    $folder = Get-StepFolder 'prf001'
    $big = Join-Path $folder 'big.bin'
    Copy-Item -LiteralPath (Get-Fixture Big) -Destination $big

    $pakko = Assert-Exit (Invoke-Pakko @('a', '-rr5', '-mx=0', '-tzip', (Join-Path $folder 'big.zip'), $big)) 'pakko a -rr5'
    $yardstick = Assert-Exit (Invoke-Tool -Exe $Smoke.Par2 -Arguments @('create', '-q', '-q', '-r5', "$big.par2", $big)) 'par2 create'
    Assert-Budget -What 'a -rr5 on 200 MB' -Pakko $pakko -Yardstick $yardstick -Factor 2
}

Add-SmokeStep 'PRF-002' {
    if (-not (Test-Path -LiteralPath $Smoke.Par2)) { Skip-Smoke 'par2cmdline is not downloaded: run scripts\Get-Par2Oracles.ps1' }
    $ours = Get-StepFolder 'prf002ours'
    $zip = Copy-BigZipWithSet $ours
    Set-DamagedRange -Path $zip -Offset 50MB -Count 8MB
    $pakko = Assert-Exit (Invoke-Pakko @('r', $zip)) 'pakko r'

    # The same damage for par2cmdline, which repairs in place.
    $theirs = Get-StepFolder 'prf002theirs'
    $copy = Copy-BigZipWithSet $theirs
    Set-DamagedRange -Path $copy -Offset 50MB -Count 8MB
    $yardstick = Assert-Exit (Invoke-Tool -Exe $Smoke.Par2 -Arguments @('repair', '-q', '-q', "$copy.par2")) 'par2 repair'

    # Three times, not two: Pakko writes a whole second file and checks it, par2cmdline patches.
    Assert-Budget -What 'r of 8 MiB' -Pakko $pakko -Yardstick $yardstick -Factor 3
}

Add-SmokeStep 'PRF-003' {
    $folder = Get-StepFolder 'prf003'
    $many = Get-Fixture Many
    $ours = Join-Path $folder 'pakko.zip'
    $theirs = Join-Path $folder '7za.zip'

    $create = Assert-Exit (Invoke-Pakko @('a', $ours, $many)) 'pakko a'
    $createYardstick = Assert-Exit (Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('a', '-tzip', '-bso0', '-bsp0', $theirs, $many)) '7za a'
    $extract = Assert-Exit (Invoke-Pakko @('x', $ours, "-o$(Join-Path $folder 'px')")) 'pakko x'
    $extractYardstick = Assert-Exit (Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('x', '-bso0', '-bsp0', $ours, "-o$(Join-Path $folder 'sx')")) '7za x'

    Assert-Budget -What 'a on 6000 files' -Pakko $create -Yardstick $createYardstick -Factor 3
    Assert-Budget -What 'x on 6000 files' -Pakko $extract -Yardstick $extractYardstick -Factor 3
}

Add-SmokeStep 'PRF-004' {
    $output = & (Join-Path $Smoke.Repo 'scripts\Measure-Startup.ps1') -Runs 4 -Scenario App | Out-String
    # The first number of the row is the median, printed in the user's number format.
    $line = [regex]::Match($output, 'start -> window visible, ms\s+(\d+)[.,]')
    Assert-Smoke $line.Success "Measure-Startup printed no start time: $output"
    $median = [int]$line.Groups[1].Value
    Assert-Smoke ($median -lt 1000) "the App's window took $median ms to appear (median), expected under 1000"
    "median $median ms"
}

Add-SmokeStep 'PRF-005' {
    $zip = Get-Fixture ManyZip
    $list = Assert-Exit (Invoke-Pakko @('l', $zip)) 'pakko l'
    $listYardstick = Assert-Exit (Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('l', $zip)) '7za l'
    $test = Assert-Exit (Invoke-Pakko @('t', $zip)) 'pakko t'
    $testYardstick = Assert-Exit (Invoke-Tool -Exe $Smoke.SevenZip -Arguments @('t', '-bso0', '-bsp0', $zip)) '7za t'

    Assert-Budget -What 'l of 6000 entries' -Pakko $list -Yardstick $listYardstick -Factor 2
    Assert-Budget -What 't of 6000 entries' -Pakko $test -Yardstick $testYardstick -Factor 2
}
