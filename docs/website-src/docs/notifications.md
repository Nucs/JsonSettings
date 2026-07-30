# Notifications & WPF

Turn a settings class into an observable ViewModel. This guide covers producing
`INotifyPropertyChanged` notifications from your setters with the `[NotifyChanges]` and
`[NotifyChangesMixin]` aspects, the change guards that decide when a notification fires, the
ways a settings class can implement the interface, and how autosave reacts to nested
`INotifyPropertyChanged` / `INotifyCollectionChanged` changes. Everything here ships in the
`Nucs.JsonSettings.Autosave` package and is built on the same compile-time IL weaving as
[`[Autosave]`](autosave.md) &mdash; no runtime proxy, Native-AOT-safe.

## Two directions

"Notifications" join a settings object to WPF in two opposite directions, and it helps to keep
them apart:

- **Produce** &mdash; a setter *raises* `PropertyChanged` so a binding refreshes when your code (or
  a two-way binding) changes a property. This is `[NotifyChanges]` / `[NotifyChangesMixin]`.
- **React** &mdash; autosave *listens* to `PropertyChanged` / `CollectionChanged` on nested objects
  so a change deep inside the object graph still persists. This is the `NotificationBinder` that
  `EnableAutosave()` attaches to a `NotifiyingJsonSettings`.

A full WPF-bindable, self-persisting settings class uses both: `[Autosave]` to save,
`[NotifyChanges]` (or the mixin) to notify, and a notifying base so autosave also catches nested
collection edits.

## Quick start

```csharp
using System.ComponentModel;
using Nucs.JsonSettings;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Examples;   // NotifiyingJsonSettings

[Autosave]        // persist on change
[NotifyChanges]   // raise PropertyChanged on change
public class WindowSettings : NotifiyingJsonSettings {
    public override string FileName { get; set; } = "window.json";

    public double Width  { get; set; }   // plain auto-property: binds two-way, saves, and notifies
    public double Height { get; set; }
    public string Title  { get; set; } = "Untitled";
}
```

```csharp
var settings = JsonSettings.Load<WindowSettings>("window.json").EnableAutosave();

// In XAML: <Window DataContext="{Binding}" Width="{Binding Width, Mode=TwoWay}" Title="{Binding Title}">
settings.Title = "Ready";     // the binding updates, and the file is written
```

No hand-written `OnPropertyChanged()` in any setter; the aspect weaves it in.

## The `NotifiyingJsonSettings` base

The library ships a small base class in the `Nucs.JsonSettings.Examples` namespace that adds the
event and a raiser:

```csharp
public abstract class NotifiyingJsonSettings : JsonSettings, INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    public virtual void OnPropertyChanged([CallerMemberName] string propertyName = null!)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

Deriving it is the intended way to make a settings class observable. You can raise notifications
three ways, in increasing order of convenience:

1. **By hand** &mdash; call `OnPropertyChanged()` from a manual setter (the classic pattern).
2. **`[NotifyChanges]`** &mdash; let the aspect raise it for every setter, so auto-properties notify too.
3. **`[NotifyChangesMixin]`** &mdash; skip the base entirely and have the interface injected (see below).

> [!NOTE]
> `JsonSettings` is itself an abstract base, and C# has single inheritance, so a settings class
> **cannot** also derive an external MVVM base such as CommunityToolkit's `ObservableObject` or
> Prism's `BindableBase`. `NotifiyingJsonSettings` fills that role; if you prefer your own base
> (or your framework's method names), see [Where the event comes from](#where-the-event-comes-from).

## Producing notifications &mdash; `[NotifyChanges]`

`[NotifyChanges]` appends a `PropertyChanged` raise to the end of every instance setter in the
class, at compile time. It closes the one gap the hand-written pattern leaves: an **auto-property**
saves (under `[Autosave]`) but never notifies, so a binding to it silently goes stale unless you
expand it into a manual `get`/`set` that calls `OnPropertyChanged()`. The aspect writes that call
for you.

```csharp
[Autosave, NotifyChanges]
public class AppSettings : NotifiyingJsonSettings {
    public override string FileName { get; set; } = "app.json";

