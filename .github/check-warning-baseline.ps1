<#
.SYNOPSIS
    Fails if the number of compiler-warning sites in the shipped projects has grown.

.DESCRIPTION
    Directory.Build.targets makes warnings errors with an allowlist, which stops a NEW KIND
    of warning from appearing. It cannot stop new instances of the codes already on that
    allowlist, and the repository carries a real backlog of those (74 nullable sites on the
    modern targets alone). Without a count gate that backlog only ever grows, one plausible
    line at a time.

    So this reads .github/warning-baseline.json, rebuilds each listed project, counts
    distinct warning sites per target framework, and fails when a count is higher than its
    baseline. A count that is LOWER is reported but does not fail, so paying debt down never
    blocks a pull request; lower the baseline in the same commit to bank the improvement.

    A "site" is a file(line,col) + code pair, deduplicated. Multi-targeted builds report the
    same source line once per inner build, which is why deduplication matters here.

.PARAMETER Configuration
    Build configuration to measure. Defaults to Release, matching what CI ships.

.PARAMETER BaselinePath
    Path to the baseline JSON. Defaults to the copy next to this script.

.EXAMPLE
    pwsh ./.github/check-warning-baseline.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$BaselinePath = (Join-Path $PSScriptRoot 'warning-baseline.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $BaselinePath)) {
    Write-Error "Baseline file not found: $BaselinePath"
    exit 1
}

$baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json
$repoRoot = Split-Path -Parent $PSScriptRoot

# The rebuilds below overwrite bin/ outputs that a PREVIOUS build's worker nodes may still
# hold: MSBuild nodes idle for 15 minutes after their build by default, and the AspectInjector
# weaving task runs inside them with the woven projects' references loaded. Measuring right
# after a build - which is both what CI does and what a developer checking their own change
# does - then dies ten retries into CopyFilesToOutputDirectory with MSB3027 "The file is
# locked by: .NET Host". Shutting the build servers down first releases those handles; it is
# best-effort on purpose, because "nothing was running" must not fail the gate.
& dotnet build-server shutdown *> $null

$failed = $false
$improved = @()

foreach ($project in $baseline.projects.PSObject.Properties) {
    $projectPath = Join-Path $repoRoot $project.Name
    if (-not (Test-Path $projectPath)) {
        Write-Error "Baseline lists a project that does not exist: $($project.Name)"
        $failed = $true
        continue
    }

    Write-Host "::group::Measuring $($project.Name)"

    # -t:Rebuild because an incremental build reports nothing for projects it skips, which
    # would silently measure zero and pass. -nodeReuse:false so THIS script's worker nodes
    # exit with it rather than idling and doing to the next rebuild what the shutdown above
    # just cleaned up.
    $output = & dotnet build $projectPath `
        --configuration $Configuration `
        -t:Rebuild `
        -nodeReuse:false `
        --nologo `
        -v n 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Host ($output | Out-String)
        Write-Host '::endgroup::'
        Write-Host "::error::Build failed for $($project.Name); cannot measure warnings."
        $failed = $true
        continue
    }

    $projectFileName = Split-Path -Leaf $project.Name

    foreach ($framework in $project.Value.PSObject.Properties) {
        $expected = [int]$framework.Value

        # Each warning line ends with "[<project path>::TargetFramework=<tfm>]". BOTH halves
        # matter. Building a project also builds what it references and reports their warnings
        # in the same stream, so filtering on the framework alone credited JsonSettings.csproj's
        # 69 nullable warnings to JsonSettings.Autosave.csproj, whose real count is 0.
        $marker = "$($projectFileName)::TargetFramework=$($framework.Name)]"
        $sites = $output |
            Where-Object { $_ -is [string] -and $_.Contains($marker) } |
            Select-String -Pattern '[^\\/]+\.cs\(\d+,\d+\): warning CS\d+' -AllMatches |
            ForEach-Object { $_.Matches.Value } |
            Sort-Object -Unique

        $actual = @($sites).Count

        if ($actual -gt $expected) {
            Write-Host "::error::$($project.Name) [$($framework.Name)]: $actual warning sites, baseline is $expected."
            # Print only what is new relative to the count, i.e. everything, so the author
            # can find the added one without re-running the build locally.
            $sites | ForEach-Object { Write-Host "    $_" }
            $failed = $true
        }
        elseif ($actual -lt $expected) {
            $improved += "$($project.Name) [$($framework.Name)]: $actual (baseline $expected)"
            Write-Host "  $($framework.Name): $actual (baseline $expected) - improved"
        }
        else {
            Write-Host "  $($framework.Name): $actual - at baseline"
        }
    }

    Write-Host '::endgroup::'
}

if ($improved.Count -gt 0) {
    Write-Host ''
    Write-Host '::notice::Warning counts have dropped below the baseline. Lower the numbers in .github/warning-baseline.json to lock the improvement in:'
    $improved | ForEach-Object { Write-Host "    $_" }
}

if ($failed) {
    Write-Host ''
    Write-Host 'The warning backlog is frozen, not accepted. Fix the new warning, or - if it is'
    Write-Host 'genuinely unavoidable - raise the baseline explicitly and say why in the commit.'
    exit 1
}

Write-Host ''
Write-Host 'Warning baseline holds.'
exit 0
