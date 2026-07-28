# Upgrading from 2.0.x to 2.1.0

**Status: the public API and the encrypted file format are unchanged. Four behaviours are not.**

This document records what was measured, so the question does not have to be re-litigated from
scratch. The regression suite behind it lives in
[`tests/JsonSettings.Tests/Upgrade/`](../tests/JsonSettings.Tests/Upgrade).

---

## Contents

- [How this was measured](#how-this-was-measured)
- [What did not change](#what-did-not-change)
- [What changed](#what-changed)
- [The regression suite](#the-regression-suite)
- [Reproducing](#reproducing)

---

## How this was measured

Every expectation here is the behaviour `Nucs.JsonSettings` 2.0.1 and 2.0.2 actually exhibited,
established by compiling the same scenario bodies against those packages **from nuget.org** and
recording the result — not by reading their source and reasoning about it.

- 42 self-contained scenarios, run against 2.0.1, 2.0.2 and 2.1.0
- 7 cross-version fixtures, written by one version and read by the other, both directions
- **2.0.1 and 2.0.2 agreed on all 42.** "2.0.x" below means both.

The published assemblies were also diffed member by member with `MetadataLoadContext`.

## What did not change

**The public API.** Every public and protected member of both assemblies is identical to 2.0.2 —
393 members on `Nucs.JsonSettings`, 18 on `Nucs.JsonSettings.Autosave`. Nothing added, removed or
resignatured. All five 2.1.0 target-framework assets expose the same surface; the only deltas
between them are BCL artifacts (`_Attribute`/`_Exception` on .NET Framework, `ISpanFormattable` on
modern .NET).

**The encrypted file format, in both directions.**

| fixture | written by 2.0.1, read by 2.1.0 | written by 2.1.0, read by 2.0.1 |
|---|---|---|
| plain | ok | ok |
| `WithEncryption` | ok | ok |
| `WithBase64().WithEncryption()` | ok | ok |
| `WithEncryption().WithBase64()` | ok | ok |

The cipher is the same (AES with a 128-bit block in CBC, previously spelled `RijndaelManaged`) and
the PBKDF2 pseudorandom function is still SHA-1. **Rolling back to 2.0.x is safe.**

**Assembly identity.** `AssemblyVersion` moved `2.0.1.0` → `2.1.0.0`. As of 2.1.0 the assemblies
were not strong-named, so no binding redirects were required.

## What changed

Sixteen of the 42 scenarios changed. They reduce to four causes.

### 1. A custom module inside the encryption layer

Modules hook the `Encrypt`/`Decrypt` pair. `Encrypt` is an ordinary event so handlers run in attach
order; `Decrypt` is declared with a reverse insert so handlers run in reverse attach order. The two
are symmetric, so a module attached **before** `WithEncryption` wraps the plaintext and one attached
**after** wraps the ciphertext.

2.1.0 added a UTF-8 validity check on the bytes `RijndaelModule` hands back, as a tie-breaker for
wrong-password detection. That check assumes the layer immediately inside the encryption is UTF-8
JSON — true for the built-in modules, but never a stated part of the module contract.

| chain | 2.0.x | 2.1.0 |
|---|---|---|
| `WithModule(gzip).WithEncryption(pw)` | ok | `JsonSettingsException: Password appears to be invalid.` |
| `WithModule(xor).WithEncryption(pw)` | ok | same |
| `WithModule(gzip).WithEncryption(pw).WithModule(gzip)` | ok | same |
| `WithEncryption(pw).WithModule(gzip)` | ok | ok |
| `WithModule(hex).WithEncryption(pw)` | ok | ok |
| `WithBase64().WithEncryption(pw)` | ok | ok |

The hex row is the discriminator: its output is **not JSON but is valid UTF-8**, and it passes. The
rejection is specifically about UTF-8 validity.

A settings file already written by 2.0.x through such a chain **stops being readable**. Not just a
configuration that no longer works — data that can no longer be reached. Fixtures for both are
embedded in `ModuleChainingTests`.

**Workaround:** attach the custom module after `WithEncryption`, so it wraps the ciphertext.

### 2. Recovery no longer sees a short encrypted file

`JsonSettings.Load` runs `OnDecrypt` **before** it consults the recovery hook. Anything thrown from
the decrypt stage bypasses `RecoveryModule` entirely. In 2.0.x a short file produced a tolerated
short read, so control reached the hook; 2.1.0 rejects it with `EndOfStreamException`.

| file | 2.0.x, `WithRecovery(RenameAndLoadDefault)` | 2.1.0 |
|---|---|---|
| zero-length | recovered | `EndOfStreamException` |
| 1 byte | recovered | `EndOfStreamException` |
| 10 bytes (inside the IV) | recovered | `EndOfStreamException` |
| 16 bytes (IV, no ciphertext) | recovered | recovered |
| 20 / 48 bytes (past the IV) | `JsonSettingsException` | `JsonSettingsException` |
| corrupt / empty / whitespace plaintext | recovered | recovered |

**The zero-length row is the one that matters.** A settings file of zero bytes is the ordinary
result of a process dying mid-save, a full disk, or a host that truncates on open — the most likely
damaged state a real deployment meets, and precisely what `RecoveryModule` exists to absorb.

`RecoveryAction.Throw` is affected too: it promised `JsonSettingsRecoveryException` and now yields
`EndOfStreamException` for these inputs.

### 3. `EndOfStreamException` escapes `catch (JsonSettingsException)`

One type to catch around a load, whatever the file turned out to contain, is what
`JsonSettingsException` is for. `EndOfStreamException` derives from `IOException`, so a consumer
catching the documented type no longer covers a file shorter than an initialization vector.

Measured on 2.0.1, no recovery module, encrypted:

```
len 0, 1, 10, 16  ->  JsonSettingsException "The settings file is empty!"
len 20, 48        ->  JsonSettingsException "Password appears to be invalid."
```

On 2.1.0 the first three escape as `EndOfStreamException`; `len 16` and beyond are unchanged.

### 4. Newtonsoft.Json 13 caps nesting at 64

Not this library's own code. Json.NET 13.0.1 changed the default `MaxDepth` from null (unlimited) to
64, and 2.1.0 upgraded Newtonsoft.Json 12.0.3 → 13.0.3.
`JsonSettings.SerializationSettings` never sets `MaxDepth`, so it inherits whatever the default is.

| node chain depth | 2.0.x | 2.1.0 |
|---|---|---|
| 2, 30, 60, 63 | ok | ok |
| 64, 65, 70, 200 | ok | `JsonSettingsException` / `JsonReaderException` |

64 is the first *failing* depth, not the last passing one — the settings object contributes the
outermost container, so a 64-node chain is 65 levels by Json.NET's count. Collection nesting counts
the same way.

The asymmetry is what makes it easy to miss: **saving a deep object still works.** An application
can run its whole lifetime writing a file it will fail to read on next start.

**Restore:**

```csharp
JsonSettings.SerializationSettings.MaxDepth = null;
```

### Related, and arguably intended

The ~0.4% of wrong-password loads whose output survives PKCS7 padding validation used to reach the
JSON parser and surface as `Unable to parse file`. They now throw immediately with the correct
message. If `WithEncryption` was combined with `WithRecovery`, that branch previously reset settings
to defaults on a mistyped password; it now reports the bad password. Better — but different.

## The regression suite

`tests/JsonSettings.Tests/Upgrade/` — 45 tests, run on all five target frameworks.

| file | covers |
|---|---|
| `BaselineModules.cs` | the gzip / xor / hex test modules and the shared settings shape |
| `ModuleChainingTests.cs` | cause 1, including two fixtures written by 2.0.1 |
| `DamagedEncryptedFileTests.cs` | causes 2 and 3 |
| `SerializationDepthTests.cs` | cause 4 |

**These assert the 2.0.x baseline.** As of writing, 26 pass and 19 fail, identically on every
framework — so the causes are in shared code, not framework-specific.

**A red test there is a behaviour change, not a broken test.** Each failure is a decision: either
the library regressed and should be fixed, or the change is wanted, in which case flip the assertion
**and** say so in the release notes. Flipping one quietly turns a record of a known break into a
record of nothing.

The 87 pre-existing tests are unaffected and green on all five frameworks.

## Reproducing

```bash
# the regression suite, one framework
dotnet test tests/JsonSettings.Tests/JsonSettings.Tests.csproj -f net8.0 --filter "FullyQualifiedName~Upgrade"

# everything else, which must stay green
dotnet test tests/JsonSettings.Tests/JsonSettings.Tests.csproj --filter "FullyQualifiedName!~Upgrade"
```

To re-measure the baseline rather than trust this document, point a scratch project at
`Nucs.JsonSettings` 2.0.1 and run the same scenario bodies; every table above came from doing
exactly that.
