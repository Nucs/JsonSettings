# JsonSettings.Examples.UI.WinForms

A Windows Forms app for the library's oldest audience — line-of-business desktop software — and
the only example that builds the same app for **.NET Framework 4.8 and modern .NET**
(`net48;net10.0-windows`), so the `lib/net48` asset runs in a real Framework UI.

Its centerpiece is the **zero-code settings dialog**:

```csharp
propertyGrid.SelectedObject = AppSettings.Instance;
```

That single line is a complete, persistent settings editor: the `PropertyGrid` groups by
`[Category]`, shows `[Description]` in its help pane — and every value committed in the grid runs
a setter that `[Autosave]` wove, so the edit is already on disk. No Apply, no Save, no dialog
plumbing.

The right half of the window binds the *same* properties with a classic `BindingSource`:

```csharp
var binding = new BindingSource { DataSource = AppSettings.Instance };
nameBox.DataBindings.Add("Text", binding, nameof(AppSettings.DisplayName),
                         false, DataSourceUpdateMode.OnPropertyChanged);
```

Edit a value on either side and the other follows live. That synchronization is the
`INotifyPropertyChanged` that `[NotifyChangesMixin]` injects at build time — `AppSettings`
declares no interface and raises nothing by hand, yet `BindingSource` discovers and subscribes to
it like any hand-written observable.

Also demonstrated:

- **An encrypted vault with the same zero-code UX** — a second settings object goes through
  `Configure<VaultSettings>(path).WithEncryption("password").LoadNow().EnableAutosave()` and gets
  its own `PropertyGrid`. The grid neither knows nor cares that the file underneath is
  AES-256-CBC ciphertext; `[PasswordPropertyText]` masks the token while editing, pairing UI
  masking with at-rest encryption. The **"Show raw files on disk"** button prints both files side
  by side — the profile is readable JSON, the vault is bytes — same code path, one fluent call
  apart.
- **Batched saves** — the "Apply server settings" button writes `Server` and `Port` inside a
  `SuspendAutosave()` scope: two setters, one file save. The status-bar save counter (fed by the
  `AfterSave` event of *both* settings objects) visibly goes up by exactly one.
- **Window title following a property** — plain code subscribing to the injected interface, which
  requires a cast through `object` (`(INotifyPropertyChanged)(object)settings`) since the
  interface is not on the class in source.

Close the app, reopen — everything is where you left it. The file lives in
`%APPDATA%\JsonSettings.Examples\winforms-demo.json`; its path is shown in the status bar after
the first save (and in the grid's `FileName` row).

## Run it

```bash
dotnet run --project examples/JsonSettings.Examples.UI.WinForms -f net10.0-windows
dotnet run --project examples/JsonSettings.Examples.UI.WinForms -f net48
```

or open `JsonSettings.sln` in Visual Studio and start this project. Both frameworks share the
same settings file — run one after the other and the values carry over, which doubles as a live
demonstration of the cross-framework file compatibility the test suite pins.
