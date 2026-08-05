# JsonSettings.Examples.UI.Maui

A small .NET MAUI app that stores its state with **Nucs.JsonSettings** and the `[Autosave]`
weaver. Sibling to the WPF [`JsonSettings.Examples.UI`](../JsonSettings.Examples.UI); this one
also serves as the pin-board for a build-time issue investigated below.

The whole demo is one settings class, [`Settings/DemoSettings.cs`](Settings/DemoSettings.cs):

```csharp
[Autosave]
public sealed class DemoSettings : JsonSettings {
    public override string FileName { get; set; } = "jsonsettings-maui-demo.json";
    public int LaunchCount { get; set; }
    public int ClickCount  { get; set; }
    public string Note      { get; set; } = "";
}
```

`MainPage` bumps `LaunchCount` on start, `ClickCount` on the button, and writes `Note` on every
keystroke. Nothing calls `Save()` — `[Autosave]` weaves each setter so the assignment persists the
whole object. Close and reopen the app and every value is still there.

## Run it

Windows only (the project targets `net10.0-windows...` — see the csproj comment for why), with the
MAUI Windows workload:

```bash
dotnet workload install maui-windows
dotnet build   examples/JsonSettings.Examples.UI.Maui -c Debug
dotnet run --project examples/JsonSettings.Examples.UI.Maui -f net10.0-windows10.0.19041.0
```

or open the folder in Visual Studio and F5. The settings file lands in the app data directory
(`FileSystem.AppDataDirectory`); the path is printed at the bottom of the page.

This project is deliberately **not** in `JsonSettings.sln`: CI restores and builds that solution
on a runner without the `maui-windows` workload, and a MAUI project fails restore outright
(`NETSDK1147`) when the workload is missing. Build the folder directly as above, or add the
project to a solution of your own locally.

Trimming is turned off (`<PublishTrimmed>false</PublishTrimmed>`): JsonSettings serializes through
reflection-based Newtonsoft.Json, and a trimmed build can drop the property setters the settings
graph needs.

## The build issue this project was made to probe (fixed in 2.2.1)

A consumer that references the **published NuGet** `Nucs.JsonSettings.Autosave` **2.2.0** from a
small executable project failed to build:

```
error MSB4018: The "CreateAppHost" task failed unexpectedly.
System.IO.IOException: The process cannot access the file '...\<App>.dll'
because it is being used by another process.
   at Microsoft.NET.HostModel.ResourceUpdater.AddResourcesFromPEImage(String peFile)
   at Microsoft.NET.HostModel.AppHost.HostWriter.CreateAppHost(...)
```

### Why

2.2.0 swapped Castle.Core (a runtime proxy) for **AspectInjector 2.9.0** (compile-time IL weaving).
AspectInjector is not `PrivateAssets`, so its targets flow into every consumer and weave the
consumer's own assembly after compile. The weaver briefly keeps the freshly-woven intermediate
assembly open. For an executable, the SDK then runs `CreateAppHost`, which copies the app's Win32
resources (icon, version) **out of that same managed DLL** into the native apphost `.exe` — and if
the weaver's handle is still open, that read throws.

It is timing-sensitive, and build weight decides the winner:

| Consumer | Reference | Build weight | Result |
|---|---|---|---|
| console `Exe`, one project | NuGet | tiny/fast | **fails** (deterministic) |
| WinUI `WinExe` (the `App11` repro) | NuGet | small/fast | **fails** (deterministic) |
| **this MAUI app** | NuGet **or** project | heavy | builds |
| WPF `JsonSettings.Examples.UI` | project | heavy | builds |

A fast build reaches `CreateAppHost` before the weaver's handle is released; a heavy build (MAUI, or
any multi-project `ProjectReference` build like the examples here) takes long enough that the handle
is gone first. It is **not** framework-specific — the console repro fails identically on `net8.0`,
`net10.0` and `net10.0-windows`.

That is also why this example never reproduced the failure while the WinUI `App11` repro did:
MAUI's Windows build is simply too heavy to lose the race. The reference switch used for the
probe is still in the csproj:

```bash
# default: references the in-repo projects
dotnet build examples/JsonSettings.Examples.UI.Maui

# consume the published packages instead (2.2.0 until 2.2.1 is on nuget.org)
dotnet build examples/JsonSettings.Examples.UI.Maui -p:UseNugetPackages=true
```

Both build clean today — and no longer only because MAUI is heavy: built from inside this
repository, `Directory.Build.targets` imports the 2.2.1 out-of-process weave targets into every
project, this one included, which shields even the still-broken 2.2.0 packages.

### Fixed in 2.2.1

The probe did its job: 2.2.1's packaged build targets suppress AspectInjector's in-process task and
run the identical weaver task in a short-lived **child MSBuild process**, so every handle the weaver
leaks is closed by the OS before `CreateAppHost` runs. The failing console/WinUI repros build 3/3
clean against 2.2.1 with weaving fully active. Full forensics and the fix's design live in
`docs/aspectinjector-2.9.0-apphost-lock.md`; opt back into the old in-process weave (and the bug)
with `<NucsJsonSettingsOutOfProcWeave>false</NucsJsonSettingsOutOfProcWeave>`.

Historical workarounds that were verified against 2.2.0, kept for reference — both build the repro
but neither is acceptable (see the forensics doc): `<AspectInjector_Enabled>false</AspectInjector_Enabled>`
(silently disables `[Autosave]` weaving) and `<UseAppHost>false</UseAppHost>` (no native apphost;
not an option for WinUI/MAUI).
