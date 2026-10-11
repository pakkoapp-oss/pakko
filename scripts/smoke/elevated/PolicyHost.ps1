#Requires -Version 7.0
<#
.SYNOPSIS
    The elevated half of scripts/Invoke-Smoke.ps1: sets Pakko's machine policies and the Windows
    FIPS flag for one smoke step at a time, runs appcert, and puts the registry back (T-F378).
.DESCRIPTION
    Started once per run with one elevation prompt. It takes requests as files in -Folder
    (request-<n>.json, answered by reply-<n>.json) and does three things only: write the five
    values Pakko reads under HKLM\Software\Policies\Pakko, write FipsAlgorithmPolicy\Enabled, and
    run appcert.exe from the Windows SDK. Whatever the registry held at the start is written back
    when it is asked to stop, when a request fails, and when the process that started it is gone.

    Not for use on its own: the steps of scripts/smoke/Policy.ps1 drive it.
.PARAMETER Folder
    Where requests arrive and replies are written.
.PARAMETER ParentId
    The process that started this one. When it exits, the registry is restored and this exits.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Folder,
    [Parameter(Mandatory)][int]$ParentId
)

$ErrorActionPreference = 'Stop'
$policyKey = 'HKLM:\Software\Policies\Pakko'
$fipsKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa\FipsAlgorithmPolicy'
$policyNames = @('EnforceMOTW', 'AllowedFormats', 'BlockedFormats', 'DisableTarExtraction', 'DisableRecoveryData')
$log = Join-Path $Folder 'host.log'

function Write-HostLog {
    param([Parameter(Mandatory)][string]$Text)
    Add-Content -LiteralPath $log -Value ("{0:HH:mm:ss} {1}" -f (Get-Date), $Text) -Encoding utf8
}

# What the registry held before the first request: the five values with their kinds.
$keyExisted = Test-Path -LiteralPath $policyKey
$saved = @{}
if ($keyExisted) {
    $key = Get-Item -LiteralPath $policyKey
    foreach ($name in $policyNames) {
        if ($key.GetValueNames() -contains $name) {
            $saved[$name] = @{ Value = $key.GetValue($name); Kind = $key.GetValueKind($name) }
        }
    }
}
$fipsBefore = (Get-ItemProperty -LiteralPath $fipsKey).Enabled

function Restore-Registry {
    if (Test-Path -LiteralPath $policyKey) {
        foreach ($name in $policyNames) {
            Remove-ItemProperty -LiteralPath $policyKey -Name $name -ErrorAction SilentlyContinue
        }
    }
    if (-not $keyExisted) {
        if (Test-Path -LiteralPath $policyKey) { Remove-Item -LiteralPath $policyKey -Recurse -Force }
    }
    else {
        foreach ($name in $saved.Keys) {
            $null = New-ItemProperty -LiteralPath $policyKey -Name $name -Value $saved[$name].Value -PropertyType $saved[$name].Kind -Force
        }
    }
    Set-ItemProperty -LiteralPath $fipsKey -Name Enabled -Value $fipsBefore -Type DWord
}

# Replaces the state: everything not named in the request is as it was at the start.
function Set-Policy {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'The elevation prompt is the confirmation; the state is put back at the end.')]
    param([Parameter(Mandatory)]$Request)
    Restore-Registry
    $values = $Request.Values
    if ($values -and @($values.PSObject.Properties).Count -gt 0) {
        if (-not (Test-Path -LiteralPath $policyKey)) { $null = New-Item -Path $policyKey -Force }
        foreach ($property in $values.PSObject.Properties) {
            if ($policyNames -notcontains $property.Name) { throw "Not a Pakko policy value: $($property.Name)" }
            if ($property.Value -is [array]) {
                $null = New-ItemProperty -LiteralPath $policyKey -Name $property.Name -Value ([string[]]$property.Value) -PropertyType MultiString -Force
            }
            else {
                $null = New-ItemProperty -LiteralPath $policyKey -Name $property.Name -Value ([int]$property.Value) -PropertyType DWord -Force
            }
        }
    }
    if ($null -ne $Request.Fips) {
        Set-ItemProperty -LiteralPath $fipsKey -Name Enabled -Value ([int]$Request.Fips) -Type DWord
    }
}

function Invoke-Appcert {
    param([Parameter(Mandatory)]$Request)
    $appcert = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit\appcert.exe'
    if (-not (Test-Path -LiteralPath $appcert)) { throw "appcert.exe is not installed: $appcert" }
    if (Test-Path -LiteralPath $Request.Report) { Remove-Item -LiteralPath $Request.Report -Force }
    & $appcert reset | Out-Null
    $output = & $appcert test -apptype packagedwin32 -appxpackagepath $Request.Package -reportoutputpath $Request.Report 2>&1 | Out-String
    Write-HostLog "appcert exit $LASTEXITCODE"
    @{ ExitCode = $LASTEXITCODE; Output = $output }
}

Write-HostLog "start: key existed=$keyExisted, saved=$($saved.Keys -join ','), fips=$fipsBefore"
$next = 1
$stopped = $false
try {
    Set-Content -LiteralPath (Join-Path $Folder 'ready.json') -Value (@{ ProcessId = $PID } | ConvertTo-Json) -Encoding utf8
    while (-not $stopped -and (Get-Process -Id $ParentId -ErrorAction SilentlyContinue)) {
        $requestPath = Join-Path $Folder "request-$next.json"
        if (-not (Test-Path -LiteralPath $requestPath)) {
            Start-Sleep -Milliseconds 150
            continue
        }

        $reply = @{ Ok = $true }
        try {
            $request = Get-Content -LiteralPath $requestPath -Raw -Encoding utf8 | ConvertFrom-Json
            Write-HostLog "request $next : $($request.Kind)"
            switch ($request.Kind) {
                'policy' { Set-Policy $request }
                'appcert' { $reply.Result = Invoke-Appcert $request }
                'stop' { $stopped = $true }
                default { throw "Unknown request: $($request.Kind)" }
            }
        }
        catch {
            Write-HostLog "request $next failed: $_"
            $reply = @{ Ok = $false; Error = "$_" }
            Restore-Registry
        }

        if ($stopped) { Restore-Registry }
        # Written under another name first: the other side reads the file as soon as it exists.
        $replyPath = Join-Path $Folder "reply-$next.json"
        Set-Content -LiteralPath "$replyPath.tmp" -Value ($reply | ConvertTo-Json -Depth 5) -Encoding utf8
        Move-Item -LiteralPath "$replyPath.tmp" -Destination $replyPath -Force
        $next++
    }
}
finally {
    Restore-Registry
    Write-HostLog "restored: key=$(Test-Path -LiteralPath $policyKey), fips=$((Get-ItemProperty -LiteralPath $fipsKey).Enabled)"
}
