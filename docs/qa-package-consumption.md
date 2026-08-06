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
