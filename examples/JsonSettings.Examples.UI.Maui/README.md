# JsonSettings.Examples.UI.Maui

A small .NET MAUI app that stores its state with **Nucs.JsonSettings**. Sibling to the WPF
[`JsonSettings.Examples.UI`](../JsonSettings.Examples.UI), which tours every notification
integration; this one shows the smallest possible persistent MAUI app — where the settings class
**is** the page's `BindingContext`.

The whole demo is one settings class, [`Settings/DemoSettings.cs`](Settings/DemoSettings.cs):

```csharp
[Autosave, NotifyChangesMixin]
public sealed class DemoSettings : JsonSettings {
    public override string FileName { get; set; } = "jsonsettings-maui-demo.json";

    public int LaunchCount { get; set; }

    [NotifyChangesFor(nameof(ClickLabel))]
    public int ClickCount { get; set; }
    public string ClickLabel => ClickCount == 1 ? "Clicked 1 time" : $"Clicked {ClickCount} times";

    [NotifyChangesFor(nameof(NoteEchoText))]
    public string Note { get; set; } = "";
    public string NoteEchoText => ...;
}
```

Every control on the page is a plain `{Binding}` against that object. `[Autosave]` weaves each
setter to persist the whole object on assignment; `[NotifyChangesMixin]` injects
`INotifyPropertyChanged` and raises it from the same setters, so the bindings refresh with no
view model, no `OnPropertyChanged`, and no code-behind label updates. `[NotifyChangesFor]` fans a
`ClickCount`/`Note` change out to the computed button caption and echo label. The Reset button
writes two properties inside a `SuspendAutosave()` scope — the save counter in the corner goes up
by exactly one while both bindings refresh immediately (notifications are never suspended, saving
is). Close and reopen the app and every value is still there.

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
