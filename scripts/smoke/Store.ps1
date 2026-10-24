#Requires -Version 7.0
# Steps of docs/SMOKE.md's store tier: checks of a release's own files (-Tag, -ReleaseFiles) and of
# the Store bundle built for the tag. Dot-sourced by scripts/Invoke-Smoke.ps1.
#
# What a file should say comes from the tag (its name, its commit, its CHANGELOG.md), never from
# the files under test.

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-ToolPath {
    param([Parameter(Mandatory)][string]$Name)
    $tool = (Get-Command -Name $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source
    if (-not $tool) { Skip-Smoke "$Name is not installed" }
    $tool
}

function Get-TagCommit {
    param([Parameter(Mandatory)][string]$Tag)
    $resolved = Invoke-Tool -Exe (Get-ToolPath git) -Arguments @('rev-parse', "$Tag^{commit}") -WorkingDirectory $Smoke.Repo
    if ($resolved.ExitCode -ne 0) { Skip-Smoke "the tag $Tag is not in this clone: git fetch --tags" }
    $resolved.Output.Trim()
}

# The release under test: its tag, the version and commit the tag stands for, and its files.
function Get-SmokeRelease {
    $tag = $Smoke.Lifecycle.Tag
    $folder = $Smoke.Lifecycle.ReleaseFiles
    if (-not $tag -or -not $folder) { Skip-Smoke 'needs -Tag <vX.Y.Z> and -ReleaseFiles <folder> (gh release download <tag> -D <folder>)' }
    Assert-Smoke ($tag -match '^v\d+\.\d+\.\d+$') "-Tag is not vX.Y.Z: $tag"
    [pscustomobject]@{ Tag = $tag; Version = $tag.Substring(1); Commit = Get-TagCommit $tag; Folder = $folder }
}

function Get-ReleaseFile {
    param([Parameter(Mandatory)]$Release, [Parameter(Mandatory)][string]$Name)
    $path = Join-Path $Release.Folder $Name
    Assert-Smoke (Test-Path -LiteralPath $path) "the release has no $Name in $($Release.Folder)"
    $path
}

function Read-ZipEntryText {
    param([Parameter(Mandatory)][string]$Zip, [Parameter(Mandatory)][string]$Entry)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        $found = $archive.GetEntry($Entry)
        Assert-Smoke $found "$(Split-Path -Leaf $Zip) has no $Entry"
        $reader = [System.IO.StreamReader]::new($found.Open())
        try { $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
}

# The version in the name of the x64 bundle in -PreviousRelease: the release before the one
# under test.
function Get-PreviousVersion {
    $folder = $Smoke.Lifecycle.PreviousRelease
    if (-not $folder) { Skip-Smoke 'needs -PreviousRelease <folder> (gh release download <the tag before> -D <folder>)' }
    $bundle = Get-ChildItem -LiteralPath $folder -Recurse -Filter '*_x64.msixbundle' | Select-Object -First 1
    Assert-Smoke ($bundle -and $bundle.Name -match '_(\d+\.\d+\.\d+)\.\d+_') "-PreviousRelease has no bundle with a version in its name: $folder"
    $Matches[1]
}

# The AppxManifest.xml of every application package inside a bundle, by architecture.
function Get-InnerManifest {
    param([Parameter(Mandatory)][string]$Bundle, [Parameter(Mandatory)][string]$Folder)
    $manifests = @{}
    [xml]$bundleManifest = Read-ZipEntryText -Zip $Bundle -Entry 'AppxMetadata/AppxBundleManifest.xml'
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Bundle)
    try {
        foreach ($package in @($bundleManifest.Bundle.Packages.Package | Where-Object Type -EQ 'application')) {
            $inner = Join-Path $Folder $package.FileName
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($archive.GetEntry($package.FileName), $inner, $true)
            $manifests[$package.Architecture] = Read-ZipEntryText -Zip $inner -Entry 'AppxManifest.xml'
        }
    }
    finally { $archive.Dispose() }
    $manifests
}

# The Store bundle of the newest successful dispatch of build.yml on a tag's commit. It is read,
# never started: without a run the row is skipped and says how to start one.
function Get-StoreBundle {
    param([Parameter(Mandatory)][string]$Tag)
    $key = "StoreBundle-$Tag"
    if ($Smoke.Fixtures.ContainsKey($key)) { return $Smoke.Fixtures[$key] }

    $commit = Get-TagCommit $Tag
    $gh = Get-ToolPath gh
    $listed = Invoke-Tool -Exe $gh -Arguments @('run', 'list', '--workflow', 'build.yml', '--event', 'workflow_dispatch', '--commit', $commit, '--status', 'success', '--limit', '1', '--json', 'databaseId') -WorkingDirectory $Smoke.Repo
    Assert-Smoke ($listed.ExitCode -eq 0) "gh run list: $($listed.Text)"
    $runs = @($listed.Output | ConvertFrom-Json)
    if ($runs.Count -eq 0) { Skip-Smoke "no successful Store build on $Tag ($($commit.Substring(0, 7))): gh workflow run build.yml --ref $Tag, then run this row again" }

    $folder = Get-StepFolder "store-$Tag"
    $downloaded = Invoke-Tool -Exe $gh -Arguments @('run', 'download', [string]$runs[0].databaseId, '--name', 'pakko-store-msixbundle', '--dir', $folder) -WorkingDirectory $Smoke.Repo
    Assert-Smoke ($downloaded.ExitCode -eq 0) "gh run download $($runs[0].databaseId): $($downloaded.Text)"
    $bundle = Get-ChildItem -LiteralPath $folder -Recurse -Filter '*.msixbundle' | Select-Object -First 1
    Assert-Smoke $bundle "run $($runs[0].databaseId) has no .msixbundle in pakko-store-msixbundle"
    $Smoke.Fixtures[$key] = [pscustomobject]@{ Path = $bundle.FullName; Run = $runs[0].databaseId; Folder = $folder }
    $Smoke.Fixtures[$key]
}

Add-SmokeStep 'CLI-017' {
    $release = Get-SmokeRelease
    $architecture = "$($Smoke.Package.Architecture)".ToLowerInvariant()
    $folder = Get-StepFolder 'cli017'
    [System.IO.Compression.ZipFile]::ExtractToDirectory((Get-ReleaseFile $release "pakko-win-$architecture.zip"), $folder)
    $exe = Join-Path $folder 'pakko.exe'
    Assert-Smoke (Test-Path -LiteralPath $exe) "pakko-win-$architecture.zip has no pakko.exe at its root"

    $product = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
    Assert-Smoke ($product -eq "$($release.Version)+$($release.Commit)") "pakko.exe's product version is $product, not $($release.Version)+$($release.Commit)"
    foreach ($flag in '-v', '--version') {
        $version = Invoke-Tool -Exe $exe -Arguments @($flag)
        Assert-Smoke ($version.ExitCode -eq 0 -and $version.Output.Trim() -eq "pakko $($release.Version)") "$flag printed '$($version.Output.Trim())', not 'pakko $($release.Version)'"
    }
    foreach ($flag in '-h', '--help') {
        $help = Invoke-Tool -Exe $exe -Arguments @($flag)
        Assert-Smoke ($help.ExitCode -eq 0) "$flag exit $($help.ExitCode)"
        foreach ($command in 'a', 'x', 't', 'l', 'i', 'h', 'r') {
            Assert-Smoke ($help.Output -match "(?m)^\s+$command\s{2,}\S") "$flag does not list the command '$command'"
        }
    }

    # The two tests left out expect the version text of a build that is not a release.
    $before = $env:PAKKO_CLI_EXE
    $env:PAKKO_CLI_EXE = $exe
    try {
        $run = Invoke-Tool -Exe (Get-ToolPath dotnet) -Arguments @('test', (Join-Path $Smoke.Repo 'tests\Archiver.CLI.Tests'), '--nologo', '--filter',
            'FullyQualifiedName~Subprocess&FullyQualifiedName!~DashV_ExitsZeroAndPrintsPakkoPrefixedVersion&FullyQualifiedName!~DashDashVersion_ExitsZeroAndPrintsPakkoPrefixedVersion')
    }
    finally { $env:PAKKO_CLI_EXE = $before }
    $summary = [regex]::Match($run.Text, 'Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+)')
    Assert-Smoke $summary.Success "dotnet test printed no summary: $($run.Text[-400..-1] -join '')"
    Assert-Smoke ($run.ExitCode -eq 0 -and $summary.Groups[1].Value -eq '0') "subprocess tests failed on the release's pakko.exe: $($summary.Value)"
    "$product; passed $($summary.Groups[2].Value)"
}

Add-SmokeStep 'REL-003' {
    $release = Get-SmokeRelease
    $listed = @{}
    foreach ($line in Get-Content -LiteralPath (Get-ReleaseFile $release 'SHA256SUMS')) {
        if (-not $line.Trim()) { continue }
        $hash, $name = $line.Trim() -split '\s+', 2
        $listed[$name.TrimStart('*')] = $hash
    }
    foreach ($name in 'pakko-win-x64.zip', 'pakko-win-arm64.zip') {
        Assert-Smoke ($listed.ContainsKey($name)) "SHA256SUMS does not list $name"
    }
    foreach ($name in $listed.Keys) {
        $actual = Get-Sha256 (Get-ReleaseFile $release $name)
        Assert-Smoke ($actual -ieq $listed[$name]) "$name does not match SHA256SUMS: $actual"
    }

    $gh = Get-ToolPath gh
    $repository = (Invoke-Tool -Exe $gh -Arguments @('repo', 'view', '--json', 'nameWithOwner', '--jq', '.nameWithOwner') -WorkingDirectory $Smoke.Repo).Output.Trim()
    Assert-Smoke $repository 'gh repo view named no repository'
    $attested = "Archiver.App_$($release.Version).0_x64.msixbundle", "Archiver.App_$($release.Version).0_arm64.msixbundle", 'pakko-win-x64.zip', 'pakko-win-arm64.zip'
    foreach ($name in $attested) {
        $verified = Invoke-Tool -Exe $gh -Arguments @('attestation', 'verify', (Get-ReleaseFile $release $name), '--repo', $repository)
        Assert-Smoke ($verified.ExitCode -eq 0) "no valid attestation for $name in ${repository}: $($verified.Text.Trim())"
    }
    "$($listed.Count) hashes, $($attested.Count) attestations"
}

Add-SmokeStep 'REL-004' {
    $release = Get-SmokeRelease
    foreach ($architecture in 'x64', 'arm64') {
        $bundle = Get-ReleaseFile $release "Archiver.App_$($release.Version).0_$architecture.msixbundle"
        $signature = Get-AuthenticodeSignature -LiteralPath $bundle
        Assert-Smoke ($signature.Status -eq 'Valid') "$architecture bundle: the signature is $($signature.Status)"
        Assert-Smoke ($signature.SignerCertificate.Subject -eq 'CN=Pakko Dev') "$architecture bundle: signed by $($signature.SignerCertificate.Subject)"

        [xml]$manifest = Read-ZipEntryText -Zip $bundle -Entry 'AppxMetadata/AppxBundleManifest.xml'
        $identity = $manifest.Bundle.Identity
        Assert-Smoke ($identity.Version -eq "$($release.Version).0") "$architecture bundle: version $($identity.Version), not $($release.Version).0"
        Assert-Smoke ($identity.Publisher -eq 'CN=Pakko Dev') "$architecture bundle: publisher $($identity.Publisher)"
        $applications = @($manifest.Bundle.Packages.Package | Where-Object Type -EQ 'application')
        Assert-Smoke ($applications.Count -eq 1 -and $applications[0].Architecture -eq $architecture) "$architecture bundle: application packages for $($applications.Architecture -join ', ')"
    }
}

Add-SmokeStep 'REL-005' {
    $release = Get-SmokeRelease
    $changelog = Invoke-Tool -Exe (Get-ToolPath git) -Arguments @('show', "$($release.Tag):CHANGELOG.md") -WorkingDirectory $Smoke.Repo
    Assert-Smoke ($changelog.ExitCode -eq 0) "git show $($release.Tag):CHANGELOG.md: $($changelog.Text)"
    # As build.yml's release job cuts it: sections are separated by a bare "---" line.
    $section = [regex]::Split($changelog.Output, '(?m)^---\s*$') | Where-Object { $_ -match "(?m)^##\s+$([regex]::Escape($release.Tag))\s" } | Select-Object -First 1
    Assert-Smoke $section "CHANGELOG.md at $($release.Tag) has no section for it"

    $viewed = Invoke-Tool -Exe (Get-ToolPath gh) -Arguments @('release', 'view', $release.Tag, '--json', 'body', '--jq', '.body') -WorkingDirectory $Smoke.Repo
    Assert-Smoke ($viewed.ExitCode -eq 0) "gh release view $($release.Tag): $($viewed.Text)"
    $normal = { param($text) ($text -replace '\r\n', "`n").Trim() }
    $expected = & $normal $section
    $notes = & $normal $viewed.Output
    Assert-Smoke ($notes.StartsWith($expected, [StringComparison]::Ordinal)) "the notes of $($release.Tag) do not begin with its CHANGELOG.md section; they begin: $($notes.Substring(0, [Math]::Min(120, $notes.Length)))"
    "$($expected.Length) characters of CHANGELOG.md"
}

Add-SmokeStep 'REL-006' {
    $release = Get-SmokeRelease
    foreach ($name in 'pakko-msix-x64.cdx.json', 'pakko-msix-arm64.cdx.json', 'pakko-win-x64.cdx.json', 'pakko-win-arm64.cdx.json') {
        $bom = Get-Content -LiteralPath (Get-ReleaseFile $release $name) -Raw -Encoding utf8 | ConvertFrom-Json
        Assert-Smoke ($bom.bomFormat -eq 'CycloneDX' -and $bom.specVersion) "$name is not a CycloneDX document"
        Assert-Smoke ($bom.metadata.component.version -eq $release.Version) "$name describes version $($bom.metadata.component.version), not $($release.Version)"
        Assert-Smoke (@($bom.components).Count -gt 0) "$name lists no component"
    }
}

Add-SmokeStep 'REL-007' {
    $release = Get-SmokeRelease
    $winget = Get-ToolPath winget
    $id = 'PavloRybchenko.PakkoCLI'
    $settings = (Invoke-Tool -Exe $winget -Arguments @('settings', 'export')).Output | ConvertFrom-Json
    if (-not $settings.adminSettings.LocalManifestFiles) { Skip-Smoke 'winget does not install from a local manifest here: winget settings --enable LocalManifestFiles (elevated)' }
    $present = Invoke-Tool -Exe $winget -Arguments @('list', '--id', $id, '--exact', '--disable-interactivity', '--accept-source-agreements')
    $packages = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    $link = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links\pakko.exe'
    if (Test-Path -LiteralPath $link) { Skip-Smoke "$link exists already, and the row would replace it" }
    # A copy installed from a manifest is not found by its ID, only by its folder.
    $folders = @(Get-ChildItem -LiteralPath $packages -Directory -Filter "$id*" -ErrorAction SilentlyContinue)
    if ($present.ExitCode -eq 0 -or $folders) { Skip-Smoke "$id is installed on this machine, and the row would remove it" }

    $folder = Get-StepFolder 'rel007'
    & (Join-Path $Smoke.Repo 'scripts\New-WingetManifest.ps1') -Version $release.Version -Sha256SumsPath (Get-ReleaseFile $release 'SHA256SUMS') -OutputRoot $folder 6>$null | Out-Null
    $validated = Invoke-Tool -Exe $winget -Arguments @('validate', '--manifest', $folder, '--disable-interactivity')
    Assert-Smoke ($validated.ExitCode -eq 0) "winget validate: $($validated.Text.Trim())"

    # The user's PATH as the registry holds it, unexpanded.
    $userPath = { (Get-Item -LiteralPath 'HKCU:\Environment').GetValue('Path', '', 'DoNotExpandEnvironmentNames') }
    $pathBefore = & $userPath
    $installed = Invoke-Tool -Exe $winget -Arguments @('install', '--manifest', $folder, '--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity')
    try {
        Assert-Smoke ($installed.ExitCode -eq 0) "winget install --manifest: exit $($installed.ExitCode) $($installed.Text.Trim())"
        $exe = Get-ChildItem -LiteralPath $packages -Recurse -Filter 'pakko.exe' -ErrorAction SilentlyContinue | Where-Object FullName -Like "*$id*" | Select-Object -First 1
        Assert-Smoke $exe "winget installed no pakko.exe under $packages"
        # T-F361: the command is winget's symlink in WinGet\Links, not the install folder on PATH.
        Assert-Smoke ((Test-Path -LiteralPath $link) -and (Get-Item -LiteralPath $link).LinkType -eq 'SymbolicLink') "winget made no symlink $link"
        $version = Invoke-Tool -Exe $link -Arguments @('-v')
        Assert-Smoke ($version.Output.Trim() -eq "pakko $($release.Version)") "pakko through $link printed '$($version.Output.Trim())', not 'pakko $($release.Version)'"
        Assert-Smoke (-not ((& $userPath) -split ';' -contains $exe.DirectoryName)) "the user's PATH holds the install folder $($exe.DirectoryName)"
    }
    finally {
        $removed = Invoke-Tool -Exe $winget -Arguments @('uninstall', '--manifest', $folder, '--disable-interactivity', '--accept-source-agreements')
        # The first portable install on a machine adds WinGet\Links to the user's PATH and winget
        # keeps it; the step writes back the value it found either way.
        $pathAfter = & $userPath
        if ($pathAfter -cne $pathBefore) {
            Set-ItemProperty -LiteralPath 'HKCU:\Environment' -Name Path -Value $pathBefore -Type (Get-Item -LiteralPath 'HKCU:\Environment').GetValueKind('Path')
        }
    }
    Assert-Smoke ($removed.ExitCode -eq 0) "winget uninstall: exit $($removed.ExitCode) $($removed.Text.Trim())"
    $left = @(Get-ChildItem -LiteralPath $packages -Recurse -Filter 'pakko.exe' -ErrorAction SilentlyContinue | Where-Object FullName -Like "*$id*")
    Assert-Smoke ($left.Count -eq 0) "the uninstall left $($left[0].FullName)"
    Assert-Smoke (-not (Test-Path -LiteralPath $link)) "the uninstall left the symlink $link"

    $changed = @(Compare-Object ($pathBefore -split ';') ($pathAfter -split ';') | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" })
    $links = "=> $(Split-Path -Parent $link)"
    Assert-Smoke (-not ($changed | Where-Object { $_ -ne $links })) "the install and uninstall changed the user's PATH in more than winget's Links folder: $($changed -join ' // ')"
    $changed ? 'winget added its Links folder to PATH (first portable install); the step put the PATH back' : 'the PATH is as before'
}

Add-SmokeStep 'STO-001' {
    $release = Get-SmokeRelease
    $store = Get-StoreBundle $release.Tag
    # The publisher Partner Center reserved, as the script that makes the Store certificate has it.
    $setup = Get-Content -LiteralPath (Join-Path $Smoke.Repo 'scripts\Setup-StoreCert.ps1') -Raw
    Assert-Smoke ($setup -match "\`$subject\s*=\s*'(CN=[^']+)'") 'Setup-StoreCert.ps1 no longer names the Store publisher'
    $publisher = $Matches[1]

    [xml]$manifest = Read-ZipEntryText -Zip $store.Path -Entry 'AppxMetadata/AppxBundleManifest.xml'
    $identity = $manifest.Bundle.Identity
    Assert-Smoke ($identity.Name -eq $Smoke.Package.Name) "the Store bundle is $($identity.Name), not $($Smoke.Package.Name)"
    Assert-Smoke ($identity.Version -eq "$($release.Version).0") "the Store bundle has version $($identity.Version), not $($release.Version).0"
    Assert-Smoke ($identity.Publisher -eq $publisher) "the Store bundle's publisher is $($identity.Publisher), not $publisher"
    $signature = Get-AuthenticodeSignature -LiteralPath $store.Path
    Assert-Smoke ($signature.SignerCertificate.Subject -eq $publisher) "the Store bundle is signed by $($signature.SignerCertificate.Subject)"
    Assert-Smoke ($signature.Status -in 'Valid', 'UnknownError') "the Store bundle's signature is $($signature.Status)"

    $architectures = @($manifest.Bundle.Packages.Package | Where-Object Type -EQ 'application' | ForEach-Object Architecture | Sort-Object)
    Assert-Smoke (($architectures -join ',') -eq 'arm64,x64') "the Store bundle holds application packages for: $($architectures -join ', ')"
    $locales = @(Get-ChildItem -LiteralPath (Join-Path $Smoke.Repo 'src\Archiver.App\Strings') -Directory).Count
    $inner = Get-InnerManifest -Bundle $store.Path -Folder $store.Folder
    foreach ($architecture in $architectures) {
        [xml]$package = $inner[$architecture]
        $languages = @($package.Package.Resources.Resource | Where-Object Language).Count
        Assert-Smoke ($languages -eq $locales) "the $architecture package declares $languages languages; src\Archiver.App\Strings has $locales"
        Assert-Smoke ($package.Package.Identity.Version -eq "$($release.Version).0") "the $architecture package has version $($package.Package.Identity.Version)"
    }
    "run $($store.Run): $($identity.Version), $locales languages, signature $($signature.Status)"
}

Add-SmokeStep 'STO-002' {
    $release = Get-SmokeRelease
    $previousVersion = Get-PreviousVersion

    $store = Get-StoreBundle $release.Tag
    $before = Get-StoreBundle "v$previousVersion"
    $new = Get-InnerManifest -Bundle $store.Path -Folder $store.Folder
    $old = Get-InnerManifest -Bundle $before.Path -Folder $before.Folder
    Assert-Smoke ((($new.Keys | Sort-Object) -join ',') -eq (($old.Keys | Sort-Object) -join ',')) "the architectures changed: $($old.Keys -join ', ') to $($new.Keys -join ', ')"

    # The version is the one difference every release has; anything else must be meant.
    $differences = foreach ($architecture in $new.Keys | Sort-Object) {
        $expected = $old[$architecture].Replace("$previousVersion.0", "$($release.Version).0") -split '\r?\n'
        Compare-Object $expected ($new[$architecture] -split '\r?\n') | ForEach-Object { "$architecture $($_.SideIndicator) $($_.InputObject.Trim())" }
    }
    Assert-Smoke (-not $differences) "the manifests differ from $previousVersion in more than the version (record why in the release record): $(@($differences) -join ' // ')"
    "against $previousVersion (run $($before.Run)): the version only"
}

Add-SmokeStep 'STO-003' {
    $release = Get-SmokeRelease
    $previousTag = "v$(Get-PreviousVersion)"
    $null = Get-TagCommit $previousTag
    $git = Get-ToolPath git
    # A release that says nothing new to the Store's readers has a stale listing.
    $whatsNew = {
        param($tag)
        $shown = Invoke-Tool -Exe $git -Arguments @('show', "${tag}:docs/store-listing/en-US.txt") -WorkingDirectory $Smoke.Repo
        Assert-Smoke ($shown.ExitCode -eq 0) "git show ${tag}:docs/store-listing/en-US.txt: $($shown.Text)"
        $found = [regex]::Match($shown.Output, "(?s)\[What's new in this version\]\s*(.+?)(\r?\n\[|\z)")
        Assert-Smoke $found.Success "docs/store-listing/en-US.txt at $tag has no [What's new in this version] section"
        $found.Groups[1].Value.Trim()
    }
    $now = & $whatsNew $release.Tag
    Assert-Smoke ($now -ne (& $whatsNew $previousTag)) "the listing's What's new at $($release.Tag) is the text of $previousTag"

    $run = Invoke-Tool -Exe (Get-ToolPath dotnet) -Arguments @('test', (Join-Path $Smoke.Repo 'tests\Archiver.Messages.Tests'), '--nologo', '--filter', 'FullyQualifiedName~StoreListingTests')
    $summary = [regex]::Match($run.Text, 'Failed:\s+(\d+), Passed:\s+(\d+)')
    Assert-Smoke ($run.ExitCode -eq 0 -and $summary.Success -and $summary.Groups[1].Value -eq '0' -and [int]$summary.Groups[2].Value -gt 0) "StoreListingTests: $($summary.Value) $($run.Text[-300..-1] -join '')"
    "What's new differs from $previousTag; StoreListingTests passed $($summary.Groups[2].Value)"
}