    public string  Theme     { get; set; } = "Dark";   // notifies + saves, zero boilerplate
    public int     FontSize  { get; set; } = 12;
    public bool    Telemetry { get; set; }
}
```

Properties of the aspect:

- **Compile-time.** Woven by [AspectInjector](https://github.com/pamidur/aspect-injector); nothing
  is generated at runtime, so it is Native-AOT-safe (see [below](#native-aot-and-trimming)).
- **Composes with `[Autosave]`.** Both weave the same setter; one write saves once and notifies once.
- **Not inherited.** A setter is woven where it is *declared*, so every class in a hierarchy that
  declares notifiable properties needs its own `[NotifyChanges]` &mdash; the same rule as `[Autosave]`.
- **Applies to a class or a single property.** On the class it covers every setter; on one property
  it covers just that setter (and can [override the class guard](#per-class-and-per-property)).

> [!IMPORTANT]
> Put `[NotifyChanges]` on **auto-properties**. If you keep a hand-written setter that *already*
> calls `OnPropertyChanged()`, the woven call raises a **second** time. The aspect exists precisely so
> you no longer hand-write those setters &mdash; use one style or the other per property, not both.

### Where the event comes from

`[NotifyChanges]` *produces* the notification but does not *supply* the event. The class must already
own one, and the aspect finds the raiser like this:

| Your class&hellip; | What raises the event |
|---|---|
| derives `NotifiyingJsonSettings` | its `OnPropertyChanged` &mdash; the intended path |
| derives **your own** notifying base (`: JsonSettings, INotifyPropertyChanged` with a raiser) | resolved by **convention** on the method name |
| implements `INotifyPropertyChanged` inline with a raiser | same convention |
| implements nothing | nothing to call &mdash; use [`[NotifyChangesMixin]`](#no-base-class--notifychangesmixin) |

The recognised raiser method names are `OnPropertyChanged`, `RaisePropertyChanged`, and
`NotifyOfPropertyChange` (any accessibility, `void`, one `string` parameter) &mdash; the house styles of
CommunityToolkit.Mvvm / `ObservableObject`, Prism `BindableBase`, and Caliburn.Micro
`PropertyChangedBase` respectively. So if you roll a base that mimics your framework's naming, the
aspect still drives it:

```csharp
// Your own base, matching Prism's method name — no Prism dependency, just the convention.
public abstract class BindableSettings : JsonSettings, INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void RaisePropertyChanged(string propertyName)      // recognised by convention
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

[Autosave, NotifyChanges]
public class ProxySettings : BindableSettings {
    public override string FileName { get; set; } = "proxy.json";
    public string Host { get; set; }     // RaisePropertyChanged("Host") is called for you
}
```

> [!TIP]
> A class with **neither** a base nor a convention raiser is a harmless no-op: the setter runs, the
> value is set (and saved, if `[Autosave]`), and nothing is raised. Add a base, a raiser, or the
> mixin to start notifying.

## Change guards

By default a setter notifies **only when the value actually changes** &mdash; the same guard an
idiomatic `if (value == _field) return;` setter applies by hand. `NotificationGuard` lets you pick a
different policy, and it is a `[Flags]` enum so policies combine.

| `NotificationGuard` | Notifies when&hellip; |
|---|---|
| `OnlyChanged` *(default)* | the new value differs from the current one (`object.Equals`) |
| `SkipNullOrDefault` | the new value is not `null` / `default(T)` (`0`, `false`, `default(struct)`) |
| `Always` | every setter write, unconditionally &mdash; "any setter access" |

`Always` is value `0` (the absence of every filter), so combining it with anything is a no-op, which
is the intended reading. `OnlyChanged | SkipNullOrDefault` means *"notify only on a change to a
non-default value"*.

### Per-class and per-property

Set the default for a class with `[NotifyChanges(Guard = …)]` on the class, and override it for an
individual property by putting `[NotifyChanges(Guard = …)]` on that property &mdash; the property-level
guard wins.

```csharp
[Autosave, NotifyChanges]                                   // class default: OnlyChanged
public class SearchSettings : NotifiyingJsonSettings {
    public override string FileName { get; set; } = "search.json";

