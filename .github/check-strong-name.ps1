<#
.SYNOPSIS
    Fails unless every assembly it is pointed at is really strong-named with this repository's
    Open.snk.

.DESCRIPTION
    Signing is configured in Directory.Build.props and is therefore exactly as reliable as one
    conditional property nobody looks at again. The failure mode is silent by construction: an
    unsigned build produces the same file names, the same tests pass, `dotnet pack` succeeds,
    and the only visible symptom is `PublicKeyToken=null` in an identity string that nothing
    prints. NumSharp shipped unsigned for seven years that way - SignAssembly sat in a
    configuration CI never built - and nothing in its pipeline noticed.

    So this asserts the property that actually matters at the point it matters: not "the build
    was configured to sign" but "this DLL, the one about to be published, carries our identity".

    Three things are checked per assembly, because there are three different ways to end up
    with a file that LOOKS signed:

      1. The full public key is present and equals the key in Open.snk. Catches unsigned
         output (no key at all) and output signed with some other key.
      2. The COR header's StrongNameSigned flag is set. Catches DelaySign=true, which reserves
         the signature and leaves the flag clear.
      3. The strong-name signature blob is present and is not all zeros. Catches
         PublicSign=true - an open-source-build shortcut that stamps the real public key AND
         sets the StrongNameSigned flag while writing a signature of nothing but zero bytes.
         Checks 1 and 2 both pass on a public-signed assembly; only this one fails it.

    What is deliberately NOT checked is the cryptographic validity of that signature. Open.snk's
    private half is published by Microsoft, so a valid signature proves only that the file was
    not corrupted in transit - it identifies no one, and `sn -vf` would be a Windows-only
    dependency bought for nothing. See Directory.Build.props for why the key is still worth
    having.

    The expected key is DERIVED FROM Open.snk rather than hardcoded, so the file on disk is the
    single source of truth for what we sign with. Two things are then pinned against it:
    -ExpectedToken, so swapping the key file is a deliberate visible act rather than a quiet
    identity change, and the $(PublicKey) literal in Directory.Build.props, which exists only
    because InternalsVisibleTo needs the key spelled out and which would otherwise be free to
    drift out of agreement with the key actually in use.

    Finding nothing to check is a failure, not a pass. A gate that silently examines zero files
    is worse than no gate, because it reports success.

.PARAMETER Path
    What to check. Each entry may be a directory (scanned recursively for -Include), a .nupkg
    (every lib/**/*.dll inside it is checked, read from the archive without extracting), or a
    single .dll.

.PARAMETER Include
    Filename filter for directory scans. Defaults to Nucs.JsonSettings*.dll so a bin/ directory
    full of restored dependencies does not get audited - Newtonsoft and AspectInjector are signed
    with their own keys and failing on that would be wrong. (The shipped assemblies carry the Nucs.
    prefix; the .csproj/folder names are the short JsonSettings* form - see the AssemblyName note in
    each csproj.)

.PARAMETER MinimumAssemblies
    Fail if fewer than this many assemblies were checked. The release pipeline passes the exact
    number it expects, which is what turns "the packages are signed" into "all fifteen shipped
    assets are signed" - a package silently missing a target framework fails here too.

.PARAMETER ExpectedToken
    The public key token Open.snk must produce. Defaults to Microsoft's published open-source
    key; see Directory.Build.props.

.EXAMPLE
    pwsh ./.github/check-strong-name.ps1 -Path src -MinimumAssemblies 10

.EXAMPLE
    pwsh ./.github/check-strong-name.ps1 -Path artifacts/nuget -MinimumAssemblies 10
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string[]]$Path,
    [string]$Include = 'Nucs.JsonSettings*.dll',
    [int]$MinimumAssemblies = 1,
    [string]$ExpectedToken = 'cc7b13ffcd2ddd51'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# ---------------------------------------------------------------------------------------------
