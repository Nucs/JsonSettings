# Native AOT and Trimming

**Status as of 2.2.0: neither package is trim-safe. The hard AOT blocker in Autosave is gone.**

| Package | `PublishAot=true` | `PublishTrimmed=true` |
|---|---|---|
| `Nucs.JsonSettings` | Broken by default. Works if the consumer preserves metadata. Fixable in this repo. | Same. |
| `Nucs.JsonSettings.Autosave` | No longer blocked by its own proxying. Inherits the core package's metadata problem. | Works once metadata is preserved. |

Neither project sets `<IsAotCompatible>`, and that is still correct — Newtonsoft.Json's
unannotated reflection means neither package can keep that guarantee yet.

This document records what was measured, why it fails, and what fixing it would cost, so the
question does not have to be re-litigated from scratch.

> **What changed in 2.2.0.** Blocker 1 below — `Castle.DynamicProxy` and its hard dependency
> on `System.Reflection.Emit` — was removed by replacing DynamicProxy with compile-time IL
> weaving. The section is kept rather than deleted because the reasoning is what justifies
> the replacement, and because the same trap catches every other runtime-proxy library.
>
> **Scope of that claim.** What is verified is that the mechanism is gone: the shipped
> `JsonSettings.Autosave` assembly no longer references `Castle.*` at all (asserted by
> `AutosaveWeavingSupportTests.AutosaveAssembly_DoesNotReferenceCastle`), the advice is
> injected into consumer setters at build time, and autosave works end to end through a real
> `.nupkg` in a separate consuming project. What has **not** been re-run is the 15-probe
> `PublishAot` harness below; rows 1-3 of the results table therefore still describe 2.1.0.
> Blockers 2 and 3 are untouched by this change, so the `{ "Section": {} }` failure below is
> still live.

---

## Contents