    public string Query { get; set; }                       // OnlyChanged (inherits the class default)

    [NotifyChanges(Guard = NotificationGuard.Always)]
    public int RefreshTick { get; set; }                    // fires on every write, even to the same value

    [NotifyChanges(Guard = NotificationGuard.OnlyChanged | NotificationGuard.SkipNullOrDefault)]
    public string? Filter { get; set; }                     // fires on a change to a non-null value
}
```

Behaviour in detail:

- **`OnlyChanged`** reads the property's **getter before** the assignment so it can compare old and
  new. That is why the aspect wraps the setter (`Kind.Around`) rather than merely appending to it.
  A **write-only** property has no getter to read and therefore behaves as `Always`.
- **Value types** compare by value: setting `int Count` from `5` back to `0` *is* a change under
  `OnlyChanged` (it fires), but is suppressed if you also add `SkipNullOrDefault` (0 is the default).
- **`SkipNullOrDefault`** looks only at the incoming value, so it needs no getter and works on
  write-only properties.

> [!NOTE]
> `OnlyChanged` reads the getter through reflection once per write. For settings &mdash; low-frequency,
> config-shaped writes &mdash; this is immaterial, but it is why the guard, like the serializer, needs
> the model preserved under trimming/AOT.

## Ignoring properties

`[NotifyChanges]` on a class weaves *every* setter, exactly as `[Autosave]` does. Silence one with
`[IgnoreNotify]` &mdash; the notification counterpart to
[`[IgnoreAutosave]`](autosave.md#what-is-monitored):

```csharp
[Autosave, NotifyChanges]
public class Settings : NotifiyingJsonSettings {
    public override string FileName { get; set; } = "s.json";

    public string Name { get; set; }                 // saves and notifies

    [IgnoreNotify]
    public string LastSavedBy { get; set; }          // saves, but never notifies

    [IgnoreAutosave]
    public string SearchText { get; set; }           // notifies (UI state), but is never saved
}
```

Persistence and notification opt-outs are **independent** &mdash; a property can do either, both, or
neither:

| Attribute(s) | Saves | Notifies |
|---|---|---|
| *(none)* | yes | yes |
| `[IgnoreNotify]` | yes | no |
| `[IgnoreAutosave]` | no | yes |
| `[IgnoreNotify]` + `[IgnoreAutosave]` | no | no |
| `[JsonIgnore]` | no (not serialised) | yes (if it has a setter) |

That a `[JsonIgnore]` property still notifies is deliberate: a **computed** value is the classic
`INotifyPropertyChanged` case. (A get-only computed property has no setter to weave, so raise it
yourself from wherever its inputs change.)

> [!NOTE]
> **Framework properties are excluded automatically.** `FileName`, `Modulation` and
> `IVersionable.Version` never notify and need no attribute. `FileName` matters especially because
> `Save()` assigns it internally (`o.FileName = <resolved path>`), so without this exclusion every
> autosave would raise a spurious `PropertyChanged("FileName")`. Indexers are excluded too &mdash;
> there is no single property name for them to carry.

## Composing with `[Autosave]`

`[Autosave]` and `[NotifyChanges]` (or `[NotifyChangesMixin]`) are meant to be used together and weave
the **same** setter. One assignment saves once and notifies once:

```csharp
[Autosave, NotifyChanges]
public class Settings : NotifiyingJsonSettings {
    public override string FileName { get; set; } = "s.json";
    public string Name { get; set; }
}

