<#
.SYNOPSIS
    Prints the commit log between the previous release tag and the given one, as markdown.

.DESCRIPTION
    Requires the FULL history and tags to be present. actions/checkout defaults to
    fetch-depth 1 with no tags, and under that default `git describe` finds nothing, the
    script falls back to "every commit reachable from the tag", and a shallow clone makes
    that exactly one commit - so the release notes silently come out as a single line
    describing the tagged commit and nothing else. The caller must pass fetch-depth: 0.

.PARAMETER CurrentTag
    The tag being released. Accepts either a bare tag or a ref (refs/tags/v2.1.0).

.PARAMETER PreviousTag
    Optional. The tag to compare against. Defaults to the nearest tag before CurrentTag.

.EXAMPLE
    ./.github/get-release-notes.ps1 -CurrentTag refs/tags/v2.1.0
    ./.github/get-release-notes.ps1 -CurrentTag v2.1.0 -PreviousTag v2.0.1
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string]$CurrentTag,

    [Parameter(Mandatory = $false)]
    [string]$PreviousTag
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Accept a ref or a bare tag. GITHUB_REF arrives as refs/tags/v2.1.0.
$CurrentTag = $CurrentTag -replace '^refs/tags/', ''

if (-not $PreviousTag) {
    # Run git on its OWN line, not piped into Select-Object. Piping a native command straight
    # into `Select-Object -First 1` lets Select-Object stop the pipeline the moment it has its
    # single item, and PowerShell tears the pipeline down before it records the process exit
    # code -- so $LASTEXITCODE is never set. Under Set-StrictMode -Version Latest, reading an
    # unset $LASTEXITCODE on the next line then throws "cannot be retrieved because it has not
    # been set" (git describe is the first native command in this fresh pwsh session, so nothing
    # set it earlier). Capturing to a variable first makes git run to completion and sets it.
    # 2>$null so "no names found" is not treated as script failure under $ErrorActionPreference=Stop.
    $describe = git describe --tags --abbrev=0 "$CurrentTag^" 2>$null

    # $LASTEXITCODE is only meaningful HERE, immediately after the git call. The previous
    # version tested it even when -PreviousTag had been supplied and git had never run, so the
    # branch was decided by whatever command happened to have run last in the session.
    if ($LASTEXITCODE -ne 0) {
        $PreviousTag = $null
    } else {
        # git describe returns a single line; Select-Object -First 1 is a defensive guard, now
        # in its own pipeline where stopping it early cannot swallow git's exit code.
        $PreviousTag = $describe | Select-Object -First 1
    }
}

if ([string]::IsNullOrWhiteSpace($PreviousTag)) {
    # First release, or the previous tag is unreachable. Everything reachable from the tag.
    $range = $CurrentTag
    Write-Verbose "No previous tag found; describing all commits reachable from $CurrentTag."
} else {
    $range = "$PreviousTag..$CurrentTag"
    Write-Verbose "Describing $range."
}

$log = git log --format='%H%x1f%P%x1f%s' $range 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Error "git log $range failed. The tag may not exist locally - a shallow checkout (fetch-depth 1) cannot see it."
    exit 1
}

$lines = @()
foreach ($entry in $log) {
    if ([string]::IsNullOrWhiteSpace($entry)) { continue }

    # Split on unit separator, not spaces: a subject contains spaces and the old -split ' ', 3
    # quietly mangled any commit whose subject started with something that looked like a hash.
    $parts = $entry -split "`u{001f}"
    if ($parts.Count -lt 3) { continue }

    $parents = $parts[1]
    $subject = $parts[2]

    if ($parents -match ' ') {
        # Merge commit. Only PR merges say anything a reader wants; branch-integration merges
        # duplicate the commits they bring in, which are listed individually anyway.
        if ($subject -match '^Merge pull request') {
            $lines += "- $subject"
        }
    } else {
        $lines += "- $subject"
    }
}

if ($lines.Count -eq 0) {
    $lines += "- No changes recorded between $range."
}

$lines -join "`n"
