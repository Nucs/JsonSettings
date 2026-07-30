# Upgrading from 2.0.x to 2.1.0 (and what 2.2.0 restores)

**2.1.0 shipped four behaviour changes against 2.0.x. Three were bugs and are restored verbatim;
the fourth is restored for every realistic graph behind a working depth backstop.** The public API
and the encrypted file format were never affected, in either release.

This document records what was measured, so the question does not have to be re-litigated from
scratch. The regression suite behind it lives in
[`tests/JsonSettings.Tests/Upgrade/`](../tests/JsonSettings.Tests/Upgrade).

---

## Contents

- [Resolution at a glance](#resolution-at-a-glance)
- [How this was measured](#how-this-was-measured)
- [What never changed](#what-never-changed)
- [The four changes, and how each was resolved](#the-four-changes-and-how-each-was-resolved)
- [The regression suite](#the-regression-suite)
- [Reproducing](#reproducing)

---

## Resolution at a glance

| # | 2.1.0 change | verdict | this build |
|---|---|---|---|
| 1 | custom non-UTF-8 module inside `WithEncryption` rejected with the correct password | bug | **restored** — the wrong-password UTF-8 check moved to after the whole decrypt chain, so it inspects the final plaintext, not a module's intermediate output |
| 2 | `RecoveryModule` no longer absorbs a short/zero-length encrypted file | bug | **restored** — a file shorter than one IV is treated as empty again and reaches the recovery hook |
| 3 | `EndOfStreamException` escaped `catch (JsonSettingsException)` | bug | **restored** — resolved by the same change as #2; those failures are `JsonSettingsException` again |
| 4 | Newtonsoft 13 capped nesting at 64 | inherited accident | **restored with a bound** — default `MaxDepth` set explicitly to **128**, unlimited via `null` |

The wrong-password diagnostic that motivated #1, and the truncated-file rejection that motivated
#2/#3, are both **kept** — the fixes preserve the improvement and drop only the collateral damage.

Measured against 2.0.1 across 42 scenarios: **13 restored to the 2.0.1 result, 3 deliberately
different (the depth bound), 26 unchanged, 0 unexpectedly different.**

## How this was measured

Every expectation here is the behaviour `Nucs.JsonSettings` 2.0.1 and 2.0.2 actually exhibited,
established by compiling the same scenario bodies against those packages **from nuget.org** and
recording the result — not by reading their source and reasoning about it.

- 42 self-contained scenarios, run against 2.0.1, 2.0.2, 2.1.0 **and this build**
- 7 cross-version fixtures, written by one version and read by the other, both directions
- **2.0.1 and 2.0.2 agreed on all 42.** "2.0.x" below means both.

The published assemblies were also diffed member by member with `MetadataLoadContext`.

## What never changed

**The public API.** Every public and protected member of both assemblies is identical to 2.0.2 —
393 members on `Nucs.JsonSettings`, 18 on `Nucs.JsonSettings.Autosave`. Nothing added, removed or
resignatured. The fixes here add no public surface: the moved UTF-8 check and the IV-length guard
are `protected virtual` methods on `RijndaelModule`, and the depth default is a property value.

**The encrypted file format, in every direction.**

| fixture | 2.0.1 → 2.1.0 | 2.1.0 → 2.0.1 | 2.0.1 → this build | this build → 2.0.1 |
|---|---|---|---|---|
| plain | ok | ok | ok | ok |
| `WithEncryption` | ok | ok | ok | ok |
| `WithBase64().WithEncryption()` | ok | ok | ok | ok |
| `WithEncryption().WithBase64()` | ok | ok | ok | ok |
| `WithModule(gzip).WithEncryption()` | **broke** | ok | **ok (restored)** | ok |
| `WithModule(xor).WithEncryption()` | **broke** | ok | **ok (restored)** | ok |

The cipher is the same (AES with a 128-bit block in CBC, previously spelled `RijndaelManaged`) and
the PBKDF2 pseudorandom function is still SHA-1. **Rolling back to 2.0.x is safe.**

## The four changes, and how each was resolved

### 1. A custom module inside the encryption layer

Modules hook the `Encrypt`/`Decrypt` pair. `Encrypt` is an ordinary event so handlers run in attach
order; `Decrypt` is declared with a reverse insert so handlers run in reverse attach order. The two
are symmetric, so a module attached **before** `WithEncryption` wraps the plaintext and one attached
**after** wraps the ciphertext.

2.1.0 added a UTF-8 validity check on the bytes `RijndaelModule` hands back, as a tie-breaker for
wrong-password detection. That check assumes the layer immediately inside the encryption is UTF-8
JSON — true for the built-in modules, but never a stated part of the module contract.

| chain | 2.0.x | 2.1.0 | this build |
|---|---|---|---|
| `WithModule(gzip).WithEncryption(pw)` | ok | `Password appears to be invalid.` | **ok** |
| `WithModule(xor).WithEncryption(pw)` | ok | same | **ok** |
| `WithModule(gzip).WithEncryption(pw).WithModule(gzip)` | ok | same | **ok** |
| `WithEncryption(pw).WithModule(gzip)` | ok | ok | ok |
| `WithModule(hex).WithEncryption(pw)` | ok | ok | ok |
| `WithBase64().WithEncryption(pw)` | ok | ok | ok |

The hex row is the discriminator: its output is **not JSON but is valid UTF-8**, and it passed on
2.1.0 too. The rejection was specifically about UTF-8 validity.

A settings file already written by 2.0.x through such a chain **stopped being readable** on 2.1.0 —
not just a configuration that no longer works, data that could no longer be reached. Fixtures
written by 2.0.1 for both gzip and xor are embedded in `ModuleChainingTests` and load again.

**Fix.** The UTF-8 check runs on `AfterDecrypt` now, which `JsonSettings.Load` raises once the whole
decrypt chain has run, so it inspects the *final* plaintext regardless of how many modules produced
it. The wrong-password diagnostic still fires for the common encryption-only case (the plaintext
there is the final plaintext); it no longer fires on a still-encoded intermediate. `RijndaelModule.cs`.

### 2. Recovery no longer saw a short encrypted file

`JsonSettings.Load` runs `OnDecrypt` **before** it consults the recovery hook. Anything thrown from
the decrypt stage bypasses `RecoveryModule` entirely. In 2.0.x a file shorter than one IV decrypted
to an empty payload, which reached the empty-file recovery branch; 2.1.0 raised
`EndOfStreamException` from the IV read instead.

| file | 2.0.x `WithRecovery(RenameAndLoadDefault)` | 2.1.0 | this build |
|---|---|---|---|
| zero-length | recovered | `EndOfStreamException` | **recovered** |
| 1 byte | recovered | `EndOfStreamException` | **recovered** |
| 10 bytes (inside the IV) | recovered | `EndOfStreamException` | **recovered** |
| 16 bytes (IV, no ciphertext) | recovered | recovered | recovered |
| 20 / 48 bytes (past the IV) | `JsonSettingsException` | `JsonSettingsException` | `JsonSettingsException` |
| corrupt / empty / whitespace plaintext | recovered | recovered | recovered |

**The zero-length row is the one that matters.** A zero-byte settings file is the ordinary result of
a save interrupted mid-write, a full disk, or a host that truncates on open — the most likely
damaged state a real deployment meets, and precisely what `RecoveryModule` exists to absorb.
`RecoveryAction.Throw` was affected too: it promised `JsonSettingsRecoveryException` and yielded
`EndOfStreamException`.

**Fix.** `RijndaelModule.DecryptInternal` now treats input shorter than one IV as an empty payload,
restoring the pre-2.1.0 path into recovery. This is **not** the silent-garbage case the IV check
guards against: that needs a full IV *plus* ciphertext, which is at least 16 bytes and never reaches
the guard. A short-but-nonempty file that does contain a complete IV still fails padding validation
exactly as before. The IV read-loop in the streaming file path (a genuine fix, not on the settings
load path) is untouched.

### 3. `EndOfStreamException` escaped `catch (JsonSettingsException)`

`JsonSettingsException` is the one type a caller is told to catch around a load.
`EndOfStreamException` derives from `IOException`, so on 2.1.0 a file shorter than an IV escaped it.
Resolved by the same change as #2 — those inputs now produce a `JsonSettingsException` again (either
`"The settings file is empty!"` without a recovery module, or a recovered load with one).

### 4. Newtonsoft.Json 13 capped nesting at 64

Not this library's own code originally. Json.NET 13.0.1 changed the default `MaxDepth` from null
(unlimited) to 64, and 2.1.0 upgraded Newtonsoft.Json 12.0.3 → 13.0.3.
`SerializationSettings` never set `MaxDepth`, so it silently took the dependency's default.

| node-chain depth | 2.0.x | 2.1.0 | this build |
|---|---|---|---|
| 30, 60, 63 | ok | ok | ok |
| 64, 65, 70 | ok | `JsonSettingsException` | **ok** |
| 200, deeper | ok | `JsonSettingsException` | `JsonSettingsException` (bounded) |

The asymmetry is what made it easy to miss: **saving a deep object still works** — an application
can run its whole lifetime writing a file it will fail to read on next start.

**Fix, and why it is a bound rather than a straight revert to null.** The default is set explicitly
to **128**. Restoring `null` would have brought back the exact 2.0.x behaviour, but this reader is
recursive and — with `TypeNameHandling.Auto` on — stack-hungry: measured through the real load path
it exhausts the thread's stack at roughly **0.42 levels per KB**:

| thread stack | reader overflows at depth ≈ |
|---|---|
| 256 KB | 110 |
| 512 KB | 230 |
| 1 MB | 430 |

A depth limit converts that *uncatchable* `StackOverflowException` into a catchable
`JsonSettingsException` only if it fires **below** the overflow depth. That is why 128 and not 512:
512 sits above the overflow on every ordinary stack, so it would never run — it would be neither a
bound nor honestly unlimited. 128 clears any realistic settings graph (they are rarely more than a
dozen deep; note a `List<object>` under `TypeNameHandling.Auto` costs ~2 counted levels, so 128 is
~64 nested collections) yet still fires first on a 512 KB-or-larger thread.

A consumer who genuinely needs deeper — or wants literal 2.0.x behaviour — sets it back:

```csharp
JsonSettings.SerializationSettings.MaxDepth = null;   // unlimited, as 2.0.x
```

and one hardening against hostile input on a small stack can lower it further.

### Related, and kept on purpose

The ~0.4% of wrong-password loads whose output survives PKCS7 padding validation used to reach the
JSON parser and surface as `Unable to parse file`. Since 2.1.0 they throw immediately with the
correct "password" message, and this build keeps that. If `WithEncryption` is combined with
`WithRecovery`, that branch previously reset settings to defaults on a mistyped password; it now
reports the bad password. Better — and retained.

## The regression suite

`tests/JsonSettings.Tests/Upgrade/` — **46 tests, green on all five target frameworks**
(`net472`, `net48`, `net6.0`, `net8.0`, `net10.0`).

| file | covers |
|---|---|
| `BaselineModules.cs` | the gzip / xor / hex test modules and the shared settings shape |
| `ModuleChainingTests.cs` | cause 1, including two fixtures written by 2.0.1 |
| `DamagedEncryptedFileTests.cs` | causes 2 and 3 |
| `SerializationDepthTests.cs` | cause 4 |

**How to read them.** Three of the files assert the 2.0.x baseline verbatim, and they went from red
against 2.1.0 to green against this build — that is the restoration, checked rather than asserted.
`SerializationDepthTests` is the exception: it asserts the *chosen* policy (a 128 bound, unlimited
via `null`), not 2.0.x's literal `null`, and its remark says so. Its deep-graph rows run on a
thread with a large fixed stack so they test the library's limit logic, not the test runner's small
thread stack (see the note at the top of that file).

**A red test here is a behaviour change, not a broken test.** If a future change turns one red,
that is a decision to make deliberately and note in the release notes, not to paper over.

The 87 pre-existing tests are unaffected and green on all five frameworks.

## Reproducing

```bash
# the regression suite, one framework
dotnet test tests/JsonSettings.Tests/JsonSettings.Tests.csproj -f net8.0 --filter "FullyQualifiedName~Upgrade"

# everything else, which must stay green
dotnet test tests/JsonSettings.Tests/JsonSettings.Tests.csproj --filter "FullyQualifiedName!~Upgrade"
```

To re-measure rather than trust this document: compile the same scenario bodies against
`Nucs.JsonSettings` 2.0.1 from nuget.org and against a project reference to this build, and diff the
two. Every table above came from doing exactly that. The stack-vs-depth figures come from loading
ever-deeper files on threads of a fixed stack size and recording the last depth read before the
process fell over.