var s = JsonSettings.Load<Settings>("s.json").EnableAutosave();
s.Name = "x";   // -> one Save(), one PropertyChanged("Name")
```

- **No double save.** With a `NotifiyingJsonSettings` base, `EnableAutosave()` also attaches the
  `NotificationBinder`, which listens to the object's own `PropertyChanged`. When `[NotifyChanges]`
  raises it, the binder only **rebinds** nested collections; it does not save. So a scalar write saves
  exactly once, and reassigning a nested `ObservableCollection` saves once *and* rebinds the new
  instance for future edits &mdash; `[NotifyChanges]` actually makes that rebinding *more* reliable,
  because it fires even for a plain auto-property collection you never wrote an `OnPropertyChanged` for.
- **Different change semantics.** Autosave has no change-guard &mdash; it persists *every* monitored
  write, including one that assigns the current value again. `[NotifyChanges]`'s `OnlyChanged` (the
  default) is what de-dupes, so a no-op write can save without notifying.
- **Suspension is save-only.** [`SuspendAutosave()`](autosave.md#suspend-autosave) batches saves but
  does not suspend notifications &mdash; each write still raises `PropertyChanged` immediately, so the
  View stays live while the disk writes coalesce into one.
- **Independent opt-outs.** `[IgnoreAutosave]` and `[IgnoreNotify]` are separate; see
  [Ignoring properties](#ignoring-properties).
- **Loading and reloading.** The initial `Load()` does not notify (it runs before you subscribe). A
  later versioning reload repopulates the object through its setters *after* subscribers exist, so it
  *does* raise notifications &mdash; which is what you want, so the View refreshes to the reloaded
  values. Autosave still does not save during that reload.

> [!NOTE]
> The **order** of the save and the notification for a single write is not guaranteed (both aspects
> weave the setter at equal priority); only that each happens exactly once. Do not write a
> `PropertyChanged` handler that assumes the file is already on disk.

> [!WARNING]
> Do not put **both** `[NotifyChanges]` and `[NotifyChangesMixin]` on one class &mdash; the mixin
> already raises. And do not add `[NotifyChanges]` to a setter you *also* raise by hand; see the
> [note above](#producing-notifications--notifychanges).

## No base class &mdash; `[NotifyChangesMixin]`

`[NotifyChangesMixin]` uses AspectInjector's **mixin** to *inject* `INotifyPropertyChanged` into a
class that declares no notifying base, then raises that injected event from every setter. The class
does not name the interface in source; after the build it implements it. This is the fastest way to a
WPF-bindable settings object &mdash; no base type, no boilerplate, and `sealed` classes are fine.

```csharp
[Autosave]
[NotifyChangesMixin]                       // implements INotifyPropertyChanged for you
public sealed class AppSettings : JsonSettings {
    public override string FileName { get; set; } = "app.json";
    public string Name { get; set; }
    public int    Port { get; set; }
}

var s = JsonSettings.Load<AppSettings>("app.json").EnableAutosave();

