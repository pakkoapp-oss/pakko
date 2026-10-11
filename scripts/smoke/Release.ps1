#Requires -Version 7.0
# Steps of docs/SMOKE.md's REL rows of the release tier: checks of the commit to be tagged, not of
# the installed package. Dot-sourced by scripts/Invoke-Smoke.ps1.

function Get-SmokeCommit {
    $git = (Get-Command -Name git -CommandType Application | Select-Object -First 1).Source
    $head = Invoke-Tool -Exe $git -Arguments @('rev-parse', 'HEAD') -WorkingDirectory $Smoke.Repo
    Assert-Smoke ($head.ExitCode -eq 0) "git rev-parse HEAD: $($head.Text)"
    $changed = Invoke-Tool -Exe $git -Arguments @('status', '--porcelain') -WorkingDirectory $Smoke.Repo
    [pscustomobject]@{ Sha = $head.Output.Trim(); Clean = -not $changed.Output.Trim() }
}

# The newest finished canary run on a commit: the jobs that say whether the nightly checks hold.
function Assert-CanaryRun {
    param([Parameter(Mandatory)][string]$Commit)
    $gh = (Get-Command -Name gh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source
    if (-not $gh) { Skip-Smoke 'the GitHub CLI (gh) is not installed' }
    $listed = Invoke-Tool -Exe $gh -Arguments @('run', 'list', '--workflow', 'canary.yml', '--commit', $Commit, '--status', 'completed', '--limit', '1', '--json', 'databaseId') -WorkingDirectory $Smoke.Repo
    Assert-Smoke ($listed.ExitCode -eq 0) "gh run list: $($listed.Text)"
    $runs = @($listed.Output | ConvertFrom-Json)
    if ($runs.Count -eq 0) { Skip-Smoke "no finished canary run on $($Commit.Substring(0, 7)): gh workflow run canary.yml --ref <branch of the commit>, then run this row again" }

    $viewed = Invoke-Tool -Exe $gh -Arguments @('run', 'view', [string]$runs[0].databaseId, '--json', 'jobs') -WorkingDirectory $Smoke.Repo
    Assert-Smoke ($viewed.ExitCode -eq 0) "gh run view: $($viewed.Text)"
    $jobs = @(($viewed.Output | ConvertFrom-Json).jobs)
    $verdict = { param($name) @($jobs | Where-Object name -EQ $name)[0].conclusion }

    # canary-failed-day runs only when a job of the day failed.
    Assert-Smoke ((& $verdict 'canary-failed-day') -eq 'skipped') "canary-failed-day: $(& $verdict 'canary-failed-day'), expected skipped"
    Assert-Smoke ((& $verdict 'canary-fuzz') -eq 'success') "canary-fuzz: $(& $verdict 'canary-fuzz')"
    $arm64 = @(@($jobs | Where-Object name -EQ 'canary-arm64')[0].steps | Where-Object name -Like 'Subprocess tests against the Native AOT pakko.exe (arm64*')
    Assert-Smoke ($arm64.Count -eq 1 -and $arm64[0].conclusion -eq 'success') "the ARM64 subprocess step: $($arm64.Count ? $arm64[0].conclusion : 'not found')"
    "run $($runs[0].databaseId) on $($Commit.Substring(0, 7))"
}

Add-SmokeStep 'REL-001' {
    $commit = Get-SmokeCommit
    $dotnet = (Get-Command -Name dotnet -CommandType Application | Select-Object -First 1).Source
    $run = Invoke-Tool -Exe $dotnet -Arguments @('test', '--nologo', '--filter', 'Category=Slow') -WorkingDirectory $Smoke.Repo -TimeoutSeconds 5400
    $failed = @($run.Output -split '\r?\n' | Where-Object { $_ -match '^\s*Failed\s|Failed!|error ' } | Select-Object -First 3)
    Assert-Smoke ($run.ExitCode -eq 0) "dotnet test --filter Category=Slow: exit $($run.ExitCode) $($failed -join ' / ')"
    $passed = 0
    foreach ($match in [regex]::Matches($run.Output, 'Passed:\s+(\d+)')) { $passed += [int]$match.Groups[1].Value }
    Assert-Smoke ($passed -gt 0) 'dotnet test --filter Category=Slow ran no test'
    "passed $passed on $($commit.Sha.Substring(0, 7))$($commit.Clean ? '' : ' with uncommitted changes')"
}

Add-SmokeStep 'REL-002' {
    $commit = Get-SmokeCommit
    Assert-CanaryRun -Commit $commit.Sha
}
