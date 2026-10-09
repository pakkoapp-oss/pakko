#Requires -Version 5.1
<#
.SYNOPSIS
    Writes the CycloneDX SBOM of one shipped artifact: the MSIX or the CLI zip, one architecture (T-F365).
.DESCRIPTION
    Restores the artifact's projects in locked mode, so the graph is exactly the committed
    packages.lock.json files (T-F364), then runs the pinned CycloneDX tool (.config/dotnet-tools.json)
    on them without a second restore. The SBOM lists what the artifact ships or needs at run time:
    build-only packages, Windows ML (T-F363 keeps its DLLs out of the package) and the other
    architectures' ILCompiler packages are left out, and the result is checked before it is written.
    CI runs this in a job of its own with no secrets (build.yml's sbom job); see scripts/README.md.
.PARAMETER Artifact
    Msix (Archiver.App with the three satellite exes) or Cli (pakko.exe alone).
.PARAMETER Architecture
    x64 or arm64.
.PARAMETER Version
    The version written into the SBOM's metadata, e.g. 1.7.1 or 0.0.0-dev+abc1234.
.PARAMETER OutputPath
    The .cdx.json file to write.
.EXAMPLE
    .\New-Sbom.ps1 -Artifact Cli -Architecture x64 -Version 0.0.0-dev -OutputPath artifacts\sbom\pakko-win-x64.cdx.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Msix', 'Cli')]
    [string] $Artifact,
    [Parameter(Mandatory = $true)]
    [ValidateSet('x64', 'arm64')]
    [string] $Architecture,
    [Parameter(Mandatory = $true)]
    [string] $Version,
    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$rid = "win-$Architecture"
$cliProject = 'src\Archiver.CLI\Archiver.CLI.csproj'
$projects = if ($Artifact -eq 'Msix') {
    @('src\Archiver.App\Archiver.App.csproj', 'src\Archiver.Shell\Archiver.Shell.csproj', 'src\Archiver.OperationUi\Archiver.OperationUi.csproj', $cliProject)
} else {
    @($cliProject)
}

# Not in the artifact: build tools, Windows ML (referenced only to exclude its native DLLs, T-F363),
# and the ILCompiler (= the statically linked .NET runtime) of the other architectures.
$excluded = @(
    'Microsoft.Windows.SDK.BuildTools',
    'Microsoft.Windows.SDK.BuildTools.MSIX',
    'Microsoft.Windows.AI.MachineLearning',
    'System.Numerics.Tensors'
) + (@('win-x86', 'win-x64', 'win-arm64') | Where-Object { $_ -ne $rid } | ForEach-Object { "runtime.$_.Microsoft.DotNet.ILCompiler" })

$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDir = Split-Path $OutputPath -Parent
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$filter = $null
Push-Location $repoRoot
try {
    foreach ($project in $projects) {
        & dotnet restore $project --locked-mode
        if ($LASTEXITCODE -ne 0) { throw "Locked restore of $project failed (exit $LASTEXITCODE)." }
    }

    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed (exit $LASTEXITCODE)." }

    if ($Artifact -eq 'Msix') {
        $filter = Join-Path ([System.IO.Path]::GetTempPath()) "pakko-sbom-$([guid]::NewGuid().ToString('N')).slnf"
        $solution = @{ solution = @{ path = (Join-Path $repoRoot 'windows-archiver-wrapper.sln'); projects = $projects } }
        Set-Content -LiteralPath $filter -Value ($solution | ConvertTo-Json -Depth 4) -Encoding UTF8
        $source = $filter
        $name = 'Pakko'
    } else {
        $source = $cliProject
        $name = 'pakko'
    }

    # Not --exclude-filter: in 6.2.0 it keeps only what the root's own dependsOn reaches, and on a
    # solution filter that is one package; the exclusions are applied below instead.
    & dotnet tool run dotnet-CycloneDX $source --exclude-dev --disable-package-restore --output-format Json --spec-version 1.6 `
        --set-type Application --set-name $name --set-version $Version `
        --output $outputDir --filename (Split-Path $OutputPath -Leaf)
    if ($LASTEXITCODE -ne 0) { throw "CycloneDX failed (exit $LASTEXITCODE)." }
} finally {
    Pop-Location
    if ($filter) { Remove-Item -LiteralPath $filter -Force -ErrorAction SilentlyContinue }
}

$bom = Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json
$dropped = @($bom.components | Where-Object { $excluded -contains $_.name } | ForEach-Object { $_.'bom-ref' })
$bom.components = @($bom.components | Where-Object { $dropped -notcontains $_.'bom-ref' })
$bom.dependencies = @($bom.dependencies | Where-Object { $dropped -notcontains $_.ref })
# CycloneDX 1.6 leaves an empty dependsOn out; so does this.
foreach ($dependency in $bom.dependencies) {
    if (-not $dependency.PSObject.Properties['dependsOn']) { continue }
    $dependsOn = @($dependency.dependsOn | Where-Object { $dropped -notcontains $_ })
    if ($dependsOn.Count -eq 0) { $dependency.PSObject.Properties.Remove('dependsOn') } else { $dependency.dependsOn = $dependsOn }
}
# On a solution filter the root's dependsOn names one package only; the root depends on every
# component nothing else depends on.
$rootRef = $bom.metadata.component.'bom-ref'
$referenced = @($bom.dependencies | Where-Object { $_.ref -ne $rootRef -and $_.PSObject.Properties['dependsOn'] } | ForEach-Object { $_.dependsOn })
$root = @($bom.dependencies | Where-Object { $_.ref -eq $rootRef })
if ($root.Count -ne 1) { throw "Expected one dependency entry for the root '$rootRef', found $($root.Count)." }
$topLevel = @($bom.components | ForEach-Object { $_.'bom-ref' } | Where-Object { $referenced -notcontains $_ })
$root[0] | Add-Member -NotePropertyName dependsOn -NotePropertyValue $topLevel -Force
[System.IO.File]::WriteAllText($OutputPath, ($bom | ConvertTo-Json -Depth 100))

# The tool's --runtime does not drop other architectures' packages, and a new tool version may
# change what it emits: check the result rather than trust the arguments.
$names = @($bom.components | ForEach-Object { $_.name })
if ($bom.specVersion -ne '1.6') { throw "Expected CycloneDX 1.6, got $($bom.specVersion)." }
if ($names -notcontains "runtime.$rid.Microsoft.DotNet.ILCompiler") { throw "The $rid .NET runtime (ILCompiler) is missing from the SBOM." }
$foreign = @($names | Where-Object { $_ -like 'runtime.win-*' -and $_ -notlike "runtime.$rid.*" })
if ($foreign) { throw "Packages of another architecture in the $rid SBOM: $($foreign -join ', ')" }
$leaked = @($names | Where-Object { $excluded -contains $_ })
if ($leaked) { throw "Packages that are not shipped are in the SBOM: $($leaked -join ', ')" }
if ($Artifact -eq 'Msix' -and $names -notcontains 'Microsoft.WindowsAppSDK.Runtime') { throw 'Microsoft.WindowsAppSDK.Runtime is missing from the MSIX SBOM.' }

Write-Host "SBOM: $OutputPath ($($names.Count) components)"