// The interface is injected, so cast to subscribe:
((INotifyPropertyChanged) s).PropertyChanged += (_, e) => Console.WriteLine(e.PropertyName);
s.Name = "changed";                        // saved and notified  ->  prints "Name"
```

- **Per-instance.** The aspect is woven `Scope.PerInstance`, so each settings object owns its own
  event and subscriber list &mdash; exactly what an event on an ordinary object needs. (A global
  singleton would share one subscriber list across every instance; the mixin deliberately does not.)
- **Same guards.** `Guard` on `[NotifyChangesMixin]` behaves identically to `[NotifyChanges]`, and a
  per-property `[NotifyChanges(Guard = …)]` still overrides it.
- **Subscribe via a cast.** Because the interface is injected, your source sees a plain class; cast to
  `INotifyPropertyChanged` (or bind in XAML, which does the cast for you) to reach `PropertyChanged`.

> [!WARNING]
> Use the mixin on a **single** settings class. The interface can be injected only **once**, so a
> derived class cannot re-inject it, and a derived setter woven by the advice-only `[NotifyChanges]`
> cannot reach the base's injected event. For an **inheritance hierarchy**, derive
> `NotifiyingJsonSettings` and put `[NotifyChanges]` on each declaring class instead &mdash; every setter
> then raises through the one shared `OnPropertyChanged`. Do **not** combine the mixin with a class that
> already implements `INotifyPropertyChanged` (including `NotifiyingJsonSettings`); that is a duplicate
> implementation of the same interface.

> [!NOTE]
> **Boundary with nested-collection autosave.** `EnableAutosave()` only attaches the
> [`NotificationBinder`](#reacting-to-notifications--autosave-on-nested-changes) to a
> `NotifiyingJsonSettings`. A mixin-only class therefore saves on its own property writes but **not**
> when a nested `ObservableCollection` is mutated in place. If you need that, use the notifying base.

## Ways a settings class can be observable

Because `JsonSettings` occupies the base slot, "implementing the interface" has several shapes. This
is the full matrix &mdash; pick by how much you want to write and whether you have a hierarchy:

| # | Declaration | Notifies via | Best for |
|---|---|---|---|
| a | `: NotifiyingJsonSettings` + hand-written setters | your `OnPropertyChanged()` calls | fine control, a few properties |
| b | `: NotifiyingJsonSettings` + `[NotifyChanges]` | the aspect, through the base | most classes, incl. hierarchies |
| c | your own base `: JsonSettings, INotifyPropertyChanged` + `[NotifyChanges]` | the aspect, by [raiser convention](#where-the-event-comes-from) | matching your app's method names / adding base behaviour |
| d | `: JsonSettings, INotifyPropertyChanged` inline + a raiser + `[NotifyChanges]` | the aspect, by convention | one-off class that needs the interface named in source |
| e | `: JsonSettings` + `[NotifyChangesMixin]` | the injected event | a single/`sealed` class with no base |

And orthogonally, you can hand the object to consumers **typed as an interface** &mdash; useful for DI
seams and testing &mdash; with `EnableIAutosave`:

```csharp
public interface IAppSettings : INotifyPropertyChanged { string Name { get; set; } }

[Autosave, NotifyChanges]
public class AppSettings : NotifiyingJsonSettings, IAppSettings {
    public override string FileName { get; set; } = "app.json";
    public string Name { get; set; }
}

IAppSettings s = JsonSettings.Load<AppSettings>("app.json").EnableIAutosave<AppSettings, IAppSettings>();
```

`EnableIAutosave<TSettings, ISettings>()` enables autosave and returns the instance typed as your
interface. Since weaving replaced the old Castle interface proxy, this is now just a cast over the same
object &mdash; there is no separate proxy to keep in sync &mdash; but it remains a convenient way to expose
settings behind an abstraction.

## Reacting to notifications &mdash; autosave on nested changes

The other direction: when your settings object derives `NotifiyingJsonSettings`, `EnableAutosave()`
attaches a `NotificationBinder` that watches the object's own properties and their nested notifiers,
so autosave also fires when a change happens *inside* the graph, not just on a top-level setter.

Autosave fires when:

- a property that implements `INotifyPropertyChanged` raises `PropertyChanged`;
- a property that implements `INotifyCollectionChanged` &mdash; such as `ObservableCollection<T>` &mdash;
  raises `CollectionChanged`, **including a get-only collection you mutate in place**;
- a nested collection is **replaced** &mdash; the binder rebinds to the new instance automatically, so
  the replacement *and* subsequent mutations of it both save.

Objects nested *inside* an `ObservableCollection` (or any non-settings property) are **not** themselves
monitored &mdash; only the collection's own change events are.

```csharp
using System.Collections.ObjectModel;
using Nucs.JsonSettings;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Examples;

[Autosave]
public class HouseholdSettings : NotifiyingJsonSettings {
    public override string FileName { get; set; } = "household.json";

    private string _street = "Sesame Street 123";
    public string Street {
        get => _street;
        set { if (value == _street) return; _street = value; OnPropertyChanged(); }
    }

    public string AutoProperty { get; set; }

    [IgnoreAutosave]
    public string Scratch { get; set; }               // excluded from autosave

    private ObservableCollection<string> _residents = new();
    public ObservableCollection<string> Residents {
        get => _residents;
        set { if (Equals(value, _residents)) return; _residents = value; OnPropertyChanged(); }
    }

    [IgnoreAutosave]
    public ObservableCollection<object> Volatile { get; } = new();   // watched for the View, not saved

