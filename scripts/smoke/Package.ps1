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

# --- the package's life cycle (PKG-006..009) ---------------------------------------------------
# These rows remove and replace the installed package, so they run last and only with
# -PackageLifecycle. Whatever happens in them, the run ends with the package it began with
# (Restore-SmokePackage, which scripts/Invoke-Smoke.ps1 also calls at its end). The package's
# data is not kept: Deploy.ps1 removes the package on every run too.

$PakkoAlias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\pakko.exe'

function Get-InstalledPakko { Get-AppxPackage -Name 'PavloRybchenko.Pakko' | Select-Object -First 1 }

function Get-PakkoDataFolder { Join-Path $env:LOCALAPPDATA "Packages\$($Smoke.Package.PackageFamilyName)" }

# Checks that the run may replace the package and that it can put the first one back.
function Initialize-SmokeLifecycle {
    if (-not $Smoke.Lifecycle.Asked) { Skip-Smoke 'the row replaces the installed package: pass -PackageLifecycle' }
    if ($Smoke.Lifecycle.Original) { return }

    $version = $Smoke.Package.Version
    $file = Get-ChildItem -LiteralPath (Join-Path $Smoke.Repo 'src\Archiver.App\AppPackages') -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "*_${version}_*" -and $_.Extension -in '.msix', '.msixbundle' } | Select-Object -First 1
    if (-not $file) { Skip-Smoke "the file of the installed package $version is not under src\Archiver.App\AppPackages, so it could not be put back: run Deploy.ps1" }
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if ($signature.Status -ne 'Valid') { Skip-Smoke "$($file.Name) has no valid signature ($($signature.Status)), so it could not be put back" }
    $Smoke.Lifecycle.Original = $file.FullName
}

# The bundle of this machine's architecture in a folder of release files, with its version.
function Get-ReleaseBundle {
    param([Parameter(Mandatory)][ValidateSet('ReleaseFiles', 'PreviousRelease')][string]$From)
    $folder = $Smoke.Lifecycle[$From]
    if (-not $folder) { Skip-Smoke 'needs -ReleaseFiles (gh run download <run> -n pakko-msix-<arch> -D <folder>, or the release as below) and -PreviousRelease (gh release download <tag> -D <folder>)' }
    $architecture = "$($Smoke.Package.Architecture)".ToLowerInvariant()
    $bundle = Get-ChildItem -LiteralPath $folder -Recurse -Filter "*_$architecture.msixbundle" -ErrorAction SilentlyContinue | Select-Object -First 1
    Assert-Smoke $bundle "-$From has no *_$architecture.msixbundle: $folder"
    Assert-Smoke ($bundle.Name -match '_(\d+\.\d+\.\d+\.\d+)_') "the bundle's name carries no version: $($bundle.Name)"
    [pscustomobject]@{ Path = $bundle.FullName; Version = [version]$Matches[1] }
}

function Stop-PakkoProcess {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Closes what the life-cycle rows started.')]
    param()
    Get-Process -Name 'Archiver.App', 'Archiver.Shell', 'Archiver.OperationUi', 'pakko' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Wait-Smoke -What 'the end of the Pakko processes' -Condition { -not (Get-Process -Name 'Archiver.App', 'Archiver.Shell', 'Archiver.OperationUi', 'pakko' -ErrorAction SilentlyContinue) }
}

# Leaves -Bundle installed, whatever was there. -Clean: remove first, so no data is carried over.
function Install-ReleaseBundle {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Asked for with -PackageLifecycle; the first package is put back at the end.')]
    param([Parameter(Mandatory)]$Bundle, [switch]$Clean)
    Stop-PakkoProcess
    $installed = Get-InstalledPakko
    if (-not $Clean -and $installed -and $installed.Version -eq $Bundle.Version) { return }
    if (-not $Clean -and $installed) {
        # A lower version goes in only when forced; a bundle never replaces a flat .msix in place.
        try {
            Add-AppxPackage -Path $Bundle.Path -ForceUpdateFromAnyVersion -ErrorAction Stop
            return
        }
        catch { $installed = Get-InstalledPakko }
    }
    if ($installed) { Remove-AppxPackage -Package $installed.PackageFullName -ErrorAction Stop }
    Add-AppxPackage -Path $Bundle.Path -ErrorAction Stop
}

