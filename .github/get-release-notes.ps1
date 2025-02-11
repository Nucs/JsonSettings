# Example: .\get-release-notes.ps1 "refs/tags/v2.1.0.10-beta"  "refs/tags/v2.1.0.0"
param (
    [Parameter(Mandatory=$true)]
    [string]$CurrentTag,
    
    [Parameter(Mandatory=$false)]
    [string]$PreviousTag
)

# Use provided previous tag or get the previous release tag
if (-not $PreviousTag) {
    $PreviousTag = git describe --tags --abbrev=0 "$CurrentTag^" 2>$null
}

if (-not $PreviousTag -or $LASTEXITCODE -ne 0) {
    # If no previous tag exists or provided, get all commits
    $range = $CurrentTag
} else {
    $range = "$PreviousTag..$CurrentTag"
}

# Get commit messages between tags
# Include both direct commits and merge commits, but format them differently
$commits = git log --format="%H %P %s" $range | ForEach-Object {
    $parts = $_ -split ' ', 3  # Split into hash, parent hashes, and subject
    $hash = $parts[0]
    $parents = $parts[1]
    $subject = $parts[2]
    
    # If commit has multiple parents, it's a merge commit
    if ($parents -match ' ') {
        # Only include if it looks like a PR merge
        if ($subject -match '^Merge pull request') {
            "- $subject"
        }
    } else {
        # Regular commit
        "- $subject"
    }
}

# Output the commits
$commits -join "`n"