# Derive the expected identity from Open.snk.
#
# An .snk holds a CryptoAPI PRIVATEKEYBLOB. The strong-name public key is a 12-byte header
# (signature algorithm, hash algorithm, blob length) followed by the same blob rewritten as a
# PUBLICKEYBLOB - identical bytes except for the type byte and the "RSA2"/"RSA1" magic, with
# everything after the modulus dropped. The token is the low 8 bytes of its SHA-1, reversed.
# ---------------------------------------------------------------------------------------------
function Get-PublicKeyFromSnk {
    param([Parameter(Mandatory)][string]$SnkPath)

    $snk = [System.IO.File]::ReadAllBytes($SnkPath)
    if ($snk.Length -lt 20) { throw "$SnkPath is too short to be a key pair ($($snk.Length) bytes)." }
    if ($snk[0] -ne 0x07) { throw "$SnkPath is not a PRIVATEKEYBLOB (type byte 0x$('{0:x2}' -f $snk[0]), expected 0x07). A public-key-only .snk cannot sign." }

    $magic = [System.Text.Encoding]::ASCII.GetString($snk, 8, 4)
    if ($magic -ne 'RSA2') { throw "$SnkPath has magic '$magic', expected 'RSA2'." }

    $modulusLength = [System.BitConverter]::ToInt32($snk, 12) / 8
    if ($snk.Length -lt 20 + $modulusLength) { throw "$SnkPath is truncated: needs at least $(20 + $modulusLength) bytes for a $($modulusLength * 8)-bit modulus, has $($snk.Length)." }

    $publicBlob = [byte[]]::new(20 + $modulusLength)
    $publicBlob[0] = 0x06                                    # PUBLICKEYBLOB
    $publicBlob[1] = 0x02                                    # CUR_BLOB_VERSION
    [System.Array]::Copy($snk, 4, $publicBlob, 4, 4)         # algorithm id
    [System.Text.Encoding]::ASCII.GetBytes('RSA1').CopyTo($publicBlob, 8)
    [System.Array]::Copy($snk, 12, $publicBlob, 12, 8)       # bit length + public exponent
    [System.Array]::Copy($snk, 20, $publicBlob, 20, $modulusLength)

    $key = [byte[]]::new(12 + $publicBlob.Length)
    [System.BitConverter]::GetBytes([int]0x00002400).CopyTo($key, 0)   # CALG_RSA_SIGN
    [System.BitConverter]::GetBytes([int]0x00008004).CopyTo($key, 4)   # CALG_SHA1
    [System.BitConverter]::GetBytes([int]$publicBlob.Length).CopyTo($key, 8)
    $publicBlob.CopyTo($key, 12)

    $hash = [System.Security.Cryptography.SHA1]::HashData($key)
    $token = $hash[($hash.Length - 8)..($hash.Length - 1)]
    [System.Array]::Reverse($token)

    return [pscustomobject]@{
        Key   = [System.Convert]::ToHexString($key).ToLowerInvariant()
        Token = [System.Convert]::ToHexString($token).ToLowerInvariant()
    }
}

# ---------------------------------------------------------------------------------------------
# Read an assembly's strong-name facts straight out of the PE image.
#
# Metadata only: no Assembly.Load, no MetadataLoadContext, nothing that needs the assembly's
# dependencies present or its target framework installed. That is what lets one pwsh process
# check net48 and net10.0 assets side by side, and lets the release gate read DLLs out of a
# .nupkg without unpacking it.
# ---------------------------------------------------------------------------------------------
function Get-StrongNameFacts {
    param([Parameter(Mandatory)][byte[]]$Image)

    $stream = [System.IO.MemoryStream]::new($Image, $false)
    $peReader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        if (-not $peReader.HasMetadata) { return $null }   # native or resource-only DLL

        $headers = $peReader.PEHeaders
        if ($null -eq $headers.CorHeader) { return $null }

        # Called statically because GetMetadataReader is a C# extension method on PEReader
        # (PEReaderExtensions), and PowerShell does not resolve extension methods as instance calls.
        $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
        if (-not $reader.IsAssembly) { return $null }      # netmodule, not an assembly

        $definition = $reader.GetAssemblyDefinition()

        # Assigned in two statements rather than from an `if` expression: a zero-length array is
        # emitted to the output stream as no items at all, so `$k = if (...) { [byte[]]::new(0) }`
        # yields $null - which is the unsigned case, the one this has to report accurately.
        $keyBytes = [byte[]]::new(0)
        if (-not $definition.PublicKey.IsNil) { $keyBytes = $reader.GetBlobBytes($definition.PublicKey) }

        # StrongNameSigned = 0x8. Clear on a delay-signed assembly.
        $signedFlag = ($headers.CorHeader.Flags -band 0x8) -ne 0

        # The reserved signature blob. Public signing fills it with zeros; real signing does not.
        $directory = $headers.CorHeader.StrongNameSignatureDirectory
        $signatureBytes = 0
        $signatureNonZero = $false
        if ($directory.Size -gt 0) {
            $offset = 0
            if ($headers.TryGetDirectoryOffset($directory, [ref]$offset)) {
                $signatureBytes = $directory.Size
                for ($i = 0; $i -lt $directory.Size; $i++) {
                    if ($Image[$offset + $i] -ne 0) { $signatureNonZero = $true; break }
                }
            }
        }

        return [pscustomobject]@{
            Name             = $reader.GetString($definition.Name)
            Key              = [System.Convert]::ToHexString($keyBytes).ToLowerInvariant()
            SignedFlag       = $signedFlag
            SignatureBytes   = $signatureBytes
            SignatureNonZero = $signatureNonZero
        }
    }
    finally {
        $peReader.Dispose()
        $stream.Dispose()
    }
}