# Puts back the package the run began with. Does nothing when it is the one installed.
function Restore-SmokePackage {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Undoes what the life-cycle rows did.')]
    param()
    if (-not $Smoke.Lifecycle.Original) { return }
    $installed = Get-InstalledPakko
    if ($installed -and $installed.PackageFullName -eq $Smoke.Package.PackageFullName) { return }
    Stop-PakkoProcess
    if ($installed) { Remove-AppxPackage -Package $installed.PackageFullName -ErrorAction Stop }
    Add-AppxPackage -Path $Smoke.Lifecycle.Original -ErrorAction Stop
}

# Starts the App of the installed package and waits for its window.
function Start-PakkoApp {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Starts the App a smoke step asked for.')]
    param()
    Start-Process -FilePath "shell:AppsFolder\$($Smoke.Package.PackageFamilyName)!App"
    Wait-Smoke -What 'the window of the App' -TimeoutSeconds 60 -Condition {
        @(Get-Process -Name 'Archiver.App' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 }).Count -gt 0
    }
}

# What Add-AppxPackage said when it refused, or nothing when it installed.
function Get-InstallRefusal {
    param([Parameter(Mandatory)][string]$Path)
    try { Add-AppxPackage -Path $Path -ErrorAction Stop }
    catch { return "$($_.Exception.Message)" }
}

function Assert-NoPakkoCrash {
    param([Parameter(Mandatory)][datetime]$Since)
    $entries = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'Application Error', '.NET Runtime'; StartTime = $Since } -ErrorAction SilentlyContinue |
            Where-Object { $_.Message -match 'Archiver\.|pakko\.exe' })
    Assert-Smoke ($entries.Count -eq 0) "the event log has $($entries.Count) entries from Pakko; first: $(($entries | Select-Object -First 1).Message -replace '\s+', ' ')"
}

Add-SmokeStep 'PKG-006' -AtEnd {
    Initialize-SmokeLifecycle
    $previous = Get-ReleaseBundle PreviousRelease
    $new = Get-ReleaseBundle ReleaseFiles
    Assert-Smoke ($previous.Version -lt $new.Version) "-PreviousRelease ($($previous.Version)) is not older than -ReleaseFiles ($($new.Version))"

    Install-ReleaseBundle $previous -Clean
    $folder = Get-StepFolder 'pkg006'
    $archive = Join-Path $folder 'old.zip'
    $made = Invoke-Tool -Exe $PakkoAlias -Arguments @('a', $archive, (Get-Fixture Tree))
    Assert-Smoke ($made.ExitCode -eq 0) "pakko $($previous.Version) could not create an archive: $($made.Text)"

    $marker = Join-Path (Get-PakkoDataFolder) 'LocalState\smoke-marker.txt'
    $log = Join-Path (Get-PakkoDataFolder) 'LocalCache\Local\Pakko\logs\pakko.log'
    $text = "marker $([guid]::NewGuid())"
    Set-Content -LiteralPath $marker -Value $text -Encoding utf8
    Start-PakkoApp
    Wait-Smoke -What "the App's log" -Condition { Test-Path -LiteralPath $log }
    Stop-PakkoProcess
    $firstLine = Get-Content -LiteralPath $log -TotalCount 1

    $since = Get-Date
    Add-AppxPackage -Path $new.Path -ErrorAction Stop
    $installed = Get-InstalledPakko
    Assert-Smoke ($installed.Version -eq $new.Version) "after the update the installed version is $($installed.Version), not $($new.Version)"
    Assert-Smoke ($installed.PackageFamilyName -eq $Smoke.Package.PackageFamilyName) "the update changed the package family: $($installed.PackageFamilyName)"
    Assert-Smoke ((Test-Path -LiteralPath $marker) -and (Get-Content -LiteralPath $marker -Raw).Trim() -eq $text) 'the marker in LocalState did not survive the update'
    Assert-Smoke ((Test-Path -LiteralPath $log) -and (Get-Content -LiteralPath $log -TotalCount 1) -eq $firstLine) 'pakko.log did not survive the update'

    Start-PakkoApp
    Stop-PakkoProcess
    $tested = Invoke-Tool -Exe $PakkoAlias -Arguments @('t', $archive)
    Assert-Smoke ($tested.ExitCode -eq 0) "pakko $($new.Version) does not accept the archive of $($previous.Version): $($tested.Text)"
    Assert-NoPakkoCrash -Since $since
    "$($previous.Version) to $($new.Version)"
}

