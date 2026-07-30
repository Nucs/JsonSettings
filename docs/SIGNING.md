# Signing

Both shipped packages are **strong-named**. Neither is Authenticode-signed or NuGet
author-signed. Those are different things and the difference matters, so this document states
plainly what you get, what you do not, and how to check either claim yourself instead of
believing this page.

| | Status |
|---|---|
| Strong name (assembly identity) | ✅ `PublicKeyToken=cc7b13ffcd2ddd51` on every target framework |
| Authenticode signature on the DLLs | ❌ none |
| NuGet author signature on the packages | ❌ none |
| NuGet repository signature | ✅ applied by nuget.org to anything it serves, not by us |
| SHA-256 checksums per release | ✅ published as GitHub release assets |

## What the key is

`Open.snk` in the repository root is **Microsoft's published open-source strong-name key**:

```
596 bytes
sha256  b897629e0b20090c9219ac80392c037fa4cfcc2bdc21d3c45d9bf74b8df0f671
md5     2fe5bdce4ef988fa5bd982debb350668
```

It is byte-for-byte identical to
[`dotnet/arcade`'s `src/Microsoft.DotNet.Arcade.Sdk/tools/snk/Open.snk`](https://github.com/dotnet/arcade/blob/main/src/Microsoft.DotNet.Arcade.Sdk/tools/snk/Open.snk).
Microsoft signs parts of the .NET libraries with this key and publishes its **private** half so
that open-source builds can produce strong-named output without access to real signing
infrastructure. Its presence in this repository is therefore not a leaked secret — a key whose
owner publishes the private half has exactly one intended use, and this is it.

The same token is carried by assemblies you already reference:

```
netstandard.dll        cc7b13ffcd2ddd51
System.Memory.dll      cc7b13ffcd2ddd51
System.Buffers.dll     cc7b13ffcd2ddd51
```

Sharing a key does not collide identities. An assembly identity is
*(name, version, culture, public key token)*, and the name still differs.

## Identity, not authenticity

An `.snk` is a bare RSA key pair. No certificate, no subject, no issuer, no chain — nothing that
names a publisher. A strong name lets the runtime tell this `Nucs.JsonSettings` from a different
assembly of the same name and bind versions against it. That is all it does.

Because the private half of this key is public, **anyone can produce an assembly carrying our
identity**. Strong-naming is not a tamper-evidence mechanism here, and it is not one in general:
.NET Core and .NET 5+ do not verify strong-name signatures at load time at all.

Two consequences worth stating outright:

- **`InternalsVisibleTo` is not an access control.** It never was, and with a public key it is
  not even inconvenient to defeat.
- **A strong name is not provenance.** To check that a package really came from this project,
  compare its SHA-256 against the `.sha256` assets published with the corresponding
  [GitHub release](https://github.com/Nucs/JsonSettings/releases).

## Why sign at all, then

Because a strong-named assembly **cannot reference a weak-named one**. Shipping unsigned excluded
this library outright from every signed codebase — including the .NET Framework
line-of-business applications that are much of the audience for a settings-file library. There
was no workaround available to those consumers short of repackaging the DLL themselves.

That is the entire reason. It is a packaging concern, not a security one, and it should not be
described as a security improvement in release notes or anywhere else.

## Upgrading from 2.0.x

Everything published before 2.1.0 shipped **unsigned** (`PublicKeyToken=null`; verified against
`Nucs.JsonSettings` 2.0.1 and 2.0.2 on nuget.org). Gaining a strong name **changes the assembly
identity**, which has two practical effects:

- A `bindingRedirect` written against the old identity will not match the new one. Remove it
  rather than editing it — the redirect's `publicKeyToken="null"` is part of what it matches on.
- Anything that hardcodes the full identity string (some plugin loaders, `Assembly.Load` with a
  display name, serialized `System.Type` names in old config files) needs the token added.

Recompiling against 2.1.0 is otherwise sufficient; no source change is required.

## How this is enforced

Signing is configured in exactly one place, `Directory.Build.props`
(`SignAssembly`, `AssemblyOriginatorKeyFile`, `DelaySign`, `PublicSign`, `$(PublicKey)`).

The reason it is also *checked* is that losing it is silent. File names do not change, tests
still pass, `dotnet pack` still succeeds, and the only symptom is `PublicKeyToken=null` in an
identity string that nothing prints. NumSharp shipped unsigned for seven years exactly that way,
its `SignAssembly` sitting in a build configuration CI never used.

**Build-time and release-time** — [`.github/check-strong-name.ps1`](../.github/check-strong-name.ps1)
derives the expected key from `Open.snk` itself and checks three separate things per assembly,
because there are three different ways to end up with a file that looks signed:

| Check | Catches |
|---|---|
| Full public key present and equal to `Open.snk`'s | unsigned output, or output signed with another key |
| COR header `StrongNameSigned` flag set | `DelaySign=true` — signature reserved but never written |
| Signature blob present and **not all zeros** | `PublicSign=true` — real public key, flag set, signature is 128 zero bytes |

That third row is the one that is easy to miss: a public-signed assembly passes the first two
checks. The script also fails when it finds nothing to check, and cross-checks the `$(PublicKey)`
literal in `Directory.Build.props` against the key file so the two copies cannot drift.

It runs three times in [`build-and-release.yml`](../.github/workflows/build-and-release.yml):

1. **`build`** — over `src/*/bin/Release`, on every push and pull request.
2. **`pack`** — over every `lib/**/*.dll` *inside the produced `.nupkg` files*, before the
   artifact is uploaded. What ships is the package, not the directory it was built from.
3. **`publish-nuget`** — over the same packages again after the artifact round-trip, immediately
   before `dotnet nuget push`. A published version on nuget.org is permanent.

Each run asserts a minimum of **10** assemblies (2 packages × 5 target frameworks), so a package
that silently lost a target framework fails too.

**Runtime** — [`StrongNameTests`](../tests/JsonSettings.Tests/StrongNameTests.cs) asserts the
token and the full public key against the loaded assemblies, that the friend declarations are
keyed, and that friend access actually resolves. It states the expected key as its own literal
rather than reading it from the build; a test that read the key from the same place the build
does would agree with any key, including a replaced one.

## Weaving re-signs the assembly (`Nucs.JsonSettings.Autosave`)

`Nucs.JsonSettings.Autosave` autosaves by rewriting property setters at compile time with
[AspectInjector](https://github.com/pamidur/aspect-injector) (this replaced `Castle.DynamicProxy`
in 2.2.0; see [AOT.md](AOT.md)). That rewrite happens **after** `CoreCompile`, and the compiler
has already strong-name-signed the assembly by then. Editing a signed assembly leaves the
signature describing bytes that no longer exist, so a woven assembly fails verification. Measured
on a clean A/B of a signed two-project probe:

```
AspectInjector_Enabled=false   sn -vf App.dll -> "Assembly is valid"
AspectInjector_Enabled=true    sn -vf App.dll -> "Strong name validation failed."
```

AspectInjector used to re-sign the result itself; **2.9.0 retired that feature**
([release notes](https://github.com/pamidur/aspect-injector/releases/tag/2.9.0):
*"resigning assemblies feature is retired as it no longer supported by MS"*).

So the package ships its own restore step. `build/Nucs.JsonSettings.Autosave.targets` runs after
the weave and re-signs the assembly with the project's own `$(AssemblyOriginatorKeyFile)`, using
the same `sn -R` the SDK would. It is shipped in **both** `build/` and `buildTransitive/`, because
the weaving — and therefore the signature damage — happens in whichever assembly *declares* the
settings class:

- **This repository's own build** imports the file directly from `Directory.Build.targets`, which
  is why `JsonSettings.Autosave.dll` passes `check-strong-name.ps1` despite being woven.
- **A strong-named consumer** gets it transitively: their own assembly is woven where their
  `[Autosave]` class is declared, and the packaged target re-signs it with their key. Verified end
  to end by consuming the packed `.nupkg` from a separately-signed project and confirming
  `sn -vf` reports the consumer assembly valid.

Two things to know:

- **If `sn.exe` is not found**, the build emits warning **`NJS1001`** rather than failing. The
  assembly still loads and runs — .NET 5+ does not verify strong names at load — but it will fail
  an explicit `sn -vf`. `sn.exe` ships with the .NET Framework SDK and is Windows-only.
- **Opt out** with `<NucsAutosaveResignAfterWeaving>false</NucsAutosaveResignAfterWeaving>`. The
  target already no-ops for delay-signed and public-signed builds, which never carried a valid
  signature to restore.

The "signature blob present and not all zeros" check in `check-strong-name.ps1` (third row of the
table above) is what would catch a re-sign that silently failed: a re-signed assembly carries a
real 128-byte signature, an unrestored one does not.

## Verifying it yourself

Token of an installed package:

```powershell
[System.Reflection.AssemblyName]::GetAssemblyName("$env:USERPROFILE\.nuget\packages\nucs.jsonsettings\2.1.0\lib\net8.0\JsonSettings.dll").GetPublicKeyToken() |
    ForEach-Object { '{0:x2}' -f $_ }
```

On .NET Framework, or with the Windows SDK on PATH:

```
sn -T JsonSettings.dll
```

Everything the release pipeline checks, against any directory or `.nupkg`:

```powershell
pwsh ./.github/check-strong-name.ps1 -Path <dir-or-nupkg> -MinimumAssemblies 10
```

That the key really is Microsoft's, rather than something that merely says so:

```bash
curl -sL -o arcade.snk https://raw.githubusercontent.com/dotnet/arcade/main/src/Microsoft.DotNet.Arcade.Sdk/tools/snk/Open.snk
cmp arcade.snk Open.snk && echo "byte-identical"
```

## What real authenticity would take

Two things this repository does not have, both needing a code-signing certificate from a public
CA (or a service such as the .NET Foundation's / SignPath's OSS programs):

- **Authenticode** on each DLL — signs the binaries, so Windows and enterprise allow-listing can
  attribute them to a named publisher.
- **NuGet author signing** (`dotnet nuget sign`) — signs the package, so consumers can enforce
  `<trustedSigners>` policy in `nuget.config`.

Both would be layered **on top of** strong-naming, not instead of it; they answer "who produced
this", while the strong name answers "which assembly is this". Adding either is a change to the
release pipeline plus a certificate and its secret storage, and is deliberately out of scope for
the open-source key described here.