$snkPath = Join-Path $repoRoot 'Open.snk'
if (-not (Test-Path $snkPath)) {
    Write-Host "::error::Open.snk is missing from the repository root. Nothing can be signed without it."
    exit 1
}

$expected = Get-PublicKeyFromSnk -SnkPath $snkPath
Write-Host "Open.snk -> public key token $($expected.Token)"

$failed = $false

if ($expected.Token -ne $ExpectedToken) {
    Write-Host "::error::Open.snk produces token $($expected.Token), expected $ExpectedToken. The signing key has been replaced. If that is intentional, update -ExpectedToken here, `$(PublicKey) in Directory.Build.props, and StrongNameTests - and say why, because every previously published assembly carries the old identity."
    $failed = $true
}

# ---------------------------------------------------------------------------------------------
# The $(PublicKey) literal has to agree with the key file. It is a second copy of one fact,
# needed only because InternalsVisibleTo cannot derive a key from an .snk, and a stale copy
# produces friend references that resolve to nobody - which surfaces as "X is inaccessible due
# to its protection level" on a member that is plainly visible, and sends the reader looking
# anywhere but here.
# ---------------------------------------------------------------------------------------------
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
[xml]$props = Get-Content $propsPath -Raw

# XPath rather than $props.Project.PropertyGroup.PublicKey: that form enumerates members across
# the PropertyGroup array, and under Set-StrictMode reading a property the other groups do not
# have is a terminating error rather than $null.
$declaredKey = $props.SelectSingleNode('/Project/PropertyGroup/PublicKey')?.InnerText
$declaredToken = $props.SelectSingleNode('/Project/PropertyGroup/StrongNamePublicKeyToken')?.InnerText

if ([string]::IsNullOrWhiteSpace($declaredKey)) {
    Write-Host "::error::Directory.Build.props declares no <PublicKey>. Friend assembly references would be generated keyless, which is CS1726 in a signed assembly."
    $failed = $true
}
elseif ($declaredKey.Trim().ToLowerInvariant() -ne $expected.Key) {
    Write-Host "::error::Directory.Build.props <PublicKey> does not match Open.snk. Replace it with:"
    Write-Host "    $($expected.Key)"
    $failed = $true
}
else {
    Write-Host "Directory.Build.props <PublicKey> matches Open.snk."
}

if ($declaredToken -and $declaredToken.Trim().ToLowerInvariant() -ne $expected.Token) {
    Write-Host "::error::Directory.Build.props <StrongNamePublicKeyToken> is '$declaredToken', but Open.snk produces '$($expected.Token)'."
    $failed = $true
}

# ---------------------------------------------------------------------------------------------
# Collect the assemblies to check. Each becomes a (label, bytes) pair so a DLL read out of a
# .nupkg reports as "package.nupkg -> lib/net8.0/x.dll" rather than as a temp path.
# ---------------------------------------------------------------------------------------------
$targets = [System.Collections.Generic.List[object]]::new()

foreach ($entry in $Path) {
    $resolved = if ([System.IO.Path]::IsPathRooted($entry)) { $entry } else { Join-Path $repoRoot $entry }

    if (-not (Test-Path $resolved)) {
        Write-Host "::error::Path not found: $entry"
        $failed = $true
        continue
    }

    $item = Get-Item $resolved

    if ($item.PSIsContainer) {
        # .nupkg files inside a directory are unpacked, not skipped: pointing the release gate at
        # artifacts/nuget must audit what is in the packages, not the zero loose DLLs beside them.
        # One enumeration, then explicit predicates. -Filter is deliberately not used: its wildcard
        # matching is the Win32 one, where a pattern like *.nupkg can also match longer extensions
        # such as .snupkg (which sit in the same directory here, contain only .pdb files, and would
        # be reported as packages with no assemblies in them), and its case sensitivity follows the
        # operating system - this gate runs on both windows-latest and ubuntu-latest.
        $all = @(Get-ChildItem $resolved -Recurse -File)
        $packages = @($all | Where-Object { $_.Extension -eq '.nupkg' })
        $dlls = @($all | Where-Object { $_.Name -like $Include -and $_.FullName -notmatch '[\\/]obj[\\/]' })

        foreach ($package in $packages) { $targets.Add([pscustomobject]@{ Kind = 'nupkg'; Item = $package }) }
        foreach ($dll in $dlls) { $targets.Add([pscustomobject]@{ Kind = 'dll'; Item = $dll }) }

        if ($packages.Count -eq 0 -and $dlls.Count -eq 0) {
            Write-Host "::warning::$entry contained no .nupkg and nothing matching '$Include'."
        }
    }
    elseif ($item.Extension -eq '.nupkg') { $targets.Add([pscustomobject]@{ Kind = 'nupkg'; Item = $item }) }
    else { $targets.Add([pscustomobject]@{ Kind = 'dll'; Item = $item }) }
}

