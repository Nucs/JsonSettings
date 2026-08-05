# JsonSettings.Examples.UI.Avalonia

An [Avalonia](https://avaloniaui.net/) app whose entire "view model" is one **Nucs.JsonSettings**
class. It is the cross-platform member of the example family — the same code builds and runs on
Windows, Linux and macOS with no workload — and the only one that shows `[Autosave]` and
`[NotifyChangesMixin]` working together end to end:

```csharp
[Autosave, NotifyChangesMixin]
public sealed class DemoSettings : JsonSettings {
    public override string FileName { get; set; } = "avalonia-demo.json";

    [NotifyChangesFor(nameof(Greeting))]
    public string Name { get; set; } = "";
    public string Greeting => string.IsNullOrWhiteSpace(Name) ? "Hello, stranger." : $"Hello, {Name}!";

    public bool DarkMode { get; set; }
    public int Counter { get; set; }
    public ObservableCollection<string> Tags { get; set; } = new();
    public double WindowWidth  { get; set; } = 560;
    public double WindowHeight { get; set; } = 680;
}
```

The window binds straight to this object — `DataContext = DemoSettings.Instance` — with
**compiled bindings** (`AvaloniaUseCompiledBindingsByDefault`), so every `{Binding}` is checked
against the class at build time, while change notification comes from the
`INotifyPropertyChanged` that `[NotifyChangesMixin]` weaves in after compilation. No hand-written
`OnPropertyChanged`, no view-model wrapper, and nothing calls `Save()`.

What each control demonstrates:

| Control | Feature |
|---|---|
| Name `TextBox` → `Greeting` label | two-way binding + `[NotifyChangesFor]` fanning a change out to a computed, get-only property |
| Dark-theme `ToggleSwitch` | a bound `bool` driving `RequestedThemeVariant` live (see `App.axaml.cs`) |
| "+1 from a background thread" | a woven setter written off-thread; `EnableNotificationMarshaling()` posts its `PropertyChanged` back to the UI thread |
| Tags list with Add/Remove | a nested `ObservableCollection` — `EnableAutosave()` binds its `INotifyCollectionChanged`, so `Add`/`Remove` persist with no setter involved |
| window size on close | two writes batched into one file save with `SuspendAutosave()` |
| status line | the `AfterSave` event, posted to the UI thread by hand (settings events are not marshalled) |

Close the app and reopen — name, theme, counter, tags and window size all come back.

## Run it

```bash
dotnet run --project examples/JsonSettings.Examples.UI.Avalonia
```

Works the same on Windows, Linux and macOS. The settings file lands in
`Environment.SpecialFolder.ApplicationData` → `JsonSettings.Examples/avalonia-demo.json`
(`%APPDATA%` on Windows, `~/.config` on Linux); the exact path is shown at the bottom of the
window. In a Debug build, F12 opens Avalonia DevTools.

## Notes

- The settings class does **not** implement `INotifyPropertyChanged` in source — the interface is
  injected at build time. XAML bindings discover it on the instance automatically; code has to
  cast through `object` first (`(INotifyPropertyChanged)(object)settings`), which `App.axaml.cs`
  does to follow `DarkMode` for theming.
- The strong-name re-sign the packages normally perform after weaving is switched off in the
  csproj (`NucsAutosaveResignAfterWeaving=false`) — it needs the Windows-only `sn.exe`, and an
  app exe gains nothing from a valid strong name. See the csproj comment.
