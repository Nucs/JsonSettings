# JsonSettings.Examples.UI.Maui

A small .NET MAUI app that stores its state with **Nucs.JsonSettings** and the `[Autosave]`
weaver. Sibling to the WPF [`JsonSettings.Examples.UI`](../JsonSettings.Examples.UI), which tours
the `[NotifyChanges]` data-binding integrations; this one shows the smallest possible persistent
MAUI app.

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

The instance lives behind a `Lazy` singleton so the file is first opened once MAUI is running —
`FileSystem.AppDataDirectory` is a platform service and is only meaningful after app start:

```csharp
public static DemoSettings Instance => _instance.Value;

private static DemoSettings LoadFromAppData() {
    var path = Path.Combine(FileSystem.Current.AppDataDirectory, "jsonsettings-maui-demo.json");
    return JsonSettings.Load<DemoSettings>(path).EnableAutosave();
}
```

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

This project is not part of `JsonSettings.sln` — the solution must stay restorable without the
`maui-windows` workload (CI and contributors who don't have it). Build the folder directly as
above, or add the project to a solution of your own locally.

## Notes

- **Trimming is off** (`<PublishTrimmed>false</PublishTrimmed>`): JsonSettings serializes through
  reflection-based Newtonsoft.Json, and a trimmed build can drop the property setters the settings
  graph needs.
- **Reference switch**: by default the app references the in-repo projects. Build with
  `-p:UseNugetPackages=true` to consume the published NuGet packages instead — same source, same
  weaving, only the reference kind differs.
