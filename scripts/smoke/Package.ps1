#Requires -Version 7.0
# Steps of docs/SMOKE.md's PKG rows. Dot-sourced by scripts/Invoke-Smoke.ps1.

$PackagedExes = 'Archiver.App.exe', 'Archiver.Shell.exe', 'Archiver.OperationUi.exe', 'pakko.exe'

Add-SmokeStep 'PKG-001' {
    foreach ($exe in $PackagedExes) {
        $path = Join-Path $Smoke.Install $exe
        Assert-Smoke (Test-Path -LiteralPath $path) "$exe is not in the package"
        $managed = $true
        try { $null = [System.Reflection.AssemblyName]::GetAssemblyName($path) }
        catch [System.BadImageFormatException] { $managed = $false }
        Assert-Smoke (-not $managed) "$exe is a managed assembly, not a native one"
    }

    $files = @(Get-ChildItem -LiteralPath $Smoke.Install -Recurse -File)
    Assert-Smoke (-not ($files | Where-Object Name -eq 'coreclr.dll')) 'the package carries coreclr.dll'

    # The last record (v1.8.0, x64): update both numbers when a release changes them on purpose.
    $recordFiles = 27
    $recordMegabytes = 34.7
    $megabytes = ($files | Measure-Object Length -Sum).Sum / 1MB
    Assert-Smoke ([Math]::Abs($files.Count - $recordFiles) -le $recordFiles / 10) "the package has $($files.Count) files; the last record is $recordFiles"
    Assert-Smoke ([Math]::Abs($megabytes - $recordMegabytes) -le $recordMegabytes / 10) ('the package is {0:n1} MB; the last record is {1:n1} MB' -f $megabytes, $recordMegabytes)
    '{0} files, {1:n1} MB' -f $files.Count, $megabytes
}

Add-SmokeStep 'PKG-002' -AtEnd {
    $entries = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'Application Error', '.NET Runtime'; StartTime = $Smoke.Started } -ErrorAction SilentlyContinue |
            Where-Object { $_.Message -match 'Archiver\.|pakko\.exe' })
    Assert-Smoke ($entries.Count -eq 0) "the event log has $($entries.Count) entries from Pakko since the run began; first: $(($entries | Select-Object -First 1).Message -replace '\s+', ' ')"
}

Add-SmokeStep 'PKG-003' {
    $version = Invoke-Tool -Exe (Join-Path $Smoke.Install 'pakko.exe') -Arguments @('-v')
    Assert-Smoke ($version.Output.Contains("(package $($Smoke.Package.PackageFullName))")) "pakko -v does not name the installed package: $($version.Output)"
    foreach ($exe in $PackagedExes) {
        $info = (Get-Item -LiteralPath (Join-Path $Smoke.Install $exe)).VersionInfo
        Assert-Smoke $info.FileVersion "$exe has no file version"
    }
}

Add-SmokeStep 'PKG-004' -AtEnd {
    $temp = [System.IO.Path]::GetTempPath()
    $stages = @(Get-ChildItem -LiteralPath $temp -Directory -Filter 'PakkoTarStage_*' -ErrorAction SilentlyContinue | Where-Object CreationTime -ge $Smoke.Started)
    Assert-Smoke ($stages.Count -eq 0) "$($stages.Count) PakkoTarStage_* folder(s) of this run remain in %TEMP%"

    $sandbox = Join-Path $temp 'PakkoTarSandbox'
    $quarantined = @(if (Test-Path -LiteralPath $sandbox) { Get-ChildItem -LiteralPath $sandbox -ErrorAction SilentlyContinue | Where-Object CreationTime -ge $Smoke.Started })
    Assert-Smoke ($quarantined.Count -eq 0) "$($quarantined.Count) item(s) of this run remain in %TEMP%\PakkoTarSandbox"

    $partial = @(Get-ChildItem -LiteralPath $Smoke.Work -Recurse -File -Filter '*.tmp' -ErrorAction SilentlyContinue)
    Assert-Smoke ($partial.Count -eq 0) "temporary files remain next to the run's archives: $(($partial | Select-Object -First 3 -ExpandProperty Name) -join ', ')"
}