    public HouseholdSettings() { }
    public HouseholdSettings(string fileName) : base(fileName) { }
}
```

```csharp
var s = JsonSettings.Load<HouseholdSettings>("household.json").EnableAutosave();

s.Residents.Add("Cookie Monster");                 // saves — nested CollectionChanged
s.Residents = new ObservableCollection<string>();  // saves — property replaced
s.Residents.Add("Elmo");                           // saves — the new collection was rebound
s.Street += " (rear)";                             // saves — top-level setter
s.AutoProperty = "Hello";                          // saves — top-level setter
s.Scratch = "Hello";                               // does NOT save — [IgnoreAutosave]
s.Volatile.Add(new());                             // does NOT save — [IgnoreAutosave] on the collection
```

- **Requires `NotifiyingJsonSettings`.** A plain `JsonSettings` or a `[NotifyChangesMixin]`-only class
  saves on its own setters but not on nested collection edits (there is no `NotificationBinder`).
- **`[IgnoreAutosave]`** excludes a property from both save-on-set and nested watching.
- **Disposing the settings** unbinds every nested handler, so a collection held elsewhere cannot keep
  saving through &mdash; or keep alive &mdash; a disposed object.

## WPF binding patterns

- **DataContext.** Set a window's or control's `DataContext` to the loaded settings object and bind
  directly: `Text="{Binding Title, Mode=TwoWay}"`. Two-way writes go through the woven setter, which
  saves (`[Autosave]`) and re-notifies (`[NotifyChanges]`).
- **Collections.** Bind `ItemsControl.ItemsSource` to an `ObservableCollection<T>` property. Adds and
  removes update the UI and (with a notifying base) save.

> [!WARNING]
> **Threading.** Raise notifications on the UI thread while the object is bound. WPF marshals a simple
> scalar-property `PropertyChanged` for you, but mutating a bound `ObservableCollection` from a
> background thread throws. If you write settings off the UI thread, marshal through the `Dispatcher`,
> or update off-thread inside a [`SuspendAutosave`](autosave.md#suspend-autosave) scope and assign the
> result back on the UI thread.

> [!TIP]
> **Bursts (e.g. a slider).** Every bound write is a disk write. To coalesce a known burst into one
> save, wrap it in `using (settings.SuspendAutosave()) { … }`. Interval-based throttling is a planned
> feature; see [Autosave &rsaquo; Throttled Save](autosave.md#throttled-save).

> [!NOTE]
> **`SettingsBag` is not a ViewModel.** The dynamic [`SettingsBag`](dynamic-settings-bag.md) has its
> own dictionary-backed autosave but does **not** implement `INotifyPropertyChanged`, so it does not
> raise per-key change notifications for binding. Use a typed `JsonSettings` class for WPF.

## Native AOT and trimming

The notification aspects emit **no runtime code** and are AOT-safe, exactly like `[Autosave]`. The
reflection they do at runtime &mdash; reading a getter for `OnlyChanged`, locating a raiser method, reading
the guard attribute &mdash; needs the settings model preserved under trimming, the same requirement the
Newtonsoft serializer underneath already imposes. The boxed-default comparison used by
`SkipNullOrDefault` is written to avoid `Activator`/`Reflection.Emit`, so it introduces no new AOT
constraint. See [`docs/AOT.md`](https://github.com/Nucs/JsonSettings/blob/master/docs/AOT.md).

## Strong-named consumers

IL weaving rewrites the compiled assembly after it is signed, invalidating the strong-name signature.
The `Nucs.JsonSettings.Autosave` package ships MSBuild targets that re-sign your assembly with your own
`AssemblyOriginatorKeyFile` after the weave; the notification aspects run in that **same** AspectInjector
pass, so they are re-signed automatically with no extra configuration. See
[Autosave &rsaquo; Strong-named consumers](autosave.md#strong-named-consumers) for the full note and the
`NucsAutosaveResignAfterWeaving` opt-out.

## Comparison with other approaches

These aspects are deliberately small and settings-focused; they are not a general MVVM framework. Where
they sit next to the usual `INotifyPropertyChanged` tooling:

| Approach | Mechanism | Auto-property notify | Adds the interface | AOT / trim |
|---|---|---|---|---|
| Hand-written `OnPropertyChanged()` | source | no (one per setter) | you write it | yes |
| **`[NotifyChanges]`** | IL weave (AspectInjector) | yes | no (needs a base/convention) | yes |
| **`[NotifyChangesMixin]`** | IL weave + mixin | yes | yes | yes |
| Fody `PropertyChanged` | IL weave (Fody) | yes | yes | weaver-dependent |
| CommunityToolkit.Mvvm `[ObservableProperty]` | source generator | yes (on a partial field) | via `ObservableObject` base | yes |
| ReactiveUI `RaiseAndSetIfChanged` | runtime helper | no (explicit per setter) | via `ReactiveObject` base | yes |
| PostSharp / Metalama `[NotifyPropertyChanged]` | IL weave / source | yes | yes | product-dependent |

The distinguishing point: `[NotifyChanges]` / `[NotifyChangesMixin]` reuse the **same weaving pipeline
as `[Autosave]`**, so one attribute set covers save *and* notify with no additional dependency, no
source-generator partial-class requirement, and the same Native-AOT and strong-naming story as the rest
of the package. Because `JsonSettings` takes the base slot, the source-generator and base-class
approaches that require *their* base (`ObservableObject`, `ReactiveObject`) can't be the settings base
directly; the convention resolver bridges to their method *names* instead.

## Troubleshooting

| Symptom | Cause & fix |
|---|---|
| Property saves but the UI does not update | The setter notifies nothing. Add `[NotifyChanges]` (with a notifying base) or `[NotifyChangesMixin]`. |
| `PropertyChanged` fires twice for one change | The setter is hand-written *and* `[NotifyChanges]` is applied. Make it an auto-property, or drop the attribute for that property. |
| `[NotifyChanges]` seems to do nothing | No event owner. Derive `NotifiyingJsonSettings`, expose a convention raiser, or use `[NotifyChangesMixin]`. |
| A property should save but stay silent (or notify but not save) | Use `[IgnoreNotify]` / `[IgnoreAutosave]` &mdash; they are independent. |
| `FileName` fires notifications | It does not &mdash; `FileName`/`Modulation`/`Version` are framework-managed and excluded. |
| Mixin class does not save on `collection.Add(...)` | `NotificationBinder` needs `NotifiyingJsonSettings`. Use the notifying base for nested-collection autosave. |
| `EnableAutosave()` throws "not marked `[Autosave]`" | Autosave and notifications are separate. Add `[Autosave]` too (both are needed to save *and* notify). |
| `NotSupportedException` mutating a bound collection | Cross-thread collection change. Marshal to the `Dispatcher`, or edit inside `SuspendAutosave` and assign on the UI thread. |
| Notification fires when a value is set to the same thing | The guard is `Always` (or a per-property override). Use `OnlyChanged` (the default). |

## Requirements

- Install the `Nucs.JsonSettings.Autosave` package.
- To **produce** notifications: derive `NotifiyingJsonSettings` (or expose a convention raiser) and add
  `[NotifyChanges]`; **or** add `[NotifyChangesMixin]` with no base.
- To **save** as well: add `[Autosave]` and call `EnableAutosave()` after `Load`.
- To have autosave **react** to nested collection/property changes: derive `NotifiyingJsonSettings`.
- `[NotifyChanges]` and `[Autosave]` are **not inherited** &mdash; mark every declaring class in a hierarchy.
- Silence a property with `[IgnoreNotify]` (notifications) or `[IgnoreAutosave]` (saving); the two are
  independent. Framework `FileName` / `Modulation` / `Version` never notify.

## See also

- [Autosave](autosave.md) &mdash; persisting on change, suspension, reentrancy, strong-naming.
- [Dynamic Settings Bag](dynamic-settings-bag.md) &mdash; the key/value alternative (no `INotifyPropertyChanged`).
- [Modulation API](modulation-api.md) &mdash; how modules such as `AutosaveModule` attach to an object.