$checked = 0

function Test-Assembly {
    param([string]$Label, [byte[]]$Image)

    $facts = Get-StrongNameFacts -Image $Image
    if ($null -eq $facts) {
        Write-Host "  skip    $Label (no managed assembly metadata)"
        return $true
    }

    $script:checked++

    # ONE diagnosis, most specific first, rather than every failing check. The three checks are
    # not independent - an unsigned assembly also has no flag and no blob - so listing all of
    # them produces self-contradicting output ("is delay-signed; is public-signed") that reads
    # like the checker is confused about what it found.
    $diagnosis = $null
    if ($facts.Key -eq '') {
        $diagnosis = 'not strong-named at all: no public key. SignAssembly did not take effect for this project'
    }
    elseif ($facts.Key -ne $expected.Key) {
        $diagnosis = "signed with a DIFFERENT key: $($facts.Key.Substring(0, 64))... AssemblyOriginatorKeyFile is not pointing at Open.snk"
    }
    elseif (-not $facts.SignedFlag) {
        $diagnosis = 'delay-signed: carries our public key, but the StrongNameSigned flag is clear and the signature was never written. DelaySign must be false'
    }
    elseif ($facts.SignatureBytes -eq 0) {
        $diagnosis = 'has our public key and the StrongNameSigned flag, but no signature blob at all'
    }
    elseif (-not $facts.SignatureNonZero) {
        $diagnosis = "public-signed: carries our public key AND the StrongNameSigned flag, but the $($facts.SignatureBytes)-byte signature is all zeros. PublicSign must be false"
    }

    if ($diagnosis) {
        Write-Host "::error::$Label - $diagnosis."
        return $false
    }

    Write-Host "  ok      $Label ($($facts.Name), $($facts.SignatureBytes)-byte signature)"
    return $true
}

foreach ($target in $targets) {
    if ($target.Kind -eq 'nupkg') {
        Write-Host "::group::$($target.Item.Name)"
        $archive = [System.IO.Compression.ZipFile]::OpenRead($target.Item.FullName)
        try {
            $libEntries = @($archive.Entries | Where-Object { $_.FullName -match '^lib/.+\.dll$' } | Sort-Object FullName)
            if ($libEntries.Count -eq 0) {
                Write-Host "::error::$($target.Item.Name) contains no lib/**/*.dll. A package with no assemblies in it is not something to publish."
                $failed = $true
            }
            foreach ($libEntry in $libEntries) {
                $memory = [System.IO.MemoryStream]::new()
                $entryStream = $libEntry.Open()
                try { $entryStream.CopyTo($memory) } finally { $entryStream.Dispose() }
                if (-not (Test-Assembly -Label "$($target.Item.Name) -> $($libEntry.FullName)" -Image $memory.ToArray())) { $failed = $true }
                $memory.Dispose()
            }
        }
        finally { $archive.Dispose() }
        Write-Host '::endgroup::'
    }
    else {
        $relative = $target.Item.FullName.Replace($repoRoot, '').TrimStart('\', '/')
        if (-not (Test-Assembly -Label $relative -Image ([System.IO.File]::ReadAllBytes($target.Item.FullName)))) { $failed = $true }
    }
}

Write-Host ''
Write-Host "Checked $checked assembl$(if ($checked -eq 1) { 'y' } else { 'ies' })."

if ($checked -lt $MinimumAssemblies) {
    Write-Host "::error::Expected at least $MinimumAssemblies assemblies but checked $checked. Either the build produced less than it should have, or this gate was pointed somewhere it finds nothing - both are failures, because a gate that checks nothing reports success."
    $failed = $true
}

if ($failed) {
    Write-Host ''
    Write-Host 'Signing is configured in Directory.Build.props: SignAssembly, AssemblyOriginatorKeyFile,'
    Write-Host 'DelaySign and PublicSign, plus the $(PublicKey) literal that keyed InternalsVisibleTo'
    Write-Host 'attributes are generated from. Nothing here is fixed by re-running the build.'
    exit 1
}

Write-Host "Every assembly carries $($expected.Token)."
exit 0