- [What was measured](#what-was-measured)
- [Results](#results)
- [Blocker 1 — Castle DynamicProxy is Reflection.Emit](#blocker-1--castle-dynamicproxy-is-reflectionemit)
- [Blocker 2 — Newtonsoft.Json is unannotated](#blocker-2--newtonsoftjson-is-unannotated)
- [Blocker 3 — this library's own reflection](#blocker-3--this-librarys-own-reflection)
- [The failure mode that matters most](#the-failure-mode-that-matters-most)
- [What a consumer can do today](#what-a-consumer-can-do-today)
- [What supporting AOT would cost](#what-supporting-aot-would-cost)
- [Reproducing](#reproducing)

---

## What was measured

A 15-probe console harness exercising the documented feature surface — plain round-trip,
nested graphs, polymorphism via `TypeNameHandling.Auto`, `WithBase64`, `WithEncryption`,
`WithVersioning`, `WithRecovery`, `SettingsBag`, `AsDynamic()`, all three `EnableAutosave`
entry points — published in five configurations and executed.

- .NET SDK 10.0.101, `Microsoft.DotNet.ILCompiler` 10.0.1, `win-x64`, Windows 11
- Probe app targets `net10.0`, `ProjectReference` to both `src/` projects
- **Control:** the same harness on ordinary JIT passes **15/15**. Every failure below is
  caused by publishing, not by the harness.

Preservation is expressed as `TrimmerRootDescriptor` XML with `preserve="all"`. "Everything
rooted" means the probe assembly, both `JsonSettings` assemblies, `Newtonsoft.Json`,
`Castle.Core`, `Microsoft.CSharp` and `System.Linq.Expressions`. That is the ceiling a
consumer can reach with no change to this library, and it discards most of the size benefit
that motivated AOT in the first place.

## Results

| # | Configuration | Passing | Output size |
|---|---|---|---|
| 0 | JIT, untrimmed (control) | **15 / 15** | — |
| 1 | **NativeAOT, out of the box** | **2 / 15** | 13.4 MB exe |
| 2 | NativeAOT, libraries rooted, consumer types not | 3 / 15 | 22.3 MB exe |
| 3 | NativeAOT, everything rooted | **12 / 15** | 29.6 MB exe |
| 4 | `PublishTrimmed` (JIT retained), out of the box | 2 / 15 | 26.4 MB |
| 5 | `PublishTrimmed` (JIT retained), everything rooted | **15 / 15** | 27.5 MB |

Four things to take from that table.

**Out of the box, everything fails.** Not the exotic features — `JsonSettings.Load<T>()` on a
POCO with two `string` properties fails:

```
ReflectiveException: Type PlainSettings does not have empty constructor (public or private)
```

That is `Activation.CreateInstance` in [`src/JsonSettings/Inline/Activation.cs`](../src/JsonSettings/Inline/Activation.cs).
The constructor exists in source; the trimmer removed it because only reflection reaches it,
and `Type.GetConstructors()` cannot tell the trimmer that.

**Row 5 is the important contrast.** Trimming alone passes 15/15 once metadata is preserved,
including all three autosave paths. NativeAOT with the *same* preservation still fails 3/15.
The difference is not metadata — it is that AOT has no runtime code generator.
**Trimming is a metadata problem and is survivable. AOT is that plus a code-generation
problem, and the code-generation half is not survivable for Autosave.**

**Row 2 shows where the gap actually lives.** Rooting the libraries and leaving consumer
types to normal trim analysis fixes almost nothing (3/15) — `SettingsBag` passes only because
it lives inside the rooted `JsonSettings` assembly. The metadata that goes missing is the
*consumer's settings class*, which this library reaches purely reflectively.

**Row 1's two passes are the fix, demonstrated.** Probes 13 and 14 are byte-identical
scenarios to probes 1 and 5, except they route through a wrapper carrying
`[DynamicallyAccessedMembers(All)]` on its generic parameter:

```csharp
public static T Load<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string file)
    where T : ISavable => JsonSettings.Load<T>(file);
```

They pass with **zero rooting** where the un-annotated originals fail. Annotating this
library's public generic entry points is not speculation; it is measured to work.

---

## Blocker 1 — Castle DynamicProxy is Reflection.Emit — RESOLVED in 2.2.0

**This section describes 2.1.0. See [the resolution](#how-blocker-1-was-resolved) below.**

All three autosave entry points failed identically, and kept failing with every assembly fully
rooted:

```
TypeInitializationException
  └─ PlatformNotSupportedException: Dynamic code generation is not supported on this platform.
```

`Castle.DynamicProxy` builds proxy types at runtime with `System.Reflection.Emit`.
NativeAOT compiles ahead of time and ships no JIT, so `AssemblyBuilder` throws by design.
`RuntimeFeature.IsDynamicCodeSupported` is `False` in the published binary.

No annotation, descriptor, feature switch or `rd.xml` changes this. **`Nucs.JsonSettings.Autosave`
cannot work under NativeAOT while DynamicProxy is the proxying mechanism.** It contributes 62
of the 181 baseline trim warnings, 11 of them `IL3050` (`RequiresDynamicCode`).

The affected API is `EnableAutosave()`, `EnableIAutosave<T,I>()` and the
`NotifiyingJsonSettings` autosave path. `SettingsBag.EnableAutosave()` was unaffected — it is
dictionary-backed, not proxy-backed, and passed under AOT.

### How Blocker 1 was resolved

`Castle.Core` was replaced with [AspectInjector](https://github.com/pamidur/aspect-injector),
which rewrites the settings class's setters during the build instead of synthesising a proxy
type at runtime. The `[Autosave]` attribute is an aspect; the woven call lands in the
assembly that declares the class, which is the consumer's.

Three properties of that approach matter here:

- **Nothing is generated at runtime**, so `RuntimeFeature.IsDynamicCodeSupported == false` is
  no longer a problem rather than a fatal one.
- **The weaving happens in the consumer's assembly.** AspectInjector ships `buildTransitive/`
  targets, so its build logic reaches a project that picks it up indirectly through this
  package. Verified by packing real `.nupkg`s into a local feed and consuming them from a
  separate project, where a class with plain non-virtual properties autosaved correctly.
- **There is no proxy, so there is no second object.** `EnableAutosave()` returns the instance
  it was given. That removes a whole failure class that had nothing to do with AOT: under
  Castle, a reference captured before the call kept pointing at an object that did not
  autosave.

It also lifted the restrictions the proxy imposed — properties no longer need to be `virtual`,
and `sealed` classes are supported. The cost is one new requirement: the class must be marked
`[Autosave]`, and doing so is now checked at runtime so a missing attribute throws instead of
silently never saving.

**Rough edge worth knowing about.** Weaving rewrites the assembly after the compiler has
signed it, and AspectInjector 2.9.0 retired its re-signing feature, so a strong-named
assembly comes out of the weave with an invalid signature (measured: clean A/B on a signed
probe, `sn -vf` reports "is valid" unwoven and "Strong name validation failed" woven). This
repository signs every assembly, so the package ships MSBuild targets that re-sign after the
weave and warn (`NJS1001`) when `sn.exe` is unavailable. Those targets also re-stamp
AspectInjector's incremental marker: re-signing moves the assembly's timestamp past the
marker, and without the re-stamp the *next* build weaves the already-woven assembly again,
stacking a second copy of the advice and saving twice per assignment. See
[SIGNING.md](SIGNING.md#weaving-re-signs-the-assembly-nucsjsonsettingsautosave) for the
consumer-facing side of this.

## Blocker 2 — Newtonsoft.Json is unannotated

Newtonsoft does **not** hard-fail under AOT. It detects the absence of dynamic code
generation and degrades to plain reflection instead of emitting delegates, which is why
probes 1–9 pass in row 3. The problem is that its reflection is invisible to the trimmer:
98 of the 181 baseline warnings originate in `Newtonsoft.Json.Utilities` (62) and
`Newtonsoft.Json.Serialization` (36), 33 of them `IL3050`. (57 `IL3050` sites is the total
across all assemblies, of which Newtonsoft is 33 and Castle 11.)

There is no annotated build, no source generator, and no `IsTrimmable` marker. Contract
resolution walks `GetProperties`/`GetFields` on types the trimmer has already decided to
strip. The only lever a consumer has is preservation, and preservation is what row 3's
29.6 MB binary costs.

One extra hazard is on the **default** path. `JsonSettings.SerializationSettings` sets
`TypeNameHandling.Auto` ([`JsonSettings.cs:65`](../src/JsonSettings/JsonSettings.cs#L65)),
so `$type` discriminators are resolved from strings at runtime. That worked when rooted, but
it means any polymorphic subtype not statically preserved is unresolvable — a trim hazard
that is on by default rather than opt-in.

## Blocker 3 — this library's own reflection

The only part in this repository's control, and the genuinely fixable one. Out of the box the
analyzer reports 5 sites; once everything is reachable it reports 13:

| Code | Site |
|---|---|
| `IL2070` ×2 | `Activation.GetAllConstructors(Type)` |
| `IL2067` | `Activation.CreateInstance(Type)` |
| `IL2067` | `Activation.CreateInstance(Type, object[])` |
| `IL2067` | `JsonSettings.Construct(Type, object[])` |
| `IL2072` | `JsonSettings.LoadDefault(object[])` |
| `IL2072` | `JsonSettings.LoadDefault(Version, object[])` |
| `IL2075` | `JsonSettingsAutosaveInterceptor..ctor(JsonSettings)` † |
| `IL2075` ×2 | `NotificationBinder..ctor(NotifiyingJsonSettings)` |
| `IL2090` | `TypeValidation<T>.ValidateAllVirtual()` † |
| `IL2090` | `VersioningModule<T>.DefaultVersionCache<T>..cctor()` |
| `IL3050` | `DynamicSettingsBag..ctor(SettingsBag)` |

† These two sites are 2.1.0-specific and no longer exist in 2.2.0.
`JsonSettingsAutosaveInterceptor` was deleted with the rest of the Castle machinery, and
`TypeValidation<T>.ValidateAllVirtual()` became the non-generic `TypeValidation.ValidateWoven`,
which reads a single attribute rather than walking properties. The reflective property walk it
performed now lives in `AutosaveRuntime.ResolveMonitoredProperties`, invoked once from
`EnableAutosave()` rather than on any write. The Autosave rows here therefore describe the
Castle build; the core-package rows are unchanged. The counts below have not been re-measured.

Note the totals move with reachability: **181 warnings out of the box, 311 fully rooted.**
A warning count from a trimmed build is a floor, never a ceiling — code the trimmer already
removed is code the analyzer never inspected. Any future baseline should be captured from a
rooted build.

---

## The failure mode that matters most

Probe 15 uses a settings class annotated exactly as the fix prescribes, holding a nested
section whose members the application never touches statically:

```csharp
public class UntouchedSection {
    public string Label  { get; set; } = "preset";
    public int    Number { get; set; } = 99;
}
```

It does not throw. It writes this to disk:

```json
{ "Section": {} }
```

**Silent, total loss of a configuration section — no exception, no warning, no partial file.**
The next `Save()` persists the emptied object, so the original values are gone.

`[DynamicallyAccessedMembers]` preserves the members of `T`. It does **not** transit into the
types of `T`'s properties, and no annotation scheme fixes that for arbitrary object graphs —
which is precisely why `System.Text.Json` ships a source generator instead. This reproduces
under plain `PublishTrimmed` as well, so it is not specific to AOT.

For a library whose whole job is persisting configuration — and which this project documents
pointing at files containing passwords — quietly writing `{}` is a worse outcome than a crash.
This is the single strongest argument for marking the reflective API `[RequiresUnreferencedCode]`
so the consumer gets a build-time warning rather than a runtime surprise.

---

## What a consumer can do today

If you must ship trimmed or AOT with 2.1.0:

1. **`Nucs.JsonSettings.Autosave` is no longer a hard blocker** as of 2.2.0 — mark your
   settings class `[Autosave]` and the interception is compiled in rather than emitted.
   `SettingsBag.EnableAutosave()` remains dictionary-backed and unaffected.
2. **Preserve your settings types and every type reachable from them.** Either annotate your
   own call sites, or add a descriptor:

   ```xml
   <!-- MyApp.csproj -->
   <ItemGroup>
     <TrimmerRootDescriptor Include="TrimmerRoots.xml" />
   </ItemGroup>
   ```
   ```xml
   <!-- TrimmerRoots.xml -->
   <linker>
     <assembly fullname="MyApp">
       <type fullname="MyApp.MySettings" preserve="all" />
       <type fullname="MyApp.MyNestedSection" preserve="all" />  <!-- every nested type, transitively -->
     </assembly>
     <assembly fullname="Newtonsoft.Json" preserve="all" />
     <assembly fullname="JsonSettings" preserve="all" />
   </linker>
   ```
3. **Verify the round-trip in the published binary, not in a JIT test run.** The
   `{ "Section": {} }` failure is invisible to any test that does not execute the trimmed
   output. A test asserting the saved file's contents is the only thing that catches it.
4. **Expect to give back most of the size win.** 13.4 MB → 29.6 MB in the probe.

## What supporting AOT would cost

**Option A — partial, honest support for the core package.** Verified to work for the flat
case; does not solve nested graphs.

- Annotate the public generic entry points with `[DynamicallyAccessedMembers(All)]`:
  `Load<T>`, `Configure<T>`, `Construct<T>`, `LoadDefault<T>`, and the `Type`-taking
  overloads plus `Activation.*` on the parameter.
- Mark the reflective surface `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` so the
  silent-`{}` case becomes a build warning instead of a runtime surprise.
- ~~Have `EnableAutosave()` throw a clear, early `PlatformNotSupportedException` naming AOT and
  DynamicProxy~~ — **done differently in 2.2.0.** DynamicProxy is gone, so `EnableAutosave()`
  no longer needs to throw under AOT; the `TypeInitializationException` from a Castle cctor it
  used to surface simply cannot happen any more.
- Set `<IsAotCompatible>true</IsAotCompatible>` on the `net8.0`+ targets only once the above
  is real, since it turns the analyzers on for the library itself.
- Consumers still hand-preserve nested model types. The `{}` case does not go away while
  Newtonsoft is the serializer.

**Option B — actual full AOT support.** Replace Newtonsoft with `System.Text.Json` source
generators. (The DynamicProxy half of this option was done in 2.2.0 with IL weaving, which
turned out to be far cheaper than the source generator sketched here and, unlike a generated
subclass, also covers non-virtual and sealed classes.) The remaining serializer work is a
rewrite of the core package and a breaking API change: `JsonSerializerSettings`, `IContractResolver`,
`TypeNameHandling` and `JsonConvert.PopulateObject` semantics are all part of the documented
public surface and all disappear. It also forfeits the `netstandard2.0` and `net48` targets
for the generated path.

Option A is a contained change. Option B is a new major version.

---

## Reproducing

The harness is not committed. To rebuild it:

1. Create a `net10.0` console app outside this repository (so `Directory.Build.props` and the
   warnings-as-errors policy do not apply), with `ProjectReference`s to
   `src/JsonSettings/JsonSettings.csproj` and `src/JsonSettings.Autosave/JsonSettings.Autosave.csproj`.
2. Set `PublishAot`, `RuntimeIdentifier=win-x64`, and — importantly —
   `<TrimmerSingleWarn>false</TrimmerSingleWarn>`, or ILC collapses all 181 warnings into one
   line per assembly and the detail above is invisible.
3. Write probes that **assert on the round-tripped values**, not merely that no exception was
   thrown. Probe 15 passes a naive smoke test.
4. `dotnet publish -c Release -r win-x64`, then **run the produced `.exe`**. Publishing
   succeeds with all 181 warnings; only execution reveals the failures.

Two environment notes for Windows:

- Put `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on `PATH`, or the ILC link
  step fails with ``'vswhere.exe' is not recognized`` before producing a binary.
- Do not put `System.Private.CoreLib` in a root descriptor — the link fails with
  `unresolved external symbol RhIsGCBridgeActive`. Delete `obj/<config>/<tfm>/<rid>/native`
  after a failed link; the stale object file survives and reproduces the error on the next
  otherwise-valid build.
- `-p:PublishTrimmed=true` on the command line flows as a global property into the referenced
  multi-targeted projects and fails their `netstandard2.0` leg with `NETSDK1124`. Set it
  inside the probe's own csproj instead.
