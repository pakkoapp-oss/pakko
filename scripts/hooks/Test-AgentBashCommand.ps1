#Requires -Version 7.0
<#
.SYNOPSIS
    Claude Code PreToolUse hook (T-F370): blocks Bash-tool commands this repo's rules forbid.
.DESCRIPTION
    Reads the hook payload (JSON) from stdin and checks every command at command position - the
    start of the command, a line, or the part after an unquoted ;, &, |, ( or $( - so a word inside
    quotes or a heredoc body never counts. Blocks, with exit code 2 and the reason on stderr:
      - python / python3: bare python fails through the Bash tool; use "py -3 script.py".
      - dotnet with a /p: switch: Git Bash rewrites "/p:" into a path (MSB1008); use the
        PowerShell tool.
    Anything else exits 0. A payload that is not JSON exits 1, which Claude Code reports without
    blocking the command.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest

function Get-CommandSegment {
    param([string] $Text)

    $segments = [System.Collections.Generic.List[string]]::new()
    $current = [System.Text.StringBuilder]::new()
    $quote = [char]0
    $heredocEnd = $null
    # A trailing backslash continues the command on the next line (CLAUDE.md's dotnet publish block).
    $lines = ($Text -replace '\\\r?\n', ' ') -split "`r?`n"
    foreach ($line in $lines) {
        if ($null -ne $heredocEnd) {
            if ($line.Trim() -eq $heredocEnd) { $heredocEnd = $null }
            continue
        }
        if ($quote -eq [char]0) {
            $heredoc = [regex]::Match($line, "<<-?\s*['`"]?([A-Za-z_][A-Za-z0-9_]*)")
            if ($heredoc.Success) { $heredocEnd = $heredoc.Groups[1].Value }
        }
        for ($i = 0; $i -lt $line.Length; $i++) {
            $c = $line[$i]
            if ($quote -ne [char]0) {
                if ($c -eq $quote) { $quote = [char]0 }
                elseif ($c -eq '\' -and $quote -eq '"' -and $i + 1 -lt $line.Length) { [void]$current.Append($c); $i++; $c = $line[$i] }
                [void]$current.Append($c)
                continue
            }
            if ($c -eq "'" -or $c -eq '"') { $quote = $c; [void]$current.Append($c); continue }
            if ($c -eq ';' -or $c -eq '&' -or $c -eq '|' -or $c -eq '(') {
                $segments.Add($current.ToString()); [void]$current.Clear(); continue
            }
            [void]$current.Append($c)
        }
        if ($quote -eq [char]0) { $segments.Add($current.ToString()); [void]$current.Clear() }
        else { [void]$current.Append("`n") }
    }
    $segments.Add($current.ToString())
    return $segments
}

function Get-BlockReason {
    param([string] $Segment)

    $words = @($Segment.Trim() -split '\s+' | Where-Object { $_ -ne '' })
    $index = 0
    while ($index -lt $words.Count -and $words[$index] -match '^[A-Za-z_][A-Za-z0-9_]*=') { $index++ }
    if ($index -ge $words.Count) { return $null }

    $program = [System.IO.Path]::GetFileName($words[$index].Trim('"', "'")).ToLowerInvariant()
    if ($program -in @('python', 'python3', 'python.exe', 'python3.exe')) {
        return 'Bare python fails through the Bash tool here. Run "py -3 script.py" (a script file, not -c with a Windows path).'
    }
    if ($program -in @('dotnet', 'dotnet.exe') -and $Segment -match '(^|\s)["'']?/p:') {
        return 'Git Bash rewrites "/p:" into a path (MSB1008). Run dotnet with /p: switches through the PowerShell tool.'
    }
    return $null
}

try {
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json -ErrorAction Stop
} catch {
    [Console]::Error.WriteLine("Test-AgentBashCommand: the hook payload is not JSON: $($_.Exception.Message)")
    exit 1
}

$command = ''
if ($payload.PSObject.Properties['tool_input'] -and $payload.tool_input.PSObject.Properties['command']) {
    $command = [string]$payload.tool_input.command
}

foreach ($segment in Get-CommandSegment -Text $command) {
    $reason = Get-BlockReason -Segment $segment
    if ($reason) {
        [Console]::Error.WriteLine("Blocked by scripts/hooks/Test-AgentBashCommand.ps1 (T-F370): $reason")
        exit 2
    }
}
exit 0
