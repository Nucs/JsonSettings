# QA: consuming the packages from inside this repository

How to verify the shipped packages end-to-end — pack locally, point the example projects (or any
in-tree consumer) at the nupkgs instead of their `ProjectReference`s, and run them. Used for the
2.3.0 pre-release QA; kept because one step is non-obvious and cost a debugging session to find.

## Procedure

1. **Pack with a unique version** so a stale copy in the global package cache
   (`%USERPROFILE%\.nuget\packages`) from an earlier QA round can never be resolved instead of
   today's build:

   ```bash
   dotnet pack src/JsonSettings/JsonSettings.csproj                     -c Release -p:Version=2.3.0-qa.1 -o <feed-dir>
   dotnet pack src/JsonSettings.Autosave/JsonSettings.Autosave.csproj   -c Release -p:Version=2.3.0-qa.1 -o <feed-dir>
   dotnet pack src/JsonSettings.NotifyChanges/JsonSettings.NotifyChanges.csproj -c Release -p:Version=2.3.0-qa.1 -o <feed-dir>
   ```

2. **Swap the consumer's `ProjectReference`s for `PackageReference`s** at that version.

3. **Build with the feed added and the repo's weave imports disabled** (see below):

   ```bash
   dotnet build <consumer> -t:Rebuild \
     -p:RestoreAdditionalProjectSources=<feed-dir> \
     -p:NucsQaPackageConsumption=true
   ```

4. Verify the weave actually came from the package: run the consumer (the
   `EnableAutosave()` weave-marker validation throws if it did not), or reflect over the output —
   every `[Autosave]` type must implement `IAutosaveWoven`, and
   `Nucs.JsonSettings.dll` in the output folder must carry the QA version in its
   `ProductVersion`.

5. Revert the consumer csprojs when done. For consumers *outside* the repository tree (the
   cleanest simulation), steps 2–3 need no `NucsQaPackageConsumption` and a `NuGet.config`
   with the feed replaces `RestoreAdditionalProjectSources`.

## Why `NucsQaPackageConsumption=true` exists

`Directory.Build.targets` imports `src/JsonSettings.Autosave/build/*.targets` (and the
NotifyChanges mirror) into **every** project in the tree, so the in-tree, ProjectReference-based
consumers get woven without referencing the packages. When an in-tree project consumes the
*package* instead, **both** copies of the targets are imported — the package's
(via `obj/*.nuget.g.targets`) and the repository's (via `Directory.Build.targets`, which MSBuild
imports later).

The two copies deduplicate the weave through `AspectInjector_Enabled`: whichever evaluates first
claims mode `outofproc` and flips the property, and the second reads `false`, concludes another
copy owns the weave, and goes dormant. That property-level chain is correct — but the dormant
copy also **redefines the `NucsAutosave_WeaveOutOfProc` target under the same name**, and in
MSBuild the last definition wins. Result: the dormant repository copy silently replaces the
package's active target, no weave runs, and the consumer fails at runtime with the
"marked [Autosave] but was never IL-woven" validation (working as designed — that validation is
exactly what surfaced this).

`NucsQaPackageConsumption=true` skips the two repository-side imports so the package's targets
are the only definition, which is precisely a real consumer's configuration. It is inert unless
passed explicitly.

## 2.3.0 outside-tree launch QA: App11 (WinUI 3)

`D:\App11` — the original 2.2.0 CreateAppHost-failure repro, a WinUI 3 `net8.0-windows10.0.19041.0`
WinExe — consumes the packed packages from this repo's `artifacts/nuget` folder through its own
`NuGet.config` (project-local `globalPackagesFolder`, machine cache as read-only fallback; no
`NucsQaPackageConsumption` needed since it sits outside the tree). The nupkgs are packed into
`artifacts/nuget` (gitignored) rather than a temp feed *on purpose*: earlier rounds pointed App11
at a per-session temp directory that later evaporated, breaking its restore.

Verified 2026-08-06 against `Nucs.JsonSettings` + `Nucs.JsonSettings.Autosave` **2.3.0**
(`dotnet pack -c Release -p:Version=2.3.0 -o artifacts/nuget`, all three packages):

- Default (MSIX-tooling) `-p:Platform=x64 -t:Rebuild` build is green — the exact configuration
  the 2.2.0 in-process weave deterministically killed with the CreateAppHost file lock.
- Deployed `Nucs.JsonSettings.dll` carries `ProductVersion 2.3.0+d6e8e60` — that sha is the
  master merge of `release/2.3.0` (PR #52), whose tree is identical to the branch tip the pack
  ran from, so the embedded provenance differing from HEAD is cosmetic, not stale binaries.
- Unpackaged variant (`-p:WindowsPackageType=None -p:BaseOutputPath=bin/unpackaged/`, installed
  Windows App Runtime 2.3.1) launches: window up and `Responding`, and
  `Load<SettingsBag>(path).EnableAutosave()` writes `%LOCALAPPDATA%\App11\settings.json` on the
  first indexer assignment (`Launches: 1`) with the `SafeDictionary` `$type` payload intact.
- Graceful close (`CloseMainWindow`) → relaunch loads the persisted file and increments to
  `Launches: 2` — the full load → populate → dictionary-autosave roundtrip on packaged bits.

### Follow-up round: the IL-woven `[Autosave]` path (2026-08-07)

The round above only exercised SettingsBag's dictionary autosave — no weaving. App11 now also
declares `WovenSettings`, an `[Autosave]` `JsonSettings` subclass, so the package's build
targets weave the WinExe's own `App11.dll`: the weaver has *real work* in the exact
configuration whose no-op weave leaked the handle that killed CreateAppHost under 2.2.0.
Verified at HEAD `9fb859d` (doc-only commit; `src/` tree identical to the tree the 2.3.0
nupkgs were packed from, so no repack was warranted):

- Both builds green with weaving active: default MSIX-tooling x64 rebuild and the
  `-p:WindowsPackageType=None` launch variant.
- Binary proof: `MetadataLoadContext` over the built `App11.dll` shows `WovenSettings :
  ISavable, IDisposable, IAutosaveWoven` and a `Nucs.JsonSettings 2.3.0.0` reference — the
  marker interface only the weave injects.
- Runtime proof: `Load<WovenSettings>(...).EnableAutosave()` passed its weave-marker
  validation (it throws on an unwoven `[Autosave]` type), `WovenLaunches++` hit the woven
  setter and wrote `woven.json` on assignment, and a close → relaunch roundtrip incremented
  it 1 → 2 while the SettingsBag file tracked 3 → 4 in parallel.
