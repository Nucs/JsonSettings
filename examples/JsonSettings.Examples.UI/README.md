# JsonSettings.Examples.UI

A WPF application in which **every control is a bound settings property** — one runnable window
covering the integrations of `Nucs.JsonSettings.NotifyChanges` composed with
`Nucs.JsonSettings.Autosave`. There is no hand-written `OnPropertyChanged()` anywhere in the
project: every notification in its activity log was raised by the compile-time-woven setters.

```sh
dotnet run --project examples/JsonSettings.Examples.UI -f net8.0-windows
```

The window itself is the first demo: its `Left`/`Top`/`Width`/`Height`/`Title` are TwoWay-bound to
a `[Autosave, NotifyChanges]` settings object, so moving or resizing the window is the write that
persists it — close the app and reopen it, and it comes back exactly where you left it.

A shared bottom pane shows an **activity log** (each `PropertyChanging`/`PropertyChanged`/save,
stamped with the thread it arrived on) and a live **preview of the JSON file on disk**, so every
claim a tab makes is observable.

| Tab | Integration | What to watch |
|---|---|---|
| Window & autosave | `[Autosave, NotifyChanges]` on `NotifiyingJsonSettings`; `SuspendAutosave()` | Drag the window: the save counter climbs per write. Hold the suspend toggle while dragging: the burst commits as **one** save, while bindings stay live (suspension is save-only). |
| Guards | `NotificationGuard` — `OnlyChanged` (default), `Always`, `OnlyChanged \| SkipNullOrDefault` | Autosave has no guard: "write same value" saves (+1) but notifies (+0). "Clear to null" under `SkipNullOrDefault` saves `null` to disk while the binding deliberately keeps its last value. |
| Computed & opt-outs | `[NotifyChangesFor]`, `[IgnoreNotify]`, `[IgnoreAutosave]`, `INotifyPropertyChanging` | One keystroke raises the source then `FullName` (fan-out order in the log). `LastSavedBy` saves but its binding never refreshes; `SearchText` notifies but never *triggers* a save. The log shows the old value on the `PropertyChanging` edge. |
| Collections & bursts | Nested `ObservableCollection` autosave via `NotificationBinder`; `SuspendAutosave()` | `Add` saves (nested `CollectionChanged`); **replacing** the collection saves and rebinds, so later adds keep saving. "Add 20 books in ONE save" coalesces a burst. |
| No base & conventions | `[NotifyChangesMixin]` on a `sealed` class; a raiser-convention base (`RaisePropertyChanged`, notify-only); `EnableIAutosave` | `(object)settings is INotifyPropertyChanged` is `true` only because the mixin injected it. The convention class binds live but only writes the file on an explicit `Save()`. `LaunchCount` increments through the `IAppSettings` interface on every start. |
| Threading | `EnableNotificationMarshaling()` / `DisableNotificationMarshaling()` | Run the background job with marshalling on, then off, and compare the thread stamps in the log. `PropertyChanging` is never marshalled — its lines always show the worker thread. |

The demo also exercises what the docs call out as boundaries, on purpose: the `[IgnoreAutosave]`
tab shows the value still riding along in a save triggered elsewhere (the attribute controls
*triggering*, not serialization), and the Threading tab's note explains what WPF forgives on its
own versus what marshalling is actually for.

Full guide: [Notifications & Data Binding](../../docs/website-src/docs/notifications.md).
