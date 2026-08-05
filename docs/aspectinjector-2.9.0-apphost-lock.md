# Bug: AspectInjector 2.9.0 leaves the woven assembly locked → `CreateAppHost` fails for executable consumers

Status: **FIXED in 2.2.1 by a package-side out-of-process weave** (see [The fix shipped in
2.2.1](#the-fix-shipped-in-221) below). The upstream handle leak is still present in
AspectInjector 2.9.0 — the exact undisposed objects are pinpointed in
[Root cause](#root-cause), which is written to be liftable into an upstream issue.
Filed from: `Nucs.JsonSettings.Autosave` 2.2.0. Root cause is upstream in AspectInjector 2.9.0.

---

## TL;DR (eli5)

`Nucs.JsonSettings.Autosave` 2.2.0 uses **AspectInjector** to rewrite ("weave") the consumer's
compiled `.dll` after build so `[Autosave]` setters save themselves. After the weaver rewrites the
`.dll`, it keeps that file open for a moment. For an app that produces a native `.exe`, the very
next SDK step (`CreateAppHost`) copies the app's icon/version **out of that same `.dll`** into the
`.exe` — and that read hits the still‑open file and aborts the build:

```
error MSB4018: The "CreateAppHost" task failed unexpectedly.
System.IO.IOException: The process cannot access the file '...\<App>.dll'
because it is being used by another process.
```

So a project **breaks just by referencing the package** — it never has to call a single JsonSettings
API. It bites *fast* app builds (a one‑project console app, a WinUI app) because they reach
`CreateAppHost` before the weaver's handle is released; it does not bite *heavy* builds (a full .NET
MAUI app) because those run long enough that the handle is already gone.

**The fix cannot be "turn the weaver off"** — that is the whole feature, and it also silently breaks
`[Autosave]` (the class still compiles, its setters just never save). The fix has to make the weaver
release the file before `CreateAppHost` reads it.

---

## Impact

- Any **executable** (`OutputType=Exe`/`WinExe`) consumer with a **small/fast build** that references
  `Nucs.JsonSettings.Autosave` 2.2.0 from **NuGet** fails to build. Reproduced on:
  - a one‑file console app (below), and
  - a stock WinUI 3 app (the `App11` repro that started this investigation).
- The consumer does **not** need to use any JsonSettings type — the mere package reference pulls in
  the transitive AspectInjector weave.
- Library consumers are unaffected (no apphost is produced). This is why the JsonSettings solution's
  own projects and tests are green and the problem only shows up in a downstream *app*.

## Environment

| | |
|---|---|
| SDK | .NET SDK **10.0.101** (`Microsoft.NET.Sdk.targets`, `_CreateAppHost` target) |
| Package | `Nucs.JsonSettings.Autosave` **2.2.0** → transitively `AspectInjector` **2.9.0** |
| OS | Windows 11 |
| Weaver version | 2.9.0 is the **newest** on nuget.org. 2.8.x is **not** an option: it fails `dotnet build` with `MSB4803: GetFrameworkSdkPath is not supported on the .NET Core version of MSBuild`. |

`Nucs.JsonSettings.Autosave` 2.2.0 replaced Castle.Core (a **runtime** proxy — no build‑time weaver,
so pre‑2.2.0 consumers never hit this) with AspectInjector (a **compile‑time** weaver).

## Minimal reproduction

`Repro.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Nucs.JsonSettings" Version="2.2.0" />
    <PackageReference Include="Nucs.JsonSettings.Autosave" Version="2.2.0" />
  </ItemGroup>
</Project>
```

`Program.cs` can be empty (`// nothing`). Then:

```bash
dotnet build -c Debug
# -> error MSB4018: The "CreateAppHost" task failed unexpectedly.
#    System.IO.IOException: ... 'obj\Debug\net8.0\Repro.dll' because it is being used by another process.
#       at Microsoft.NET.HostModel.ResourceUpdater.AddResourcesFromPEImage(String peFile)
#       at Microsoft.NET.HostModel.AppHost.HostWriter.CreateAppHost(...)
```

Fails on **3/3** clean builds.

## What triggers it and what doesn't (evidence)

| Consumer | Reference kind | Build weight | Result |
|---|---|---|---|
| console `Exe`, one project | NuGet PackageReference | tiny / fast | **fails (deterministic)** |
| WinUI 3 `WinExe` (`App11`) | NuGet PackageReference | small / fast | **fails (deterministic)** |
| .NET MAUI app (Windows head) | NuGet **or** ProjectReference | heavy | builds |
| WPF example (`JsonSettings.Examples.UI`) | ProjectReference | heavy | builds |

Controlled findings:

- **It is the weaver.** `-p:AspectInjector_Enabled=false` builds; removing only the Autosave
  reference builds; the base `Nucs.JsonSettings` package alone builds. Adding the weave is the only
  change that breaks it.
- **It is the apphost step.** `-p:UseAppHost=false` builds (no native `.exe`, so no `CreateAppHost`).
- **Not framework-specific.** The console repro fails identically on `net8.0`, `net10.0`, and
  `net10.0-windows`.
- **Not build-server / node reuse.** Still fails with `--disable-build-servers` **and**
  `MSBUILDDISABLENODEREUSE=1`. The handle is held for the duration of the single build invocation.
- **Timing / build weight decides it.** In a heavy build (MAUI) the weave sits hundreds of MSBuild
  steps before `CreateAppHost`, the handle is released in between, and the apphost is stamped
  normally. In a one‑project console/WinUI build the two steps are adjacent and the read loses.
  `Microsoft.NET.HostModel.RetryUtil.RetryOnIOError` retries for ~a second and still loses, so the
  handle outlives the SDK's own retry window on a fast build.
- Live corroboration: a **reused MSBuild worker node** (`/nodemode:1 /nodeReuse:true`) that had run
  the in‑proc weave was still holding the woven assembly's `.dll` mapped minutes later, blocking
  unrelated rebuilds of that assembly.

## Root cause

`AspectInjector.targets` registers an **in‑process** MSBuild task that runs
`AfterTargets="CoreCompile"` and rewrites `@(IntermediateAssembly)` (`obj\…\<App>.dll`) with
Mono.Cecil:

```xml
<UsingTask TaskName="AspectInjectorTask" AssemblyFile=".../AspectInjector.dll" />
<Target Name="AspectInjector_InjectAspects" AfterTargets="CoreCompile" ...>
  <AspectInjectorTask AssemblyPath="@(IntermediateAssembly)" ... />
</Target>
```

Decompiling the 2.9.0 binaries (`AspectInjector.dll` → `AspectInjector.Core.dll` → `FluentIL.dll`
→ `Mono.Cecil.dll`) pinpoints **two distinct leaks** in `FluentIL.PatcherBase.Process`, the method
`AspectInjector.Compiler.Execute` drives:

1. **The target assembly leaks a WRITE-capable handle whenever there is nothing to weave.**
   `PatcherBase.ReadAssembly` opens the assembly with Cecil `ReaderParameters { ReadWrite = true }`,
   which is `new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)`
   (`Mono.Cecil.ModuleDefinition.GetFileStream`). The **only** call to `assembly.Dispose()` sits
   inside `WriteAssembly`, and `WriteAssembly` only runs when `PatchAssembly` returns *true* —
   i.e. when the assembly contained at least one aspect or injection. An assembly with **no**
   aspects — exactly what a consumer gets by referencing the package without declaring an
   `[Autosave]` class yet — takes the `"No patching required."` path, and the ReadWrite
   `FileStream` on `obj\…\<App>.dll` is abandoned to the GC of the long-lived MSBuild node.
   A `FileShare.Read`-only, write-access handle denies every subsequent opener that needs write
   access **or** doesn't share write — which is why both `CreateAppHost`
   (`ResourceUpdater.AddResourcesFromPEImage` → `FileAccess.Read/FileShare.Read` open) and the
   next `csc`/copy that wants to *write* the file lose.

2. **Every reference assembly leaks a read handle, in every path.** `PatcherBase.GetResolver`
   creates a `KnownReferencesAssemblyResolver` (a `CachedAssemblyResolver` holding a
   `Dictionary<string, AssemblyDefinition>` of **open** modules, one per resolved reference) and
   nothing ever disposes it. Those `FileShare.Read` read handles block *writers* of the referenced
   dlls — this is the "reused MSBuild worker node still holding dlls **minutes later**, blocking
   unrelated rebuilds" observed live (reproduced in this repository: rebuilding
   `Nucs.JsonSettings.Autosave` fails its bin-copy while a stale node holds the previous build's
   output that some *other* project's weave resolved as a reference).

Why the in-build `GC.Collect()` mitigation (below) cannot be trusted even in principle:
`FluentIL.CutEvents.OnModify` is a **static** delegate property that `Processor.PatchAssembly`
overwrites per run with a closure over that run's state — the previous weave's object graph can
stay strongly rooted until the *next* weave replaces the delegate, and a rooted `FileStream` is
immune to any amount of forced collection.

The SDK's `_CreateAppHost` target then runs `ResourceUpdater.AddResourcesFromPEImage(<App>.dll)` to
copy Win32 resources into the native apphost and throws `IOException` because the file is still
open. Note: the Win32 resources the apphost copies (version, icon, manifest) are **identical before
and after weaving** — weaving changes IL, not resources. So nothing about the *content* is wrong;
the failure is purely the **file lock**. And in the common repro (no aspects declared) the weaver
did not even modify the file — the build is broken by the lock of a **no-op**.

## Why the obvious consumer workarounds are not acceptable

| Workaround | Builds? | Why it's not the answer |
|---|---|---|
| `<AspectInjector_Enabled>false</AspectInjector_Enabled>` | ✅ | Disables weaving. Any `[Autosave]` class then **compiles but never saves** — silent data loss. Only safe if the app declares no `[Autosave]` types at all. |
| `<UseAppHost>false</UseAppHost>` | ✅ | No native `.exe`. **Not possible for WinUI/MAUI**, and undesirable for most apps. |
| Use `SettingsBag` instead of a woven `[Autosave]` class | ✅ | Sidesteps weaving (SettingsBag autosave is dictionary‑backed), but gives up the headline `[Autosave]` feature. |

The requirement is: **keep AspectInjector weaving on and still build an apphost.** None of the above
satisfy it.

## In-build mitigations that were tried and did NOT work

- Forcing `GC.Collect()` + `GC.WaitForPendingFinalizers()` from an inline MSBuild task, both
  `AfterTargets="AspectInjector_InjectAspects"` and `BeforeTargets="_CreateAppHost"`. Still locked —
  the handle is not reclaimed by a GC issued from within the same build (the weaver task instance is
  still rooted by MSBuild at that point).

## The fix shipped in 2.2.1

Of the candidate directions, **run the weave in a separate process** is the one that holds up, and
it is what `Nucs.JsonSettings.Autosave` and `Nucs.JsonSettings.NotifyChanges` 2.2.1 ship. The other
two candidates die on the facts established above: the atomic swap cannot work because the leaked
handle is `FileShare.Read` **without** `FILE_SHARE_DELETE`, so the locked file can be neither
renamed nor deleted (the swap has nowhere to put the fresh copy); and "block until the handle is
free" has no bound — a rooted handle in a reused node outlives any reasonable wait (and the whole
build with it).

Mechanism, implemented entirely in the shipped `build/`+`buildTransitive/` targets (no new
binaries):

1. The packaged `.targets` suppress AspectInjector's in‑process target by setting
   `AspectInjector_Enabled=false`, exactly the switch AspectInjector documents.
2. A replacement target (`NucsAutosave_WeaveOutOfProc` / `NucsNotifyChanges_WeaveOutOfProc`) runs
   at the same anchors (`AfterTargets="CoreCompile"`, before `AfterCompile` and the re‑sign) with
   the same incrementality (the `.aspectsinjected` stamp), writes `@(ReferencePath)` to a response
   file (reference lists overflow the ~32K command line), and `Exec`s a **child MSBuild**
   (`dotnet exec .../MSBuild.dll` under .NET MSBuild, `MSBuild.exe` under VS) on a tiny shipped
   `<PackageId>.Weave.proj` that `UsingTask`s the *same* `AspectInjectorTask` from the AspectInjector
   package (located via `AspectInjector_Location`, else derived from the package's own
   `@(Analyzer)` item, else the NuGet cache) and invokes it with the *same parameters* the stock
   target passes.
3. The child exits; the OS closes **every** handle the weaver leaked — target, references,
   whatever is rooted — before `_CreateAppHost` ever runs. Weaving semantics are unchanged
   (the task processes the whole assembly, so aspects from any package are woven as before).
4. A consumer referencing **both** packages weaves once: whichever targets file imports first
   claims the weave (mode `outofproc`) and flips `AspectInjector_Enabled`; the second reads
   `false`, concludes `off`, and stays dormant — and even if both ran, the shared stamp makes the
   second a no‑op.

Knobs: `<NucsJsonSettingsOutOfProcWeave>false</NucsJsonSettingsOutOfProcWeave>` restores the stock
in‑process weave (one property, honoured by both packages); `AspectInjector_Enabled=false` still
disables weaving outright; `AspectInjector_Location` is honoured. New diagnostics NJS1005/NJS1006
(error) fire if the task dll cannot be located, with remedies in the message. Cost: one short-lived
MSBuild process per weave-needing build (~1–2 s), skipped entirely when the assembly is up to date
and in design-time builds.

Validated (2.2.1 packages from a local feed, .NET SDK 10.0.101):

| Scenario | Result |
|---|---|
| the minimal repro above, 3× clean `dotnet build` | **passes 3/3** (was fails 3/3) |
| `[Autosave]` app, net8.0 and net48: one setter write | exactly 1 save, value persisted |
| same app after 2 incremental rebuilds | still exactly 1 save (no double weave) |
| app referencing BOTH weaving packages | 1 save + 1 `PropertyChanged` per write |
| `-p:NucsJsonSettingsOutOfProcWeave=false` | original `MSB4018`/`IOException` returns — the mechanism is causal |
| repository test suite (dogfoods the same targets) | 471/471 green on net6/8/10, 466/466 on net472/net48 |

## Upstream

The correct fix remains upstream: `FluentIL.PatcherBase.Process` must dispose the
`AssemblyDefinition` it opened with `ReadWrite=true` on the no‑patch path (today only
`WriteAssembly` disposes it), dispose the `KnownReferencesAssemblyResolver`'s cached modules in
every path, and stop parking state in the static `CutEvents.OnModify`. See related history:
pamidur/aspect-injector #141 (".pdb … used by another process"), #239 ("cannot re‑sign assembly,
try rebuild"). Until then the 2.2.1 out‑of‑process weave sidesteps the entire class of leaks.

## References

- Symptom target: `Microsoft.NET.Sdk.targets` → `_CreateAppHost` → `CreateAppHost` task →
  `Microsoft.NET.HostModel.ResourceUpdater.AddResourcesFromPEImage`.
- Weaver: `AspectInjector` 2.9.0, `build/`+`buildTransitive/AspectInjector.targets`,
  in‑proc `AspectInjectorTask`.
- 2.9.0 release note: "Breaking change: resigning assemblies feature is retired as it no longer
  supported by MS" (why the weaver no longer re-signs, and why 2.8.x is unusable under `dotnet build`).