Add-SmokeStep 'PKG-007' -AtEnd {
    Initialize-SmokeLifecycle
    $previous = Get-ReleaseBundle PreviousRelease
    $new = Get-ReleaseBundle ReleaseFiles

    Install-ReleaseBundle $previous
    Assert-Smoke ((Get-InstalledPakko).Version -eq $previous.Version) "the previous release $($previous.Version) is not installed"
    $since = Get-Date
    Start-PakkoApp

    # The switch is what closes the App: without it Windows refuses, and nothing changes.
    $refusal = Get-InstallRefusal $new.Path
    Assert-Smoke ($refusal -match '0x80073D02') "an update over the running App, without the switch, was not refused as in use: $refusal"
    Assert-Smoke ((Get-InstalledPakko).Version -eq $previous.Version) 'the refused update changed the installed version'
    Assert-Smoke (Get-Process -Name 'Archiver.App' -ErrorAction SilentlyContinue) 'the refused update closed the App'

    Add-AppxPackage -Path $new.Path -ForceApplicationShutdown -ErrorAction Stop
    Assert-Smoke (-not (Get-Process -Name 'Archiver.App' -ErrorAction SilentlyContinue)) 'the App of the previous release is still running after the update'
    Assert-Smoke ((Get-InstalledPakko).Version -eq $new.Version) "after the update the installed version is $((Get-InstalledPakko).Version), not $($new.Version)"
    Start-PakkoApp
    Stop-PakkoProcess
    Assert-NoPakkoCrash -Since $since
}

Add-SmokeStep 'PKG-008' -AtEnd {
    Initialize-SmokeLifecycle
    $previous = Get-ReleaseBundle PreviousRelease
    $new = Get-ReleaseBundle ReleaseFiles

    Install-ReleaseBundle $new
    $installed = Get-InstalledPakko
    Assert-Smoke ($installed.Version -eq $new.Version) "the release $($new.Version) is not installed"

    $refusal = Get-InstallRefusal $previous.Path
    Assert-Smoke ($refusal -match '0x80073D06') "installing $($previous.Version) over $($new.Version) was not refused as a lower version: $refusal"
    Assert-Smoke ((Get-InstalledPakko).PackageFullName -eq $installed.PackageFullName) "the refused install changed the package: $((Get-InstalledPakko).PackageFullName)"

    $version = Invoke-Tool -Exe $PakkoAlias -Arguments @('-v')
    Assert-Smoke ($version.Output.Contains("(package $($installed.PackageFullName))")) "pakko -v does not name $($installed.PackageFullName): $($version.Text)"
    $archive = Join-Path (Get-StepFolder 'pkg008') 'new.zip'
    $made = Invoke-Tool -Exe $PakkoAlias -Arguments @('a', $archive, (Get-Fixture Tree))
    $tested = Invoke-Tool -Exe $PakkoAlias -Arguments @('t', $archive)
    Assert-Smoke ($made.ExitCode -eq 0 -and $tested.ExitCode -eq 0) "after the refused install pakko does not work: $($made.Text) $($tested.Text)"
}

Add-SmokeStep 'PKG-009' -AtEnd {
    Initialize-SmokeLifecycle
    $installed = Get-InstalledPakko
    Assert-Smoke $installed 'no Pakko package is installed'
    Assert-Smoke (Test-Path -LiteralPath $PakkoAlias) "the pakko alias is not there before the removal: $PakkoAlias"
    Start-PakkoApp

    Remove-AppxPackage -Package $installed.PackageFullName -ErrorAction Stop
    try {
        Assert-Smoke (-not (Get-InstalledPakko)) 'the package is still registered after its removal'
        Assert-Smoke (-not (Test-Path -LiteralPath $PakkoAlias)) "the pakko alias is still there: $PakkoAlias"
        $left = @(Get-Process -Name 'Archiver.App', 'Archiver.Shell', 'Archiver.OperationUi', 'pakko' -ErrorAction SilentlyContinue)
        Assert-Smoke ($left.Count -eq 0) "processes of the removed package still run: $(($left.Name | Sort-Object -Unique) -join ', ')"
        Assert-Smoke (-not (Test-Path -LiteralPath (Get-PakkoDataFolder))) "the package's data folder is still there: $(Get-PakkoDataFolder)"
        Wait-SmokeHold 'the package is removed'
    }
    finally {
        Stop-PakkoProcess
        Restore-SmokePackage
    }

    $back = Get-InstalledPakko
    Assert-Smoke ($back.PackageFullName -eq $Smoke.Package.PackageFullName) "the package put back is $($back.PackageFullName), not $($Smoke.Package.PackageFullName)"
    $version = Invoke-Tool -Exe $PakkoAlias -Arguments @('-v')
    Assert-Smoke ($version.Output.Contains("(package $($back.PackageFullName))")) "pakko -v does not name $($back.PackageFullName): $($version.Text)"
}
