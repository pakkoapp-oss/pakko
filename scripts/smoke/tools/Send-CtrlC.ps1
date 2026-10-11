#Requires -Version 7.0
<#
.SYNOPSIS
    Sends Ctrl+C to the console of another process, for the smoke steps that cancel pakko (T-F378).
.DESCRIPTION
    Ctrl+C goes to a console, not to a process: this script leaves its own console, joins the
    target's and raises the event there. It runs as a process of its own so that the smoke run
    keeps its console. Started ahead of time (compiling the P/Invoke takes a second), it writes
    "ready" into -Folder, waits for a file "target" holding a process ID, and answers with "sent".
.PARAMETER Folder
    Where the three files are exchanged.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Folder)

$ErrorActionPreference = 'Stop'
Add-Type -Namespace Smoke -Name ConsoleSignal -MemberDefinition @'
[DllImport("kernel32.dll", SetLastError = true)] public static extern bool FreeConsole();
[DllImport("kernel32.dll", SetLastError = true)] public static extern bool AttachConsole(uint processId);
[DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
[DllImport("kernel32.dll", SetLastError = true)] public static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);
'@

Set-Content -LiteralPath (Join-Path $Folder 'ready') -Value $PID
$target = Join-Path $Folder 'target'
$deadline = (Get-Date).AddMinutes(5)
while (-not (Test-Path -LiteralPath $target)) {
    if ((Get-Date) -gt $deadline) { exit 2 }
    Start-Sleep -Milliseconds 20
}
$processId = [uint32](Get-Content -LiteralPath $target -Raw)

$null = [Smoke.ConsoleSignal]::FreeConsole()
$attached = [Smoke.ConsoleSignal]::AttachConsole($processId)
# This process is in that console now: it must not end on its own signal.
$null = [Smoke.ConsoleSignal]::SetConsoleCtrlHandler([IntPtr]::Zero, $true)
$sent = $attached -and [Smoke.ConsoleSignal]::GenerateConsoleCtrlEvent(0, 0)
Set-Content -LiteralPath (Join-Path $Folder 'sent') -Value "$sent"
Start-Sleep -Milliseconds 500
exit ($sent ? 0 : 1)
